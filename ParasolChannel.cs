using System;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NhxMagicWeapons
{
    // The Springbloom Parasol's shield with the Lightcaller set, the mage tank the user designed (2026-11-28; the design
    // doc "Lightcaller tank: the Springbloom Parasol", https://claude.ai/artifact/WdM8FaMHHk7gmpaexTa49A). This is its
    // first step (the user, 2026-11-28: "yes, go ahead with step 1 and the spin"): the channel's frame, without what it
    // does to damage yet.
    // - Without all four Lightcaller pieces on, neither of the parasol's attacks starts and no eitr goes: a message says
    //   so instead (CheckSet).
    // - With them, its main attack is a channel: the Staff of Frost's looping attack, held forward for as long as the
    //   button is down, as the Wrath of Muspelheim's flamethrower is. The user picked its pose over holding a moment of
    //   the sword thrust (2026-11-28: "the parasol looks good in the staff pose"). It shoots nothing: every DrainInterval
    //   it takes its share of ChannelEitr instead (Drain), and ends when there's none left or the button is let go. At
    //   ChannelSpeed of your speed, turning freely.
    // - Magic Supremacy's vortex swirls around the player for as long as the channel lasts, and the parasol turns around
    //   its shaft (the user, 2026-11-28: "Can the parasol rotate around its shaft during the channel? I feel like that
    //   would be a cute touch"), speeding up and slowing down over s_spinUp seconds. Every game shows both: the
    //   channelling player's game tells the others through its player's ZDO.
    // The secondary attack stays the parasol's petal buff until the design's sigils replace it.
    [HarmonyPatch]
    internal class ParasolChannel : MonoBehaviour
    {
        // The item (PrefabItems), as a prefab and by its name
        internal const string ItemPrefab = "NhxSpringbloomParasol";
        private const string ItemName = "$item_nhx_springbloomparasol";
        // The Lightcaller set's pieces, by name (PrefabItems)
        private static readonly string[] SetPieces =
        {
            "$item_nhx_lightcaller_hood", "$item_nhx_lightcaller_vestments", "$item_nhx_lightcaller_legwraps", "$item_nhx_lightcaller_cape",
        };
        private const string NoLightMessage = "$msg_nhx_no_light";

        // The channel: the Staff of Frost's attack (staff_rapidfire, looping while held), its eitr a second (before the
        // game's discount for skill, a third off at 100: Staves\AttackEitr), taken in steps this far apart, and how fast
        // you move meanwhile, as a share of your speed
        private const string LoopSource = "StaffIceShards";
        private const float ChannelEitr = 10f;
        private const float DrainInterval = 0.1f;
        private const float ChannelSpeed = 0.5f;

        // Magic Supremacy's vortex, the Parasol's shield's own (SE_WindcallerShield_DO's start effect): a copy each game
        // makes on its own (CreateVortex). When the channel ends its particles stop coming and those there fade over their
        // lifetime, while its light dims over VortexDim
        private const string VortexSource = "vfx_windcaller_staff_shield_sphere_DO";
        private const string VortexName = "NhxParasolVortex";
        private const float VortexLinger = 2f;
        private const float VortexDim = 0.3f;

        // The spin: turns a second (negative: the other way), and the seconds it takes to get there from still and back.
        // Tried with nhx_parasolspin, to be fixed once picked
        internal static float s_spinTurns = 1f;
        internal static float s_spinUp = 0.5f;

        private static readonly int s_channelKey = "nhx_parasol_channel".GetStableHashCode();
        private static readonly int s_itemHash = ItemPrefab.GetStableHashCode();
        private static GameObject s_vortex;

        // Every player's, on every game
        private Player m_player;
        private ZNetView m_nview;
        private GameObject m_vortex;
        private float m_spinSpeed;
        private float m_spinAngle;
        // The parasol in hand being spun (VisEquipment's copy of its attach), its model and how it sits unspun
        private GameObject m_spinItem;
        private Transform m_spinModel;
        private Quaternion m_spinBase;

        // On the parasol's item, as PrefabItems makes it
        internal static void Add(ItemDrop.ItemData.SharedData shared)
        {
            GameObject frost = PrefabManager.Cache.GetPrefab<GameObject>(LoopSource);
            Attack loop = frost && frost.GetComponent<ItemDrop>() ? frost.GetComponent<ItemDrop>().m_itemData.m_shared.m_attack : null;
            if (loop == null)
            {
                Plugin.Log.LogError($"The game has no {LoopSource}: the Springbloom Parasol keeps Magic Supremacy's shield.");
                return;
            }
            Attack channel = loop.Clone();
            channel.m_loopingAttack = true;
            channel.m_attackProjectile = null;
            channel.m_perBurstResourceUsage = true;
            channel.m_burstInterval = DrainInterval;
            channel.m_projectileBursts = int.MaxValue;
            channel.m_attackEitr = ChannelEitr * DrainInterval;
            channel.m_attackStamina = 0f;
            channel.m_attackHealth = 0f;
            channel.m_attackHealthPercentage = 0f;
            channel.m_attackUseAdrenaline = 0f;
            channel.m_raiseSkillAmount = 0f;
            channel.m_speedFactor = ChannelSpeed;
            channel.m_speedFactorRotation = 1f;
            // The Staff of Frost's sounds and sparks
            channel.m_startEffect = new EffectList();
            channel.m_triggerEffect = new EffectList();
            channel.m_burstEffect = new EffectList();
            channel.m_trailStartEffect = new EffectList();
            channel.m_hitEffect = new EffectList();
            channel.m_hitTerrainEffect = new EffectList();
            shared.m_attack = channel;
            // The game would charge the petal buff the channel's eitr (Staves\AttackEitr): its own, Magic Supremacy's 80
            Staves.AttackEitr.Add(shared);
            s_vortex = CreateVortex();
        }

        // Without its ZNetView: each game makes and removes its own, on its own players
        private static GameObject CreateVortex()
        {
            GameObject source = Plugin.Assets317.LoadAsset<GameObject>(VortexSource);
            if (!source)
            {
                Plugin.Log.LogWarning($"{VortexSource} isn't in the asset bundle: the Springbloom Parasol's channel has no vortex.");
                return null;
            }
            GameObject vortex = PrefabManager.Instance.CreateClonedPrefab(VortexName, source);
            Object.DestroyImmediate(vortex.GetComponent<ZSyncTransform>());
            Object.DestroyImmediate(vortex.GetComponent<ZNetView>());
            return vortex;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Player), nameof(Player.Awake))]
        private static void AddToPlayer(Player __instance)
        {
            __instance.gameObject.AddComponent<ParasolChannel>();
        }

        // The local player's attacks with the parasol, without the set: none
        [HarmonyPrefix, HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static bool CheckSet(Humanoid __instance, ref bool __result)
        {
            if (__instance != Player.m_localPlayer)
            {
                return true;
            }
            ItemDrop.ItemData weapon = __instance.GetCurrentWeapon();
            if (weapon == null || weapon.m_shared.m_name != ItemName || HasSet(__instance))
            {
                return true;
            }
            __instance.Message(MessageHud.MessageType.Center, NoLightMessage);
            __result = false;
            return false;
        }

        private static bool HasSet(Humanoid humanoid)
        {
            return IsPiece(humanoid.m_helmetItem) && IsPiece(humanoid.m_chestItem) && IsPiece(humanoid.m_legItem) && IsPiece(humanoid.m_shoulderItem);
        }

        private static bool IsPiece(ItemDrop.ItemData item)
        {
            return item != null && Array.IndexOf(SetPieces, item.m_shared.m_name) >= 0;
        }

        // The channel's bursts, which would shoot: they only take their eitr, as the game does for its own (Attack's
        // per-burst resource use), and end the channel when there isn't enough
        [HarmonyPrefix, HarmonyPatch(typeof(Attack), nameof(Attack.FireProjectileBurst))]
        private static bool Drain(Attack __instance)
        {
            if (!IsChannel(__instance))
            {
                return true;
            }
            float eitr = __instance.GetAttackEitr();
            if (eitr > 0f)
            {
                if (!__instance.m_character.HaveEitr(eitr))
                {
                    __instance.Stop();
                    return false;
                }
                __instance.m_character.UseEitr(eitr);
            }
            return false;
        }

        private static bool IsChannel(Attack attack)
        {
            return attack != null && attack.m_loopingAttack && attack.m_weapon != null && attack.m_weapon.m_shared.m_name == ItemName;
        }

        // On its own game: whether the player channels now
        private static bool Channelling(Player player)
        {
            Attack attack = player.m_currentAttack;
            return attack != null && !attack.IsDone() && !player.m_currentAttackIsSecondary && IsChannel(attack);
        }

        private void Awake()
        {
            m_player = GetComponent<Player>();
            m_nview = GetComponent<ZNetView>();
        }

        private void Update()
        {
            if (!m_nview || !m_nview.IsValid())
            {
                return;
            }
            ZDO zdo = m_nview.GetZDO();
            bool channelling;
            if (m_nview.IsOwner())
            {
                channelling = Channelling(m_player);
                if (zdo.GetBool(s_channelKey) != channelling)
                {
                    zdo.Set(s_channelKey, channelling);
                }
            }
            else
            {
                channelling = zdo.GetBool(s_channelKey);
            }
            if (ZNet.instance && ZNet.instance.IsDedicated())
            {
                return;
            }
            UpdateVortex(channelling);
            UpdateSpin(channelling, Time.deltaTime);
        }

        private void UpdateVortex(bool channelling)
        {
            if (channelling && !m_vortex && s_vortex)
            {
                // Where the effect puts it: about the middle of the body
                m_vortex = Instantiate(s_vortex, transform, false);
                m_vortex.SetActive(true);
            }
            else if (!channelling && m_vortex)
            {
                m_vortex.AddComponent<VortexFade>().m_dim = VortexDim;
                Destroy(m_vortex, VortexLinger);
                m_vortex = null;
            }
        }

        private void UpdateSpin(bool channelling, float dt)
        {
            VisEquipment equipment = m_player.m_visEquipment;
            GameObject item = equipment && equipment.m_currentRightItemHash == s_itemHash ? equipment.m_rightItemInstance : null;
            if (item != m_spinItem)
            {
                m_spinItem = item;
                MeshFilter model = item ? item.GetComponentInChildren<MeshFilter>(true) : null;
                m_spinModel = model ? model.transform : null;
                if (m_spinModel)
                {
                    m_spinBase = m_spinModel.localRotation;
                }
                m_spinAngle = 0f;
                m_spinSpeed = 0f;
            }
            float top = s_spinTurns * 360f;
            m_spinSpeed = Mathf.MoveTowards(m_spinSpeed, channelling ? top : 0f, Mathf.Abs(top) / Mathf.Max(s_spinUp, 0.01f) * dt);
            if (m_spinModel && m_spinSpeed != 0f)
            {
                // Its shaft runs along the model's z, through its origin (Tools\fold_parasol.py)
                m_spinAngle = Mathf.Repeat(m_spinAngle + m_spinSpeed * dt, 360f);
                m_spinModel.localRotation = m_spinBase * Quaternion.AngleAxis(m_spinAngle, Vector3.forward);
            }
        }

        // The vortex once the channel ends: no more particles, and its light dimmed to nothing over m_dim seconds (its
        // LightFlicker would hold it up)
        private class VortexFade : MonoBehaviour
        {
            public float m_dim;
            private Light[] m_lights;
            private float[] m_intensity;
            private float m_age;

            private void Start()
            {
                foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>())
                {
                    particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
                foreach (LightFlicker flicker in GetComponentsInChildren<LightFlicker>())
                {
                    flicker.enabled = false;
                }
                m_lights = GetComponentsInChildren<Light>();
                m_intensity = new float[m_lights.Length];
                for (int i = 0; i < m_lights.Length; i++)
                {
                    m_intensity[i] = m_lights[i].intensity;
                }
            }

            private void Update()
            {
                m_age += Time.deltaTime;
                float left = 1f - Mathf.Clamp01(m_age / Mathf.Max(m_dim, 0.01f));
                for (int i = 0; i < m_lights.Length; i++)
                {
                    if (m_lights[i])
                    {
                        m_lights[i].intensity = m_intensity[i] * left;
                    }
                }
            }
        }
    }
}
