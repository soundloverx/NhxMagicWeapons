using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects
{
    // The staff's model by its level (made ready by StaffModel): shows the swirls in pairs and sets how fast they turn and
    // glow, the glow of the head's flame crystal, of the butt's crystal and of the shards in the collar (and their
    // pulse), the head's light, and the embers rising from it
    internal class StaffLevelLook : LevelLook
    {
        // The body, with these materials in these slots
        public Renderer m_body;
        public int m_headSlot = 4;
        public int m_buttSlot = 0;
        public int m_shardsSlot = 5;
        // In pairs: around the head, the lower shaft, the butt
        public Renderer[] m_swirls = new Renderer[0];
        // Turns the swirls (the Incinerator's animation)
        public Animator m_spin;
        public Light m_light;
        public ParticleSystem m_embers;

        // From which level each swirl shows, in order
        public int[] m_swirlLevels = { 5, 5, 10, 10, 15, 15 };
        // The head's crystal glows m_headStartGlow at level 1, the butt's m_buttStartGlow and the shards m_shardsStartGlow,
        // all glowing fully at m_fullLevel and m_maxGlow times at m_maxLevel; the head's light follows its crystal. The
        // swirls glow m_swirlStartGlow from the first's level, fully at m_fullLevel. The shards' pulse stands still and
        // the swirls don't turn below the first swirl's level; both go m_spinStart times as fast as made then, as made at
        // m_fullLevel, and m_maxSpin times at m_maxLevel
        public float m_headStartGlow = 0.35f;
        public float m_buttStartGlow = 0.05f;
        public float m_shardsStartGlow = 0.3f;
        public float m_swirlStartGlow = 0.5f;
        public float m_maxGlow = 1.5f;
        public float m_spinStart = 0.5f;
        public float m_maxSpin = 1.15f;
        // The embers show from m_embersLevel, m_embersStart times as many as m_embersRate a second, as many at
        // m_fullLevel, and m_maxEmbers times at m_maxLevel
        public int m_embersLevel = 10;
        public float m_embersRate = 6f;
        public float m_embersStart = 0.5f;
        public float m_maxEmbers = 2f;

        // As made, so that showing it again starts from there
        private float m_lightIntensity;

        private void Awake()
        {
            if (m_light)
            {
                m_lightIntensity = m_light.intensity;
            }
        }

        protected override void Show(int level)
        {
            level = Mathf.Min(level, m_maxLevel);
            int swirlsFrom = m_swirlLevels.Length > 0 ? m_swirlLevels[0] : 1;
            bool spinning = level >= swirlsFrom;
            float spin = spinning ? Ramp(level, swirlsFrom, m_spinStart, m_maxSpin) : 0f;

            float headGlow = Ramp(level, 1, m_headStartGlow, m_maxGlow);
            if (m_body)
            {
                SetGlow(m_body, m_headSlot, headGlow, 1f);
                SetGlow(m_body, m_buttSlot, Ramp(level, 1, m_buttStartGlow, m_maxGlow), 1f);
                SetGlow(m_body, m_shardsSlot, Ramp(level, 1, m_shardsStartGlow, m_maxGlow), spin);
            }

            float swirlGlow = Ramp(level, swirlsFrom, m_swirlStartGlow, m_maxGlow);
            for (int i = 0; i < m_swirls.Length; i++)
            {
                bool shown = i < m_swirlLevels.Length && level >= m_swirlLevels[i];
                m_swirls[i].gameObject.SetActive(shown);
                if (shown)
                {
                    SetGlow(m_swirls[i], 0, swirlGlow, 1f);
                }
            }
            if (m_spin)
            {
                m_spin.speed = spin;
            }
            if (m_light)
            {
                m_light.intensity = m_lightIntensity * headGlow;
            }
            if (m_embers)
            {
                bool glowing = level >= m_embersLevel;
                m_embers.gameObject.SetActive(glowing);
                ParticleSystem.EmissionModule emission = m_embers.emission;
                emission.rateOverTime = glowing ? m_embersRate * Ramp(level, m_embersLevel, m_embersStart, m_maxEmbers) : 0f;
            }
        }
    }
}
