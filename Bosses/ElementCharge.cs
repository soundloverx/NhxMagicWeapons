using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The element building up on a player who fights the twins with one of the staves (TwinsDuel): Static for the Staff of
    // Storms, Scorch for the Wrath of Muspelheim. The user (2026-11-14): "while the player holds a staff, the stacking
    // debuff for that element accumulates on a timer", "the debuff keeps stacking from the beginning of the fight until
    // the end and the player MUST swap staves to survive".
    // A stack each StackInterval while the fight lasts; each squeezes how the player fights rather than how much they can
    // take: Static slows their eitr's return, Scorch their stamina's, by m_regenCut a stack (at first each made them a
    // step weaker to the element, until the user: "becoming very weak to an element is certain death [...] there is no
    // resistance to lightning in the vanilla game anyway"; they took Claude's proposal: "go with your proposal"). At the
    // last stack the element's twin smites them (TwinsDuel.Overload) and it starts again. Drawing the other staff clears it.
    // On the player's own game (its SEMan); the count is on its icon, and every game sees it on them (ChargeLook, from
    // their ZDO). No time limit: it goes when the fight does
    internal class ElementCharge : StatusEffect
    {
        internal const string StaticName = "SE_NhxStatic";
        internal const string ScorchName = "SE_NhxScorch";

        public HitData.DamageType m_element = HitData.DamageType.Lightning;
        public int m_maxStacks = 5;
        public float m_regenCut = 0.2f;

        private int m_stacks = 1;

        internal int Stacks => m_stacks;

        // Registered with Jotunn, so its hash finds it (SEMan.AddStatusEffect looks it up in ObjectDB)
        internal static StatusEffect Add(string name, string key, string tooltip, HitData.DamageType element, Sprite icon, int maxStacks, float regenCut)
        {
            ElementCharge charge = ScriptableObject.CreateInstance<ElementCharge>();
            charge.name = name;
            charge.m_name = key;
            charge.m_tooltip = tooltip;
            charge.m_icon = icon;
            charge.m_ttl = 0f;
            charge.m_element = element;
            charge.m_maxStacks = maxStacks;
            charge.m_regenCut = regenCut;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(charge, false));
            return charge;
        }

        public override void Setup(Character character)
        {
            base.Setup(character);
            m_stacks = 1;
        }

        // One more; whether it's full
        internal bool AddStack()
        {
            m_stacks = Mathf.Min(m_stacks + 1, m_maxStacks);
            return m_stacks >= m_maxStacks;
        }

        private float Left => Mathf.Max(0f, 1f - m_regenCut * m_stacks);

        public override void ModifyEitrRegen(ref float eitrRegen)
        {
            if (m_element == HitData.DamageType.Lightning)
            {
                eitrRegen *= Left;
            }
        }

        public override void ModifyStaminaRegen(ref float staminaRegen)
        {
            if (m_element == HitData.DamageType.Fire)
            {
                staminaRegen *= Left;
            }
        }

        public override string GetIconText()
        {
            return m_stacks.ToString();
        }

        public override string GetTooltipString()
        {
            return $"{m_tooltip} <color=orange>{m_stacks}/{m_maxStacks}</color>";
        }
    }
}
