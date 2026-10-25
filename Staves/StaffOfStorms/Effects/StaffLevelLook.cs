using UnityEngine;

namespace NhxMagicWeapons.Staves.StaffOfStorms.Effects
{
    // The staff's model by its level (its parts are made by StaffModel), on its "attach" object. Wherever the game shows the
    // staff (in hand, on the back, dropped, on an item stand or an armor stand), it finds its level there, then shows the
    // prongs, grows the spikes, and sets the glow of the crystals, the brightness and pulse of the inlays along the shaft,
    // the head's light and its sparks. The game makes the model anew when the level changes (VisEquipment, ItemStand), so
    // once is enough
    internal class StaffLevelLook : MonoBehaviour
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
        // The crystals and spikes show from m_firstLevel. The model looks as it was made at m_fullLevel, and glows the most
        // at m_maxLevel (higher levels look the same)
        public int m_firstLevel = 5;
        public int m_fullLevel = 15;
        public int m_maxLevel = 20;
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

        private static readonly int s_emissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int s_scrollSpeed = Shader.PropertyToID("_ScrollSpeed");

        // On an armor stand, another game can show the staff before the stand's data arrive: looked for again for a while
        private const float RetryInterval = 0.25f;
        private const int Retries = 20;

        // As made, so that showing it again starts from there
        private float m_lightIntensity;
        private float m_sparksCount;
        private int m_retries;

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

        private void Start()
        {
            int level = Level();
            Show(level > 0 ? level : m_fullLevel);
            if (level == 0 && GetComponentInParent<ArmorStand>())
            {
                InvokeRepeating(nameof(Retry), RetryInterval, RetryInterval);
            }
        }

        private void Retry()
        {
            int level = Level();
            if (level > 0)
            {
                Show(level);
            }
            if (level > 0 || ++m_retries >= Retries)
            {
                CancelInvoke(nameof(Retry));
            }
        }

        // Where the game shows the staff, and at what level; 0 if unknown (the model then has its own look)
        private int Level()
        {
            VisEquipment equipment = GetComponentInParent<VisEquipment>();
            if (equipment)
            {
                VisSlot slot;
                int level;
                if (gameObject == equipment.m_rightItemInstance)
                {
                    slot = VisSlot.HandRight;
                    level = equipment.m_currentRightItemQuality;
                }
                else if (gameObject == equipment.m_rightBackItemInstance)
                {
                    slot = VisSlot.BackRight;
                    level = equipment.m_currentRightBackItemQuality;
                }
                else if (gameObject == equipment.m_leftItemInstance)
                {
                    slot = VisSlot.HandLeft;
                    level = equipment.m_currentLeftItemQuality;
                }
                else if (gameObject == equipment.m_leftBackItemInstance)
                {
                    slot = VisSlot.BackLeft;
                    level = equipment.m_currentLeftBackItemQuality;
                }
                else
                {
                    return 0;
                }
                // An armor stand shows its items at their prefab's level (ArmorStand.SetVisualItem), always 1
                ArmorStand armorStand = GetComponentInParent<ArmorStand>();
                return armorStand ? ArmorStandLevel(armorStand, slot) : level;
            }
            ItemDrop drop = GetComponentInParent<ItemDrop>();
            if (drop)
            {
                return drop.m_itemData.m_quality;
            }
            ItemStand stand = GetComponentInParent<ItemStand>();
            ZNetView nview = stand ? stand.GetComponent<ZNetView>() : null;
            return nview && nview.IsValid() ? nview.GetZDO().GetInt(ZDOVars.s_quality, 1) : 0;
        }

        // The level of the item an armor stand holds in this slot: the stand keeps the whole item in its ZDO, under
        // "<slot index>_itemData" (ItemDrop.SaveToZDO)
        private static int ArmorStandLevel(ArmorStand stand, VisSlot slot)
        {
            int index = stand.m_slots.FindIndex(s => s.m_slot == slot);
            ZNetView nview = stand.GetComponent<ZNetView>();
            if (index < 0 || !nview || !nview.IsValid() || nview.GetZDO().GetByteArray((index + "_itemData").GetStableHashCode()) == null)
            {
                return 0;
            }
            ItemDrop.ItemData item = new ItemDrop.ItemData();
            ItemDrop.LoadFromZDO(item, nview.GetZDO(), index);
            return item.m_quality;
        }

        private void Show(int level)
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

        // `start` at `fromLevel`, 1 at m_fullLevel, `max` at m_maxLevel, in a straight line from one to the next
        private float Ramp(int level, int fromLevel, float start, float max)
        {
            return level <= m_fullLevel
                ? Mathf.Lerp(start, 1f, Mathf.InverseLerp(fromLevel, m_fullLevel, level))
                : Mathf.Lerp(1f, max, Mathf.InverseLerp(m_fullLevel, m_maxLevel, level));
        }

        // The material's glow times `glow` (and its scrolling times `speed`), on this renderer only
        private static void SetGlow(Renderer renderer, int slot, float glow, float speed)
        {
            Material material = renderer.sharedMaterials[slot];
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, slot);
            block.SetColor(s_emissionColor, material.GetColor(s_emissionColor) * glow);
            if (material.HasProperty(s_scrollSpeed))
            {
                block.SetFloat(s_scrollSpeed, material.GetFloat(s_scrollSpeed) * speed);
            }
            renderer.SetPropertyBlock(block, slot);
        }
    }
}
