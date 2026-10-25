using UnityEngine;

namespace NhxMagicWeapons.Staves.StaffOfStorms.Effects
{
    // The staff's model by its level (its parts are made by StaffModel): shows the prongs, grows the spikes, and sets the
    // glow of the crystals, the brightness and pulse of the inlays along the shaft, the head's light and its sparks
    internal class StaffLevelLook : LevelLook
    {
        public GameObject[] m_prongs = new GameObject[0];
        public Transform[] m_spikes = new Transform[0];
        public Renderer[] m_crystals = new Renderer[0];
        // The body, with the inlays' material in this slot
        public Renderer m_body;
        public int m_inlaySlot = -1;
        public Light m_light;
        // The sparks from the head while it's held (a burst every second)
        public ParticleSystem m_sparks;

        // From which level each prong shows, in order
        public int[] m_prongLevels = { 5, 10, 20, 20 };
        // The crystals and spikes show from m_firstLevel
        public int m_firstLevel = 5;
        // At m_firstLevel the spikes are this share of their length, and the crystals glow this share of their glow, both
        // growing to their full look at m_fullLevel. The inlays glow m_inlayStartGlow at level 1, fully at m_fullLevel.
        // At m_maxLevel the crystals, inlays and light glow m_maxGlow times. The inlays' pulse stands still below
        // m_firstLevel, runs m_pulseStartSpeed times as fast as made at m_firstLevel, as made at m_fullLevel, and
        // m_maxPulseSpeed times at m_maxLevel
        public float m_spikeStartSize = 0.25f;
        public float m_crystalStartGlow = 0.05f;
        public float m_inlayStartGlow = 0.3f;
        public float m_maxGlow = 1.5f;
        public float m_pulseStartSpeed = 0.5f;
        public float m_maxPulseSpeed = 1.15f;
        // The head's sparks show from m_sparksLevel, with m_sparksStart times as many as made, as many at m_fullLevel, and
        // m_maxSparks times at m_maxLevel
        public int m_sparksLevel = 10;
        public float m_sparksStart = 0.5f;
        public float m_maxSparks = 2f;

        // As made, so that showing it again starts from there
        private float m_lightIntensity;
        private float m_sparksCount;

        private void Awake()
        {
            if (m_light)
            {
                m_lightIntensity = m_light.intensity;
            }
            if (m_sparks && m_sparks.emission.burstCount > 0)
            {
                m_sparksCount = m_sparks.emission.GetBurst(0).count.constant;
            }
        }

        protected override void Show(int level)
        {
            level = Mathf.Min(level, m_maxLevel);
            for (int i = 0; i < m_prongs.Length; i++)
            {
                m_prongs[i].SetActive(i < m_prongLevels.Length && level >= m_prongLevels[i]);
            }

            // The spikes grow in length only, from their root: grown whole, they'd stay hidden inside the gold cradle
            // around the butt crystal until nearly full size
            bool grown = level >= m_firstLevel;
            float spikeSize = Ramp(level, m_firstLevel, m_spikeStartSize, 1f);
            foreach (Transform spike in m_spikes)
            {
                spike.gameObject.SetActive(grown);
                spike.localScale = new Vector3(1f, 1f, spikeSize);
            }
            float crystalGlow = Ramp(level, m_firstLevel, m_crystalStartGlow, m_maxGlow);
            foreach (Renderer crystal in m_crystals)
            {
                crystal.gameObject.SetActive(grown);
                SetGlow(crystal, 0, crystalGlow, 1f);
            }
            if (m_body && m_inlaySlot >= 0)
            {
                float pulse = grown ? Ramp(level, m_firstLevel, m_pulseStartSpeed, m_maxPulseSpeed) : 0f;
                SetGlow(m_body, m_inlaySlot, Ramp(level, 1, m_inlayStartGlow, m_maxGlow), pulse);
            }
            if (m_light)
            {
                m_light.enabled = grown;
                m_light.intensity = m_lightIntensity * crystalGlow;
            }
            if (m_sparks)
            {
                bool sparking = level >= m_sparksLevel;
                m_sparks.gameObject.SetActive(sparking);
                ParticleSystem.EmissionModule emission = m_sparks.emission;
                if (sparking && emission.burstCount > 0)
                {
                    ParticleSystem.Burst burst = emission.GetBurst(0);
                    burst.count = Mathf.Max(1f, Mathf.Round(m_sparksCount * Ramp(level, m_sparksLevel, m_sparksStart, m_maxSparks)));
                    emission.SetBurst(0, burst);
                }
            }
        }
    }
}
