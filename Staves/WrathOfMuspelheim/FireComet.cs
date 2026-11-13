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
    // (that one too) draws it from there
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

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
        }

        // On the player's game: the comet from `from` to `to`, for a staff at `level`, shown on every game
        internal static void Create(GameObject prefab, Vector3 from, Vector3 to, int level)
        {
            FireComet comet = Instantiate(prefab, from, Quaternion.identity).GetComponent<FireComet>();
            ZDO zdo = comet.m_nview.GetZDO();
            zdo.Set(s_endKey, to);
            zdo.Set(s_levelKey, level);
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
                // The way burns: flames from the ground, fewer as it dies down
                float left = 1f - m_age / m_burnTime;
                m_flameDebt += m_flamesPerMeter * (m_ground.Count - 1) * GroundStep * left * dt;
                while (m_flameDebt >= 1f)
                {
                    m_flameDebt -= 1f;
                    Vector3 point = m_ground[Random.Range(0, m_ground.Count)] + Random.insideUnitSphere * m_burnWidth;
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
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !m_beam || !m_beam.m_look)
            {
                return true;
            }
            int level = zdo.GetInt(s_levelKey, 1);
            Vector3 from = transform.position;
            BeamSize size = m_beam.m_look.Size(level, m_beam.m_levelsPerStrand, m_beam.m_maxStrands);
            m_streak = new FireBeams(transform, m_beam.m_look, m_streakDuration, null, from, m_to, size);
            m_beam.ShowBlastOfRadius(m_to, m_burstRadius, m_burstShake);

            m_flames = FireParticles.Flames(transform, 0.3f, 0.55f, 0.5f, 0.9f, 400);
            m_embers = FireParticles.Embers(transform, 1f, 2f, 150);
            float length = Vector3.Distance(from, m_to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / GroundStep));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = Vector3.Lerp(from, m_to, (float)i / steps);
                m_ground.Add(Physics.Raycast(point + Vector3.up * GroundAbove, Vector3.down, out RaycastHit hit, GroundAbove + GroundBelow,
                    StaffHits.GroundMask, QueryTriggerInteraction.Ignore) ? hit.point + Vector3.up * 0.1f : point + Vector3.down * FeetBelow);
                // Embers thrown up behind the comet as it goes
                FireParticles.Emit(m_embers, point + Random.insideUnitSphere * 0.3f, Vector3.up * Random.Range(0.3f, 1.2f) + Random.insideUnitSphere * 0.5f);
            }
            return true;
        }
    }
}
