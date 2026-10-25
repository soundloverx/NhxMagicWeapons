using UnityEngine;

namespace NhxMagicWeapons.Staves.StaffOfStorms.Effects
{
    // The lightning of the staff's strike, falling from the sky onto its effect's spot (NhxStormStrike), on every game. The
    // caster's game stores the staff's level in the effect's ZDO as it creates it (LightningStrike); like the bolt's,
    // higher levels draw more bolts side by side
    internal class StrikeLightning : MonoBehaviour
    {
        public LightningLook m_look;
        // From how high it falls, how long it shows, and how far it jags sideways (a share of its length, at most m_maxJag)
        public float m_height = 80f;
        public float m_duration = 0.5f;
        public float m_jag = 0.08f;
        public float m_maxJag = 6f;
        // One bolt from m_firstLevel, where the strike comes, and one more every m_levelsPerStrand levels, up to
        // m_maxStrands. Their width and glow are m_scale times the look's, as a strike is seen from farther away
        public int m_firstLevel = 5;
        public int m_levelsPerStrand = 3;
        public int m_maxStrands = 5;
        public float m_scale = 3f;

        private static readonly int s_levelKey = "nhx_strike_level".GetStableHashCode();

        private ZNetView m_nview;
        private float m_age;
        // From when the ZDO has the level
        private LightningArcs m_arcs;

        // On the caster's game, right after creating the effect, so that its first send carries it
        internal static void SetLevel(GameObject strike, int level)
        {
            ZNetView nview = strike ? strike.GetComponent<ZNetView>() : null;
            if (nview && nview.IsValid())
            {
                nview.GetZDO().Set(s_levelKey, level);
            }
        }

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
        }

        private void Update()
        {
            if (!m_nview || !m_nview.IsValid())
            {
                return;
            }
            if (m_arcs == null && !Init())
            {
                return;
            }
            m_age += Time.deltaTime;
            m_arcs.Draw(m_age);
        }

        private bool Init()
        {
            int level = m_nview.GetZDO().GetInt(s_levelKey);
            if (level < 1)
            {
                return false;
            }
            LightningSize size = m_look.Size(level, m_firstLevel, m_levelsPerStrand, m_maxStrands);
            size.m_strandSize *= m_scale;
            size.m_glowSize *= m_scale;

            // The effect has lights of its own
            m_arcs = new LightningArcs(transform, m_look, m_duration) { m_jag = m_jag, m_maxJag = m_maxJag, m_lights = false };
            Vector3 ground = transform.position;
            m_arcs.Add(new LightningAnchor { m_point = ground + Vector3.up * m_height }, new LightningAnchor { m_point = ground }, 0f, size);
            return true;
        }
    }
}
