using UnityEngine;

namespace NhxMagicWeapons.Staves
{
    // A staff's model by its level, on its "attach" object. Wherever the game shows the staff (in hand, on the back,
    // dropped, on an item stand or an armor stand, in a character preview), this finds its level there and shows it
    // (Show, each staff's own). The game makes the model anew when the level changes (VisEquipment, ItemStand), so once
    // is enough, but for a preview, which follows the local player's level
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

        // In a character preview (IsPreview), the local player's level is looked at again this often: the preview doesn't
        // make the staff anew when only its level changes
        private const float FollowInterval = 0.5f;

        private int m_retries;
        private int m_shown;
        private bool m_preview;

        // The model at `level`, from 1. It may be shown again (an armor stand's data arriving late, a preview's player
        // refining the staff), so it starts from the model as made
        protected abstract void Show(int level);

        private void Start()
        {
            int level = Level();
            Apply(level > 0 ? level : m_fullLevel);
            if (level == 0 && GetComponentInParent<ArmorStand>())
            {
                InvokeRepeating(nameof(Retry), RetryInterval, RetryInterval);
            }
            if (m_preview)
            {
                InvokeRepeating(nameof(Follow), FollowInterval, FollowInterval);
            }
        }

        private void Apply(int level)
        {
            m_shown = level;
            Show(level);
        }

        private void Retry()
        {
            int level = Level();
            if (level > 0)
            {
                Apply(level);
            }
            if (level > 0 || ++m_retries >= Retries)
            {
                CancelInvoke(nameof(Retry));
            }
        }

        private void Follow()
        {
            int level = Level();
            if (level > 0 && level != m_shown)
            {
                Apply(level);
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
                int hash;
                if (gameObject == equipment.m_rightItemInstance)
                {
                    slot = VisSlot.HandRight;
                    level = equipment.m_currentRightItemQuality;
                    hash = equipment.m_currentRightItemHash;
                }
                else if (gameObject == equipment.m_rightBackItemInstance)
                {
                    slot = VisSlot.BackRight;
                    level = equipment.m_currentRightBackItemQuality;
                    hash = equipment.m_currentRightBackItemHash;
                }
                else if (gameObject == equipment.m_leftItemInstance)
                {
                    slot = VisSlot.HandLeft;
                    level = equipment.m_currentLeftItemQuality;
                    hash = equipment.m_currentLeftItemHash;
                }
                else if (gameObject == equipment.m_leftBackItemInstance)
                {
                    slot = VisSlot.BackLeft;
                    level = equipment.m_currentLeftBackItemQuality;
                    hash = equipment.m_currentLeftBackItemHash;
                }
                else
                {
                    return 0;
                }
                // An armor stand shows its items at their prefab's level (ArmorStand.SetVisualItem), always 1
                ArmorStand armorStand = GetComponentInParent<ArmorStand>();
                if (armorStand)
                {
                    return ArmorStandLevel(armorStand, slot);
                }
                if (IsPreview(equipment))
                {
                    int own = SlotLevel(Player.m_localPlayer.m_visEquipment, slot, hash);
                    if (own > 0)
                    {
                        m_preview = true;
                        return own;
                    }
                }
                return level;
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

        // A character preview: a copy of the player with no network object, wearing the local player's gear. The user
        // (2026-11-15): in AzuExtendedPlayerInventory's preview "the equipped staff [...] appears as a level 0 or 1": it
        // copies the local player's items by name at level 1 (its VECloneSync.MirrorFrom: SetRightItem(name, 1), the
        // same for the back), so the level is the local player's own item's, in the same slot. The game's main menu
        // preview equips the profile's items with their levels, and has no local player
        private static bool IsPreview(VisEquipment equipment)
        {
            Player local = Player.m_localPlayer;
            return local && equipment != local.m_visEquipment && equipment.GetComponent<Player>()
                && !(equipment.m_nview && equipment.m_nview.IsValid());
        }

        // The level of what `equipment` shows in `slot`, if it's the item `hash`; 0 if not
        private static int SlotLevel(VisEquipment equipment, VisSlot slot, int hash)
        {
            if (!equipment)
            {
                return 0;
            }
            switch (slot)
            {
                case VisSlot.HandRight:
                    return equipment.m_currentRightItemHash == hash ? equipment.m_currentRightItemQuality : 0;
                case VisSlot.BackRight:
                    return equipment.m_currentRightBackItemHash == hash ? equipment.m_currentRightBackItemQuality : 0;
                case VisSlot.HandLeft:
                    return equipment.m_currentLeftItemHash == hash ? equipment.m_currentLeftItemQuality : 0;
                case VisSlot.BackLeft:
                    return equipment.m_currentLeftBackItemHash == hash ? equipment.m_currentLeftBackItemQuality : 0;
                default:
                    return 0;
            }
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
