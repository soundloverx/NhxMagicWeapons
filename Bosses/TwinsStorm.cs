using System.Collections.Generic;
using NhxMagicWeapons.Staves;
using NhxMagicWeapons.Staves.StaffOfStorms;
using NhxMagicWeapons.Staves.StaffOfStorms.Effects;
using UnityEngine;
using UnityEngine.Rendering;
using Storms = NhxMagicWeapons.Staves.StaffOfStorms.Staff;

namespace NhxMagicWeapons.Bosses
{
    // The Stormcaller's second phase (TwinsPhase; Claude's pick, taken by the user 2026-11-15: "go with your pick"): the
    // Eye of the Storm's cloud gathers over the player it isn't hunting (its twin's quarry, the idle one when both hunt
    // the same player, or its one player when it watches) and follows them for Duration, raining the storm's lighter bolts
    // on small runes around them (ElementRune, RuneDelay of warning), in turn at their feet and where they're heading, so
    // they have to keep moving. A bolt from the twin's hands up into the cloud as it calls it.
    // Networked: every game draws the cloud over the player (StormCloud), flashing as each rune strikes; the twin's game
    // (its owner, which creates it) places the runes, deals their hits and removes it
    internal class TwinsStorm : MonoBehaviour
    {
        private const string PrefabName = "NhxStormcallerStorm";
        private const string RainRuneName = "NhxStormRainRune";
        private const float Duration = 8f;
        private const float RainInterval = 0.5f;
        // The runes: warning, size, and how far ahead of a moving player (their speed times this share of the warning) or
        // around a still one the odd ones fall
        internal const float RuneDelay = 1.2f;
        internal const float RuneRadius = 1.8f;
        private const float Lead = 0.8f;
        private const float AroundMin = 1.5f;
        private const float AroundMax = 4f;
        private const float StillSpeed = 1f;
        // Each rune's hit: lightning, no stagger (one after another, it would stun-lock), dodged by a roll, not blocked
        private const float RainDamage = 90f;
        // The cloud: how high over the player, how wide, how fast it catches up; how bright a rune's strike lights it
        private const float CloudHeight = 14f;
        private const float CloudRadius = 6f;
        private const float Smoothing = 4f;
        private const float RuneFlash = 0.6f;
        private const int LookLevel = 20;

        private static readonly KeyValuePair<int, int> s_targetKey = ZDO.GetHashZDOID("nhx_twinstorm_target");
        // A count of its runes, for every game's cloud to flash as they strike
        private static readonly int s_tickKey = "nhx_twinstorm_tick".GetStableHashCode();

        private static GameObject s_prefab;
        private static GameObject s_rainRune;

        private ZNetView m_nview;
        private Player m_target;
        private float m_age;
        // On the creating game
        private Character m_caster;
        private float m_rainTimer;
        private int m_ticks;
        // On every game but a dedicated server
        private StormCloud m_cloud;
        private Vector3 m_cloudAt;
        private int m_seenTick;
        private readonly List<float> m_flashes = new List<float>();

        // The storm and its runes, networked (CallerBosses, after BossAttacks.AddRunes)
        internal static void AddPrefabs()
        {
            if (s_prefab)
            {
                return;
            }
            s_rainRune = BossAttacks.AddRune(RainRuneName, false, RuneDelay, RuneRadius, Storms.RainBolt);
            s_prefab = new GameObject(PrefabName);
            s_prefab.transform.SetParent(StaffEffects.Container, false);
            s_prefab.AddComponent<ZNetView>();
            s_prefab.AddComponent<TwinsStorm>();
            Jotunn.Managers.PrefabManager.Instance.AddPrefab(s_prefab);
        }

        // On the twin's owner's game: the storm over `target`, from `caster`
        internal static void Create(Character caster, Player target)
        {
            if (!s_prefab || !caster || !target)
            {
                return;
            }
            TwinsStorm storm = Instantiate(s_prefab, target.transform.position, Quaternion.identity).GetComponent<TwinsStorm>();
            storm.m_caster = caster;
            storm.m_target = target;
            storm.m_nview.GetZDO().Set(s_targetKey, target.GetZDOID());
            Vector3 hands = BossHands.Between(caster, caster.GetCenterPoint());
            if (LightningStep.s_bolt)
            {
                LightningBolt.Arc(LightningStep.s_bolt, caster, hands, target.transform.position + Vector3.up * CloudHeight, LookLevel);
            }
            if (Storms.Crackle)
            {
                StaffEffects.Create(new[] { Storms.Crackle }, hands, Quaternion.identity);
            }
        }

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
        }

        private void OnDestroy()
        {
            if (m_cloud)
            {
                m_cloud.End();
            }
        }

        private void Update()
        {
            if (!m_nview || !m_nview.IsValid())
            {
                return;
            }
            float dt = Time.deltaTime;
            m_age += dt;
            Player target = Target();
            if (m_nview.IsOwner())
            {
                if (m_age >= Duration || !m_caster || m_caster.IsDead() || !target || target.IsDead())
                {
                    ZNetScene.instance.Destroy(gameObject);
                    return;
                }
                m_rainTimer -= dt;
                if (m_rainTimer <= 0f)
                {
                    m_rainTimer += RainInterval;
                    Rain(target);
                }
            }
            if (target && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                Draw(target, dt);
            }
        }

        private Player Target()
        {
            if (m_target)
            {
                return m_target;
            }
            ZDOID id = m_nview.GetZDO().GetZDOID(s_targetKey);
            GameObject found = id.IsNone() || !ZNetScene.instance ? null : ZNetScene.instance.FindInstance(id);
            m_target = found ? found.GetComponent<Player>() : null;
            return m_target;
        }

        // A rune by the player: at their feet, then where they're heading (or around them, standing still), and so on
        private void Rain(Player target)
        {
            Vector3 point = target.transform.position;
            if (m_ticks % 2 == 1)
            {
                Vector3 velocity = target.GetVelocity();
                velocity.y = 0f;
                if (velocity.magnitude > StillSpeed)
                {
                    point += velocity * (RuneDelay * Lead);
                }
                else
                {
                    point += Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * Vector3.forward * Random.Range(AroundMin, AroundMax);
                }
            }
            ElementRune rune = s_rainRune ? ElementRune.Create(s_rainRune, LightningStrike.Ground(point), RuneDelay, RuneRadius) : null;
            if (rune)
            {
                rune.SetHit(m_caster, Hit());
            }
            m_ticks++;
            m_nview.GetZDO().Set(s_tickKey, m_ticks);
        }

        private HitData Hit()
        {
            HitData hit = new HitData();
            hit.m_damage.m_lightning = RainDamage;
            hit.m_blockable = false;
            hit.m_dodgeable = true;
            hit.m_ranged = true;
            hit.m_staggerMultiplier = 0f;
            hit.m_hitType = HitData.HitType.EnemyHit;
            hit.SetAttacker(m_caster);
            return hit;
        }

        private void Draw(Player target, float dt)
        {
            Vector3 above = target.transform.position + Vector3.up * CloudHeight;
            if (!m_cloud)
            {
                if (!Storms.CloudMaterial)
                {
                    return;
                }
                m_cloud = StormCloud.Create(Storms.CloudMaterial, above, CloudRadius);
                m_cloudAt = above;
            }
            m_cloudAt = Vector3.Lerp(m_cloudAt, above, 1f - Mathf.Exp(-Smoothing * dt));
            m_cloud.Follow(m_cloudAt);
            // Each rune placed lights the cloud as it strikes
            int tick = m_nview.GetZDO().GetInt(s_tickKey);
            while (m_seenTick < tick)
            {
                m_seenTick++;
                m_flashes.Add(Time.time + RuneDelay);
            }
            for (int i = m_flashes.Count - 1; i >= 0; i--)
            {
                if (Time.time >= m_flashes[i])
                {
                    m_cloud.Flash(RuneFlash);
                    m_flashes.RemoveAt(i);
                }
            }
        }
    }
}
