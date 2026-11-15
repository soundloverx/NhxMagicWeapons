using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim
{
    // What every game sees of the Firecaller's Regalia's comet (CometDash, a showpiece being tried): a streak of fire
    // from where the player was to where they are, drawn like the channelled beam's tendrils (the staff's main beam and
    // weavers at its level); the staff's blast where they reappear, at a size of its own; and flames along the ground
    // under the way, dying down over a few seconds. Only looks: the comet's hits are dealt by the player's game.
    // That game creates it where the player was and stores where they went and the staff's level in its ZDO; every game
    // (that one too) draws it from there.
    // The Firecaller boss's comet in its second phase (Bosses\TwinsPhase) leaves a way that burns longer and wider, and
    // hurts: its ZDO says so (Create's burn time, width and hurts), and each player's game asks whether its player stands
    // in it (Burning)
    internal class FireComet : MonoBehaviour
    {
        // The staff's beam (its prefab): its look and its blast
        public FireBeam m_beam;
        // How long the streak shows, the way burns, and the ZDO lasts
        public float m_streakDuration = 0.5f;
        public float m_burnTime = 3f;
        public float m_lifetime = 5f;
        // The burst where they reappear: the blast's effect this far around, shaking the camera by this share
        public float m_burstRadius = 2.5f;
        public float m_burstShake = 0.5f;
        // Flames a second for each meter of the way at first, fewer and fewer until m_burnTime; within this of its line
        public float m_flamesPerMeter = 10f;
        public float m_burnWidth = 0.3f;

        private static readonly int s_endKey = "nhx_comet_end".GetStableHashCode();
        private static readonly int s_levelKey = "nhx_comet_level".GetStableHashCode();
        private static readonly int s_burnKey = "nhx_comet_burn".GetStableHashCode();
        private static readonly int s_widthKey = "nhx_comet_width".GetStableHashCode();
        private static readonly int s_hurtsKey = "nhx_comet_hurts".GetStableHashCode();
        // Its ZDO is kept this long after the way has burned out
        private const float AfterBurn = 1f;
        // Who stands in a burning way: within its width of the line, and this far above or below the ground under it
        private const float BurnHeight = 2f;

        // On every game: the comets whose way hurts
        private static readonly List<FireComet> s_hurting = new List<FireComet>();
        // The ground under the way is looked up this often along it, from this high above the streak, this far down
        private const float GroundStep = 0.4f;
        private const float GroundAbove = 0.5f;
        private const float GroundBelow = 4f;
        // Without ground under it, the way burns this far under the streak (the player's middle)
        private const float FeetBelow = 1f;

        private ZNetView m_nview;
        private bool m_ready;
        private float m_age;
        private Vector3 m_to;
        // On every game but a dedicated server, from when the ZDO has the comet's end
        private FireBeams m_streak;
        private ParticleSystem m_flames;
        private ParticleSystem m_embers;
        private readonly List<Vector3> m_ground = new List<Vector3>();
        private float m_flameDebt;
        private bool m_hurts;
        // The flames' share for a way wider than the prefab's
        private float m_flameScale = 1f;

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
        }

        private void OnDestroy()
        {
            s_hurting.Remove(this);
        }

        // On the player's game: the comet from `from` to `to`, for a staff at `level`, shown on every game
        internal static void Create(GameObject prefab, Vector3 from, Vector3 to, int level)
        {
            FireComet comet = Instantiate(prefab, from, Quaternion.identity).GetComponent<FireComet>();
            ZDO zdo = comet.m_nview.GetZDO();
            zdo.Set(s_endKey, to);
            zdo.Set(s_levelKey, level);
        }

        // The same, its way burning `burnTime` seconds, `width` either side of its line, and hurting who stands in it
        internal static void Create(GameObject prefab, Vector3 from, Vector3 to, int level, float burnTime, float width, bool hurts)
        {
            FireComet comet = Instantiate(prefab, from, Quaternion.identity).GetComponent<FireComet>();
            ZDO zdo = comet.m_nview.GetZDO();
            zdo.Set(s_burnKey, burnTime);
            zdo.Set(s_widthKey, width);
            zdo.Set(s_hurtsKey, hurts);
            zdo.Set(s_levelKey, level);
            zdo.Set(s_endKey, to);
        }

        // Whether `point` (a player's feet) is in a way that's burning and hurts, on this game
        internal static bool Burning(Vector3 point)
        {
            foreach (FireComet comet in s_hurting)
            {
                if (comet && comet.m_ready && comet.m_age < comet.m_burnTime && comet.InWay(point))
                {
                    return true;
                }
            }
            return false;
        }

        private bool InWay(Vector3 point)
        {
            Vector3 from = transform.position;
            Vector3 flat = m_to - from;
            flat.y = 0f;
            float length = flat.magnitude;
            Vector3 offset = point - from;
            offset.y = 0f;
            Vector3 dir = length > 0.01f ? flat / length : Vector3.zero;
            float along = Mathf.Clamp(Vector3.Dot(offset, dir), 0f, length);
            Vector3 nearest = from + dir * along;
            if (Utils.DistanceXZ(nearest, point) > m_burnWidth)
            {
                return false;
            }
            // The ground there, as the flames have it
            float ground = nearest.y;
            if (m_ground.Count > 0)
            {
                int i = length > 0.01f ? Mathf.Clamp(Mathf.RoundToInt(along / length * (m_ground.Count - 1)), 0, m_ground.Count - 1) : 0;
                ground = m_ground[i].y;
            }
            return Mathf.Abs(point.y - ground) <= BurnHeight;
        }

        private void Update()
        {
            if (!m_nview.IsValid())
            {
                return;
            }
            if (!m_ready && !Init())
            {
                return;
            }
            float dt = Time.deltaTime;
            m_age += dt;
            if (m_nview.IsOwner() && m_age >= m_lifetime)
            {
                ZNetScene.instance.Destroy(gameObject);
                return;
            }
            if (m_streak == null)
            {
                return;
            }
            m_streak.DrawTendril(m_age, transform.position, m_to);
            if (m_age < m_burnTime && m_ground.Count > 0)
            {
                // The way burns: flames from the ground, fewer as it dies down; one that hurts burns fully until its last
                // second, so that it shows where it still hurts
                float left = m_hurts ? Mathf.Clamp01(m_burnTime - m_age) : 1f - m_age / m_burnTime;
                m_flameDebt += m_flamesPerMeter * m_flameScale * (m_ground.Count - 1) * GroundStep * left * dt;
                while (m_flameDebt >= 1f)
                {
                    m_flameDebt -= 1f;
                    Vector3 point = m_ground[Random.Range(0, m_ground.Count)] + Vector3.Scale(Random.insideUnitSphere, new Vector3(m_burnWidth, 0.3f, m_burnWidth));
                    FireParticles.Emit(m_flames, point, Vector3.up * Random.Range(0.2f, 0.5f));
                    if (Random.value < 0.2f)
                    {
                        FireParticles.Emit(m_embers, point, Vector3.up * Random.Range(0.3f, 0.8f) + Random.insideUnitSphere * 0.2f);
                    }
                }
            }
        }

        private bool Init()
        {
            ZDO zdo = m_nview.GetZDO();
            if (!zdo.GetVec3(s_endKey, out m_to))
            {
                return false;
            }
            m_ready = true;
            float baseWidth = m_burnWidth;
            m_burnTime = zdo.GetFloat(s_burnKey, m_burnTime);
            m_burnWidth = zdo.GetFloat(s_widthKey, m_burnWidth);
            m_lifetime = Mathf.Max(m_lifetime, m_burnTime + AfterBurn);
            // More flames for a wider way, though not as many more (5 times as wide: 2.2 times the flames)
            m_flameScale = Mathf.Sqrt(Mathf.Max(1f, m_burnWidth / Mathf.Max(0.01f, baseWidth)));
            m_hurts = zdo.GetBool(s_hurtsKey);
            if (m_hurts)
            {
                s_hurting.Add(this);
                FindGround();
            }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !m_beam || !m_beam.m_look)
            {
                return true;
            }
            int level = zdo.GetInt(s_levelKey, 1);
            Vector3 from = transform.position;
            BeamSize size = m_beam.m_look.Size(level, m_beam.m_levelsPerStrand, m_beam.m_maxStrands);
            m_streak = new FireBeams(transform, m_beam.m_look, m_streakDuration, null, from, m_to, size);
            m_beam.ShowBlastOfRadius(m_to, m_burstRadius, m_burstShake);

            m_flames = FireParticles.Flames(transform, 0.3f, 0.55f, 0.5f, 0.9f, Mathf.RoundToInt(400 * m_flameScale));
            m_embers = FireParticles.Embers(transform, 1f, 2f, 150);
            FindGround();
            int steps = m_ground.Count - 1;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = Vector3.Lerp(from, m_to, (float)i / Mathf.Max(1, steps));
                // Embers thrown up behind the comet as it goes
                FireParticles.Emit(m_embers, point + Random.insideUnitSphere * 0.3f, Vector3.up * Random.Range(0.3f, 1.2f) + Random.insideUnitSphere * 0.5f);
            }
            return true;
        }

        // The ground under the way, once (also where nothing is drawn: a way that hurts needs it)
        private void FindGround()
        {
            if (m_ground.Count > 0)
            {
                return;
            }
            Vector3 from = transform.position;
            float length = Vector3.Distance(from, m_to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / GroundStep));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = Vector3.Lerp(from, m_to, (float)i / steps);
                m_ground.Add(Physics.Raycast(point + Vector3.up * GroundAbove, Vector3.down, out RaycastHit hit, GroundAbove + GroundBelow,
                    StaffHits.GroundMask, QueryTriggerInteraction.Ignore) ? hit.point + Vector3.up * 0.1f : point + Vector3.down * FeetBelow);
            }
        }
    }
}
