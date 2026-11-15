using System.Collections.Generic;
using HarmonyLib;
using NhxMagicWeapons.Staves;
using NhxMagicWeapons.Staves.StaffOfStorms;
using NhxMagicWeapons.Staves.WrathOfMuspelheim;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // On each boss (CallerBosses), on every game:
    // - Its armor's level, which the legendary looks go by (Armor\ArmorLegend: particles from 12, the cloak from 15, the
    //   signatures at 20...): its pieces' level, set from its ZDO (20 unless nhx_boss says otherwise). Every game gives a
    //   monster its default items itself, at level 1; the owner's are the ones that count, and ArmorLegend shares the
    //   set's level from there
    // - Its set glowing fully and crackling, as with the set's staff in hand (Armor\ArmorGlow.Wielding asks Empowered)
    // - Its legs' pace: the player's walk and jog animations are picked by the speed the game gives them (forward_speed,
    //   sideway_speed, in m/s), and its legs are twice as long, so at twice a player's speed the animations are given
    //   the player's (StrideScale), and its feet stay on the ground
    // And on the game that runs it (its owner): its part in the twins' fight (TwinsAI picks its role and target every
    // RoleInterval), its answer to a player drawing its staff (RPC_Punish: TwinsDuel), its bond with its twin
    // (TwinsBond: raising it if it falls), and its ward against its own element's staff (Ward: TwinsRules)
    internal class CallerBoss : MonoBehaviour
    {
        internal const int DefaultLevel = 20;
        internal static readonly int LevelKey = "nhx_boss_level".GetStableHashCode();
        internal const string PunishRpc = "NhxTwinsPunish";
        private const string WardRpc = "NhxTwinsWard";

        // Its element (Element: the staff whose drawing it answers)
        public int m_element;

        internal enum Role
        {
            None,
            // After the player who drew its staff last, or alongside its twin
            Hunter,
            // Alone with one player who holds the other staff (or none): keeping back, slow spells only (TwinsAI)
            Watcher,
            // A player the elements have judged: standing, facing them, channelling (TwinsJudgment draws and deals it)
            Judging,
        }

        // It lands this far short of the player it rushes at (meters, from their middle), and its rush shows as the staff's
        // look at this level
        private const float ArriveGap = 4f;
        private const int RushLevel = 20;
        private const float RoleInterval = 0.5f;
        // A hunter whose quarry has stayed out of reach (meters: a little past the Firecaller's flames, the Stormcaller's
        // bolt) for ChaseDelay seconds rushes at it, as at a player who drew its staff, at most every RushCooldown (the
        // user, 2026-11-14: "the bosses might want to blink closer from time to time anyway"): a player can outrun them
        // (sprinting, 7 m/s; they run at 5), or stand where they can't walk
        private const float FireReach = 14f;
        private const float StormReach = 26f;
        private const float ChaseDelay = 4f;
        private const float RushCooldown = 10f;
        // Where a chasing Stormcaller lands: well within its bolt's reach, not at arm's length (a caster; the Firecaller, and
        // both when punishing a swap, land at ArriveGap)
        private const float StormChaseGap = 12f;
        // Its ward shows at most this often (seconds), and for a hit through its middle (a blast, Lightning Step, the
        // comet: nearer its axis than WardNear), this far from it toward whoever struck (meters)
        private const float WardEvery = 0.15f;
        private const float WardNear = 0.3f;
        private const float WardReach = 1f;

        // Every boss on this game (BossCommand and the fight look for them here)
        internal static readonly List<CallerBoss> All = new List<CallerBoss>();
        // Its fight was left behind: it goes as it loads (Awake, Start)
        internal bool m_abandoned;
        private static readonly HashSet<VisEquipment> s_equipment = new HashSet<VisEquipment>();
        private static readonly Dictionary<ZSyncAnimation, Transform> s_animations = new Dictionary<ZSyncAnimation, Transform>();
        internal static readonly Dictionary<BaseAI, CallerBoss> ByAI = new Dictionary<BaseAI, CallerBoss>();
        private static readonly Dictionary<Character, CallerBoss> s_twins = new Dictionary<Character, CallerBoss>();

        // Whether this character is one of the twins (TwinsRules)
        internal static bool IsTwin(Character character) => character && s_twins.ContainsKey(character);

        // The twin this character is, or null: without a GetComponent, for what asks every frame or every hit (BossBars,
        // TwinsRules)
        internal static CallerBoss Of(Character character)
        {
            return character && s_twins.TryGetValue(character, out CallerBoss boss) ? boss : null;
        }

        internal ZNetView m_nview;
        internal MonsterAI m_ai;
        internal Humanoid m_humanoid;
        // Its second phase (TwinsPhase)
        internal TwinsPhase m_phase;
        // On its owner's game: its role and whom it goes after (null: as its AI would)
        internal Role m_role;
        internal Character m_target;
        private VisEquipment m_equipment;
        private ZSyncAnimation m_animation;
        private int m_level;
        private float m_roleTimer;
        private float m_outOfReach;
        private float m_nextRush;
        private float m_lastWard = -WardEvery;

        internal Element Element => (Element)m_element;

        // A boss's equipment: its set glows as with its staff in hand
        internal static bool Empowered(VisEquipment equipment) => s_equipment.Contains(equipment);

        // How far the nearest living boss (of `element`, or any for None) is from `point`, and which: infinity for none
        internal static float NearestLiving(Vector3 point, Element element, out CallerBoss nearest)
        {
            nearest = null;
            float best = float.PositiveInfinity;
            foreach (CallerBoss boss in All)
            {
                if (!boss || !boss.m_humanoid || boss.m_humanoid.IsDead() || !boss.m_nview || !boss.m_nview.IsValid()
                    || (element != Element.None && boss.Element != element))
                {
                    continue;
                }
                float distance = Vector3.Distance(point, boss.transform.position);
                if (distance < best)
                {
                    best = distance;
                    nearest = boss;
                }
            }
            return best;
        }

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            m_ai = GetComponent<MonsterAI>();
            m_humanoid = GetComponent<Humanoid>();
            m_equipment = GetComponent<VisEquipment>();
            m_animation = GetComponent<ZSyncAnimation>();
            m_phase = GetComponent<TwinsPhase>();
            // A fight left behind ends as it loads again, before anything of it happens (TwinsRules.LeftBehind): kept out
            // of the twins the fight goes by (All: holds, the players' side, the spells), not shown as a boss (its bar
            // would bring the fight's music), its AI held (TwinsAI) and nothing of its own run (Update, TwinsPhase), then
            // gone in Start
            m_abandoned = m_nview && m_nview.GetZDO() != null && TwinsRules.LeftBehind(m_nview.GetZDO());
            if (m_abandoned)
            {
                if (m_humanoid)
                {
                    m_humanoid.m_boss = false;
                }
            }
            else
            {
                All.Add(this);
            }
            if (m_humanoid)
            {
                s_twins[m_humanoid] = this;
            }
            if (m_equipment)
            {
                s_equipment.Add(m_equipment);
            }
            if (m_animation)
            {
                s_animations[m_animation] = transform;
            }
            if (m_ai)
            {
                ByAI[m_ai] = this;
            }
            if (m_nview)
            {
                m_nview.Register<ZDOID>(PunishRpc, RPC_Punish);
                m_nview.Register<Vector3>(WardRpc, RPC_Ward);
                TwinsBond.Register(this);
            }
        }

        // After every component's Awake (its health is set up): a twin just raised comes back hurt (TwinsBond)
        private void Start()
        {
            if (m_abandoned)
            {
                // Whichever game loads it first sends it away (only a ZDO's owner can destroy it)
                if (m_nview && m_nview.IsValid())
                {
                    if (!m_nview.IsOwner())
                    {
                        m_nview.ClaimOwnership();
                    }
                    TwinsRules.Vanish(this, "its fight was left (nobody in it before it unloaded)");
                }
                return;
            }
            if (m_nview && m_nview.IsValid() && m_nview.IsOwner() && m_humanoid)
            {
                TwinsBond.OnStart(this);
            }
        }

        private void OnDestroy()
        {
            All.Remove(this);
            if (m_humanoid)
            {
                s_twins.Remove(m_humanoid);
            }
            if (m_equipment)
            {
                s_equipment.Remove(m_equipment);
            }
            if (m_animation)
            {
                s_animations.Remove(m_animation);
            }
            if (m_ai)
            {
                ByAI.Remove(m_ai);
            }
        }

        private void Update()
        {
            if (m_abandoned || !m_nview || !m_nview.IsValid() || !m_humanoid)
            {
                return;
            }
            UpdateLevel();
            if (!m_nview.IsOwner() || m_humanoid.IsDead())
            {
                m_role = Role.None;
                m_target = null;
                return;
            }
            m_roleTimer -= Time.deltaTime;
            if (m_roleTimer <= 0f)
            {
                m_roleTimer = RoleInterval;
                if (TwinsRules.CheckAbandoned(this))
                {
                    return;
                }
                TwinsAI.UpdateRole(this);
                TwinsBond.Update(this);
                Chase();
            }
        }

        // A hunter rushes at a quarry that stays out of reach (every RoleInterval)
        private void Chase()
        {
            Character target = m_target;
            if (m_role != Role.Hunter || !target || target.IsDead() || m_humanoid.InAttack())
            {
                m_outOfReach = 0f;
                return;
            }
            float reach = Element == Element.Fire ? FireReach : StormReach;
            if (Vector3.Distance(target.transform.position, transform.position) <= reach)
            {
                m_outOfReach = 0f;
                return;
            }
            m_outOfReach += RoleInterval;
            if (m_outOfReach >= ChaseDelay && Time.time >= m_nextRush)
            {
                m_outOfReach = 0f;
                m_nextRush = Time.time + RushCooldown;
                TwinsLog.Event($"{TwinsLog.Name(this)} has been out of reach of {TwinsLog.Name(target)} for {ChaseDelay:0} s.");
                Rush(target, Element == Element.Storm ? StormChaseGap : ArriveGap);
            }
        }

        private void UpdateLevel()
        {
            int level = Mathf.Max(1, m_nview.GetZDO().GetInt(LevelKey, DefaultLevel));
            if (level == m_level)
            {
                return;
            }
            // Its items come in Humanoid.Start; until then, nothing to set
            ItemDrop.ItemData[] pieces = { m_humanoid.m_helmetItem, m_humanoid.m_chestItem, m_humanoid.m_legItem, m_humanoid.m_shoulderItem };
            if (System.Array.IndexOf(pieces, null) >= 0)
            {
                return;
            }
            foreach (ItemDrop.ItemData piece in pieces)
            {
                piece.m_quality = level;
            }
            m_level = level;
        }

        // A player drew its staff after the other (TwinsDuel.Draw): smitten at once, then the boss is on them in a flash,
        // a blink for the Stormcaller (Lightning Step's), a comet for the Firecaller (the Firecaller's Regalia's). Its
        // targeting follows from the draw (TwinsAI). On its owner's game
        private void RPC_Punish(long sender, ZDOID playerID)
        {
            if (!m_nview.IsOwner() || !m_humanoid || m_humanoid.IsDead())
            {
                return;
            }
            GameObject found = ZNetScene.instance ? ZNetScene.instance.FindInstance(playerID) : null;
            Player player = found ? found.GetComponent<Player>() : null;
            if (!player || player.IsDead())
            {
                return;
            }
            TwinsLog.Event($"{TwinsLog.Name(this)} punishes {TwinsLog.Name(player)}'s swap: a smite of {TwinsDuel.SwapDamage:0}, then a rush.");
            TwinsDuel.Smite(Element, player, m_humanoid, TwinsDuel.SwapDamage, TwinsDuel.SmiteRadius);
            Rush(player, ArriveGap);
            m_nextRush = Time.time + RushCooldown;
            m_roleTimer = 0f;
        }

        // Its own element's staff struck it at `point`, to nothing (TwinsRules.StavesOnly, on its owner's game): its ward
        // flashes there on every game (WardFlash), so whoever struck sees why no number came
        internal void Ward(Vector3 point, Character attacker)
        {
            if (Time.time - m_lastWard < WardEvery || !m_nview || !m_nview.IsValid() || !m_humanoid)
            {
                return;
            }
            m_lastWard = Time.time;
            Vector3 center = m_humanoid.GetCenterPoint();
            Vector3 flat = point - center;
            flat.y = 0f;
            if (flat.sqrMagnitude < WardNear * WardNear && attacker)
            {
                flat = attacker.GetCenterPoint() - center;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f)
                {
                    point = center + flat.normalized * WardReach;
                }
            }
            m_nview.InvokeRPC(ZNetView.Everybody, WardRpc, point);
        }

        private void RPC_Ward(long sender, Vector3 point)
        {
            WardFlash.Show(this, point);
        }

        // In a flash to `gap` short of `target`, along the ground (LightningStep.Walk), drawn as its element's
        private void Rush(Character target, float gap)
        {
            // Not while it stands for its second phase's coming or its comet's warning (TwinsPhase)
            if (m_phase && m_phase.Busy(out _))
            {
                TwinsLog.Event($"{TwinsLog.Name(this)}'s rush: busy with its second phase, none.");
                return;
            }
            Vector3 start = transform.position;
            Vector3 flat = target.transform.position - start;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance <= gap + 1f)
            {
                return;
            }
            Vector3 dir = flat / distance;
            if (!Flash(dir, distance - gap, out Vector3 from, out Vector3 to))
            {
                TwinsLog.Event($"{TwinsLog.Name(this)}'s rush at {TwinsLog.Name(target)}: nowhere to land, none.");
                return;
            }
            TwinsLog.Event($"{TwinsLog.Name(this)} rushes at {TwinsLog.Name(target)}: {Vector3.Distance(from, to):0} m.");
            Quaternion rotation = Quaternion.LookRotation(dir);
            if (Element == Element.Storm)
            {
                if (LightningStep.s_bolt)
                {
                    LightningBolt.Arc(LightningStep.s_bolt, m_humanoid, from, to, RushLevel);
                }
                StaffEffects.Create(LightningStep.s_leaveEffects, from, rotation);
                StaffEffects.Create(LightningStep.s_landEffects, to, rotation);
            }
            else
            {
                if (CometDash.s_comet)
                {
                    FireComet.Create(CometDash.s_comet, from, to, RushLevel);
                }
                StaffEffects.Create(CometDash.s_leaveEffects, from, rotation);
            }
        }

        // The twin moved `reach` along `dir` at once, facing it, and where its middle was and is (for the look): along the
        // ground, as Lightning Step goes; but where something's in the way (rocks, a tree), it doesn't stop there: it
        // flashes over it to a free spot near where it meant to land (the user, 2026-11-15: debris "prevented the bosses
        // from teleporting next to me"). False: nowhere to go. The rush's, and the Firecaller's comet in its second phase
        // (TwinsPhase)
        internal bool Flash(Vector3 dir, float reach, out Vector3 from, out Vector3 to)
        {
            Vector3 start = transform.position;
            from = to = m_humanoid.GetCenterPoint();
            Vector3 aim = start + dir * reach;
            bool walked = LightningStep.Walk(m_humanoid, start, dir, reach, out Vector3 end);
            if (!walked || Utils.DistanceXZ(end, aim) > ShortOfAim)
            {
                if (FindStand(aim, out Vector3 stand))
                {
                    end = stand;
                }
                else if (!walked)
                {
                    return false;
                }
            }
            to = end + (from - start);
            Quaternion rotation = Quaternion.LookRotation(dir);
            transform.SetPositionAndRotation(end, rotation);
            Rigidbody body = GetComponent<Rigidbody>();
            if (body)
            {
                body.position = end;
                body.rotation = rotation;
            }
            return true;
        }

        // A rush that walks this much short of where it aimed looks for a free spot there instead (meters)
        private const float ShortOfAim = 2f;
        // Rings of spots about the aim tried for one (meters apart, how many rings, how many spots on each)
        private const float StandStep = 1.5f;
        private const int StandRings = 4;
        private const int StandSpots = 8;

        // A spot near `aim` on the ground, out of deep water, with room for the twin to stand (its capsule at its size)
        private bool FindStand(Vector3 aim, out Vector3 stand)
        {
            stand = aim;
            CapsuleCollider capsule = m_humanoid.GetCollider() as CapsuleCollider;
            float scale = transform.localScale.y;
            float radius = (capsule ? capsule.radius : 0.5f) * scale * 0.9f;
            float height = (capsule ? capsule.height : 1.85f) * scale;
            float water = ZoneSystem.instance ? ZoneSystem.instance.m_waterLevel : float.MinValue;
            for (int ring = 0; ring <= StandRings; ring++)
            {
                int spots = ring == 0 ? 1 : StandSpots;
                for (int i = 0; i < spots; i++)
                {
                    Vector3 point = aim + Quaternion.AngleAxis(360f * i / spots, Vector3.up) * Vector3.forward * (StandStep * ring);
                    if (!ZoneSystem.instance || !ZoneSystem.instance.GetGroundHeight(point, out float ground) || ground < water - 0.5f)
                    {
                        continue;
                    }
                    point.y = ground;
                    Vector3 bottom = point + Vector3.up * (radius + 0.3f);
                    Vector3 top = point + Vector3.up * Mathf.Max(radius + 0.3f, height - radius);
                    if (!Physics.CheckCapsule(bottom, top, radius, StaffHits.SightMask, QueryTriggerInteraction.Ignore))
                    {
                        stand = point;
                        return true;
                    }
                }
            }
            return false;
        }

        // The movement speeds the animations are given, divided by the boss's size (its scale, which the world's
        // modifiers can change a little: Character.Awake). The owner's game sets them, and others take them from its ZDO
        [HarmonyPatch(typeof(ZSyncAnimation), nameof(ZSyncAnimation.SetFloat), typeof(int), typeof(float))]
        private static class StrideScale
        {
            private static readonly int s_forward = ZSyncAnimation.GetHash("forward_speed");
            private static readonly int s_sideway = ZSyncAnimation.GetHash("sideway_speed");

            private static void Prefix(ZSyncAnimation __instance, int hash, ref float value)
            {
                if ((hash == s_forward || hash == s_sideway) && s_animations.TryGetValue(__instance, out Transform boss) && boss)
                {
                    value /= Mathf.Max(0.01f, boss.localScale.y);
                }
            }
        }
    }
}
