using HarmonyLib;
using NhxMagicWeapons.Staves.StaffOfStorms.Effects;
using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Staves.StaffOfStorms
{
    // Eye of the Storm: the Staff of Storms' secondary attack with the whole Stormcaller's Raiment on (its set bonus; the
    // user's pick, 2026-11-10). Held (the Incinerator's looping attack, CallTheStorm below), it gathers a storm cloud over
    // the spot aimed at, which drifts after the crosshair; every second (the attack's shots, each paying its eitr: the game
    // stops eitr regen during attacks) it rains bolts of lightning, spread over the second and across the cloud (the user,
    // 2026-11-11: "Can it rain lightning across a larger area under the cloud instead of a bunch of bolts gathered together
    // in the center?"; until then one strike a second, on the enemy nearest its middle or on its middle, drawn as several
    // bolts side by side by level). Each bolt falls on an enemy under the cloud not struck yet that second, nearest its
    // middle first, or else on a spot under the cloud away from the second's other bolts, and strikes everything around
    // as the staff's strike does; each thing is struck once a second at most, whatever the bolts (so an enemy takes what
    // it took before; the rain reaches more of them). The first enemy struck in a second chains by the staff's rules
    // (LightningBolt.ChainFrom). The second's first bolt is the full strike (ground wave, sparks, lights, thunder, camera
    // shake), the rest lighter (a crackle), and the cloud flashes for each.
    // The caster's game creates it at the first shot (Tick, from StormEyeTick), moves it every frame and deals the hits;
    // its ZDO tells every game where it is and when a bolt falls (the bolts' effects are networked themselves); every game
    // but a dedicated server draws its cloud (StormCloud). The caster's game destroys it when the attack ends
    internal class StormEye : MonoBehaviour
    {
        // From the caster, and under the cloud
        public float m_range = 60f;
        public float m_radius = 7f;
        // Each second's first bolt and the rest (networked effects, falling from the cloud), and the area each hits
        public GameObject m_strike;
        public GameObject m_rainBolt;
        public float m_strikeRadius = 3.5f;
        // Bolts a second: m_firstBolts from m_firstLevel (where the storm comes), one more every m_levelsPerBolt levels,
        // up to m_maxBolts; spread over the attack's m_interval
        public int m_firstLevel = 5;
        public int m_firstBolts = 3;
        public int m_levelsPerBolt = 3;
        public int m_maxBolts = 7;
        public float m_interval = 1f;
        // The staff's bolt, for the chains
        public GameObject m_bolt;
        // How fast it drifts after the crosshair, and how high its cloud hangs over the ground
        public float m_speed = 12f;
        public float m_height = 16f;
        public Material m_cloudMaterial;
        // The attack shoots every second: without a shot for this long, it's over
        public float m_tickTimeout = 1.6f;

        // The staff (its name), the set's status effect (its name's hash), and the storm's attack: CallTheStorm
        private static string s_staff;
        private static int s_status;
        private static Attack s_attack;

        private static readonly KeyValuePair<int, int> s_casterKey = ZDO.GetHashZDOID("nhx_storm_caster");
        private static readonly int s_levelKey = "nhx_storm_level".GetStableHashCode();
        private static readonly int s_centerKey = "nhx_storm_center".GetStableHashCode();
        // A count of its bolts, which flash the cloud
        private static readonly int s_tickKey = "nhx_storm_tick".GetStableHashCode();
        private const float CenterMoved = 0.1f;
        // Random spots tried for a bolt, keeping the one farthest from the second's others (enough when a strike's
        // radius away); and how far below the cloud to look for the ground
        private const int SpotTries = 6;
        private const float GroundSearch = 60f;
        // How fast the cloud catches up with where the storm is, on every game
        private const float Smoothing = 6f;

        // On the caster's game: each caster's storm
        private static readonly Dictionary<Character, StormEye> s_eyes = new Dictionary<Character, StormEye>();
        // On every game: the casters with a storm, for their armor's crackle (ArmorCrackle)
        private static readonly HashSet<Character> s_casters = new HashSet<Character>();
        private static readonly List<Character> s_nearby = new List<Character>();

        private ZNetView m_nview;

        // On every game
        private bool m_shown;
        private Character m_caster;
        private Vector3 m_shownCenter;
        private int m_seenTick;
        private StormCloud m_cloud;

        // On the caster's game
        private bool m_isCaster;
        private Character m_owner;
        private Humanoid m_humanoid;
        private Attack m_attack;
        private Vector3 m_center;
        private Vector3 m_writtenCenter = new Vector3(float.MaxValue, 0f, 0f);
        private float m_lastTick;
        private int m_tick;
        // This second's: the shot's hit, the staff's level and noise, its bolts, those fallen, what they struck, where
        private HitData m_hitData;
        private int m_level;
        private float m_hitNoise;
        private int m_bolts;
        private int m_boltsFallen;
        private bool m_hitCharacter;
        private bool m_chained;
        private readonly HashSet<GameObject> m_struck = new HashSet<GameObject>();
        private readonly List<Vector3> m_spots = new List<Vector3>();

        internal static void SetUp(ItemDrop.ItemData.SharedData staff, string status, Attack attack)
        {
            s_staff = staff.m_name;
            s_status = status.GetStableHashCode();
            s_attack = attack;
        }

        // Whether `caster` has a storm out, on this game
        internal static bool IsActive(Character caster)
        {
            return caster && s_casters.Contains(caster);
        }

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
        }

        // One of the attack's shots, on the caster's game: the caster's storm, gathered at the first
        internal static void Tick(GameObject prefab, Character owner, HitData hitData, int level, float hitNoise)
        {
            s_eyes.TryGetValue(owner, out StormEye eye);
            // A new attack gathers a new storm, even when the last one hasn't seen its attack end yet
            Humanoid humanoid = owner as Humanoid;
            if (eye && humanoid && eye.m_attack != humanoid.m_currentAttack)
            {
                eye.End();
                eye = null;
            }
            if (!eye)
            {
                StormEye rules = prefab.GetComponent<StormEye>();
                Vector3 aimed = rules.Aimed(owner);
                eye = Instantiate(prefab, aimed, Quaternion.identity).GetComponent<StormEye>();
                eye.Begin(owner, level, aimed);
                s_eyes[owner] = eye;
            }
            eye.Strike(hitData, level, hitNoise);
        }

        private void Begin(Character owner, int level, Vector3 center)
        {
            m_isCaster = true;
            m_owner = owner;
            m_humanoid = owner as Humanoid;
            m_attack = m_humanoid ? m_humanoid.m_currentAttack : null;
            m_center = center;
            ZDO zdo = m_nview.GetZDO();
            zdo.Set(s_casterKey, owner.GetZDOID());
            zdo.Set(s_levelKey, level);
            WriteCenter(zdo);
        }

        // Where the crosshair points, on the ground, as for the staff's strike
        private Vector3 Aimed(Character owner)
        {
            Transform tip = StaffHits.FindTip(owner);
            Vector3 start = tip ? tip.position : owner.GetCenterPoint();
            Vector3 dir = StaffHits.AimAtCrosshair(owner, start, owner.GetLookDir(), m_range);
            return LightningStrike.Aimed(owner, start, dir, m_range);
        }

        // The tick: this second's rain, its first bolt now
        private void Strike(HitData hitData, int level, float hitNoise)
        {
            m_lastTick = Time.time;
            m_hitData = hitData;
            m_level = level;
            m_hitNoise = hitNoise;
            m_bolts = Mathf.Clamp(m_firstBolts + (level - m_firstLevel) / m_levelsPerBolt, 1, m_maxBolts);
            m_boltsFallen = 0;
            m_hitCharacter = false;
            m_chained = false;
            m_struck.Clear();
            m_spots.Clear();
            Rain();
        }

        // The second's bolts due by now
        private void Rain()
        {
            while (m_hitData != null && m_boltsFallen < m_bolts && Time.time >= m_lastTick + m_boltsFallen * m_interval / m_bolts)
            {
                Bolt(m_boltsFallen == 0);
                m_boltsFallen++;
            }
        }

        private void Bolt(bool first)
        {
            Character target = Target();
            Vector3 point = target ? LightningStrike.Ground(target.transform.position) : Spot();
            m_spots.Add(point);
            GameObject effect = first ? m_strike : m_rainBolt;
            if (effect)
            {
                StrikeLightning.SetLevel(Instantiate(effect, point, Quaternion.identity), m_level);
            }
            Vector3 dir = point - m_owner.transform.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : m_owner.transform.forward;
            // Skill and adrenaline once a second, as for one strike
            if (LightningStrike.HitArea(m_owner, m_hitData, point, m_strikeRadius, dir, m_struck) && !m_hitCharacter)
            {
                m_hitCharacter = true;
                StaffHits.OnCharacterHit(m_owner, m_hitData);
            }
            BaseAI.DoProjectileHitNoise(point, m_hitNoise, m_owner);
            if (target && !m_chained && !target.IsDead())
            {
                m_chained = true;
                LightningBolt.ChainFrom(m_bolt, m_owner, target, m_hitData, m_level);
            }
            m_nview.GetZDO().Set(s_tickKey, ++m_tick);
        }

        // A spot under the cloud, as far from the second's other bolts as a few tries find, on the ground under it
        private Vector3 Spot()
        {
            Vector3 best = m_center;
            float bestGap = -1f;
            for (int i = 0; i < SpotTries; i++)
            {
                Vector2 offset = Random.insideUnitCircle * m_radius;
                Vector3 spot = m_center + new Vector3(offset.x, 0f, offset.y);
                float gap = float.MaxValue;
                foreach (Vector3 other in m_spots)
                {
                    Vector3 apart = spot - other;
                    apart.y = 0f;
                    gap = Mathf.Min(gap, apart.magnitude);
                }
                if (gap > bestGap)
                {
                    best = spot;
                    bestGap = gap;
                }
                if (gap >= m_strikeRadius)
                {
                    break;
                }
            }
            // From the cloud down: on whatever's under it (ground, rock, roof)
            Vector3 from = best + Vector3.up * m_height;
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, m_height + GroundSearch, StaffHits.GroundMask))
            {
                return hit.point;
            }
            return best;
        }

        // The enemy nearest the storm's middle (along the ground), within m_radius of it, not struck yet this second
        private Character Target()
        {
            Character best = null;
            float bestDistance = m_radius * m_radius;
            s_nearby.Clear();
            Character.GetCharactersInRange(m_center, m_radius + 2f, s_nearby);
            foreach (Character character in s_nearby)
            {
                if (character == m_owner || character.IsDead() || m_struck.Contains(character.gameObject))
                {
                    continue;
                }
                Vector3 offset = character.transform.position - m_center;
                offset.y = 0f;
                float distance = offset.sqrMagnitude;
                if (distance <= bestDistance && StaffHits.IsValidTarget(m_owner, character))
                {
                    best = character;
                    bestDistance = distance;
                }
            }
            s_nearby.Clear();
            return best;
        }

        private void WriteCenter(ZDO zdo)
        {
            // Only when it moved, as it's sent each time
            if ((m_center - m_writtenCenter).sqrMagnitude > CenterMoved * CenterMoved)
            {
                zdo.Set(s_centerKey, m_center);
                m_writtenCenter = m_center;
            }
        }

        private bool Attacking()
        {
            return m_humanoid && m_attack != null && m_humanoid.m_currentAttack == m_attack && !m_attack.IsDone();
        }

        private void Update()
        {
            if (!m_nview.IsValid())
            {
                return;
            }
            if (m_isCaster)
            {
                if (!m_owner || m_owner.IsDead() || !Attacking() || Time.time - m_lastTick > m_tickTimeout)
                {
                    End();
                    return;
                }
                // Drifting after the crosshair
                m_center = Vector3.MoveTowards(m_center, Aimed(m_owner), m_speed * Time.deltaTime);
                WriteCenter(m_nview.GetZDO());
                Rain();
            }
            else if (m_nview.IsOwner())
            {
                // Its caster left the game, and this one took it over
                ZNetScene.instance.Destroy(gameObject);
                return;
            }
            Show(Time.deltaTime);
        }

        private void End()
        {
            if (m_owner && s_eyes.TryGetValue(m_owner, out StormEye eye) && eye == this)
            {
                s_eyes.Remove(m_owner);
            }
            m_isCaster = false;
            ZNetScene.instance.Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (m_isCaster && m_owner && s_eyes.TryGetValue(m_owner, out StormEye eye) && eye == this)
            {
                s_eyes.Remove(m_owner);
            }
            if (m_caster)
            {
                s_casters.Remove(m_caster);
            }
            if (m_cloud)
            {
                m_cloud.End();
            }
        }

        // On every game: the cloud over the storm's middle, flashing as it strikes
        private void Show(float dt)
        {
            ZDO zdo = m_nview.GetZDO();
            if (!zdo.GetVec3(s_centerKey, out Vector3 center))
            {
                return;
            }
            if (!m_shown)
            {
                m_shown = true;
                m_shownCenter = center;
                ZDOID casterId = zdo.GetZDOID(s_casterKey);
                GameObject caster = casterId.IsNone() ? null : ZNetScene.instance.FindInstance(casterId);
                m_caster = caster ? caster.GetComponent<Character>() : null;
                if (m_caster)
                {
                    s_casters.Add(m_caster);
                }
                if (m_cloudMaterial && ZNet.instance && !ZNet.instance.IsDedicated())
                {
                    m_cloud = StormCloud.Create(m_cloudMaterial, center + Vector3.up * m_height, m_radius);
                }
            }
            m_shownCenter = Vector3.Lerp(m_shownCenter, center, 1f - Mathf.Exp(-Smoothing * dt));
            int tick = zdo.GetInt(s_tickKey);
            if (m_cloud)
            {
                m_cloud.Follow(m_shownCenter + Vector3.up * m_height);
                if (tick != m_seenTick)
                {
                    m_cloud.Flash();
                }
            }
            m_seenTick = tick;
        }

        // With the set on, the staff's secondary attack is the storm's (held: the Incinerator's looping attack), for this
        // one call: the attack is copied from the item's as it starts (Humanoid.StartAttack), and the item's is put back
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class CallTheStorm
        {
            private static ItemDrop.ItemData.SharedData s_swapped;
            private static Attack s_original;

            private static void Prefix(Humanoid __instance, bool secondaryAttack)
            {
                if (!secondaryAttack || s_attack == null || s_swapped != null)
                {
                    return;
                }
                ItemDrop.ItemData weapon = __instance.GetCurrentWeapon();
                if (weapon == null || weapon.m_shared.m_name != s_staff || !__instance.GetSEMan().HaveStatusEffect(s_status))
                {
                    return;
                }
                s_swapped = weapon.m_shared;
                s_original = s_swapped.m_secondaryAttack;
                s_swapped.m_secondaryAttack = s_attack;
            }

            private static void Finalizer()
            {
                if (s_swapped != null)
                {
                    s_swapped.m_secondaryAttack = s_original;
                    s_swapped = null;
                    s_original = null;
                }
            }
        }
    }
}
