using UnityEngine;

namespace NhxMagicWeapons.Staves
{
    // A staff's model by its level, on its "attach" object. Wherever the game shows the staff (in hand, on the back,
    // dropped, on an item stand or an armor stand), this finds its level there and shows it (Show, each staff's own). The
    // game makes the model anew when the level changes (VisEquipment, ItemStand), so once is enough
    internal abstract class LevelLook : MonoBehaviour
    {
        // The model looks as it was made at m_fullLevel, and glows the most at m_maxLevel (higher levels look the same)
        public int m_fullLevel = 15;
        public int m_maxLevel = 20;

        private static readonly int s_emissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int s_fresnelColor = Shader.PropertyToID("_FresnelColor");
        private static readonly int s_scrollSpeed = Shader.PropertyToID("_ScrollSpeed");

        // On an armor stand, another game can show the staff before the stand's data arrive: looked for again for a while
        private const float RetryInterval = 0.25f;
        private const int Retries = 20;

        private int m_retries;

        // The model at `level`, from 1. It may be shown again (an armor stand's data arriving late), so it starts from
        // the model as made
        protected abstract void Show(int level);

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

        // `start` at `fromLevel`, 1 at m_fullLevel, `max` at m_maxLevel, in a straight line from one to the next
        protected float Ramp(int level, int fromLevel, float start, float max)
        {
            return level <= m_fullLevel
                ? Mathf.Lerp(start, 1f, Mathf.InverseLerp(fromLevel, m_fullLevel, level))
                : Mathf.Lerp(1f, max, Mathf.InverseLerp(m_fullLevel, m_maxLevel, level));
        }

        // The material's glow (emission, and the rim of fresnel shaders) times `glow`, and its scrolling times `speed`,
        // on this renderer only. Each only if its shader has it: Magic Supremacy's fresnel shader has no emission (the
        // game logs an error for each color asked of a shader without it)
        protected static void SetGlow(Renderer renderer, int slot, float glow, float speed)
        {
            Material material = renderer.sharedMaterials[slot];
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, slot);
            if (material.HasProperty(s_emissionColor))
            {
                block.SetColor(s_emissionColor, material.GetColor(s_emissionColor) * glow);
            }
            if (material.HasProperty(s_fresnelColor))
            {
                block.SetColor(s_fresnelColor, material.GetColor(s_fresnelColor) * glow);
            }
            if (material.HasProperty(s_scrollSpeed))
            {
                block.SetFloat(s_scrollSpeed, material.GetFloat(s_scrollSpeed) * speed);
            }
            renderer.SetPropertyBlock(block, slot);
        }
    }
}
