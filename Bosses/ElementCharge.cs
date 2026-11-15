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
    // resistance to lightning in the vanilla game anyway"; they took Claude's proposal: "go with your proposal"). From
    // m_maxStacks on, every stack brings a smite from the element's twin (TwinsDuel.Stack) and a step of SmiteWeakness;
    // it no longer starts again after a smite (the user, 2026-11-15: "the 5 stacks shouldn't disappear when the player
    // it's hit, they should continue to build while they still hold the same staff and then they will get a new smite
    // for every additional stack (6,7 etc). This will punish players for not swapping staves"). Past the last, regen
    // stays at none. Drawing the other staff holds it at TwinsDuel.HeldStacks, slowing nothing, until the other staff's
    // charge reaches that, when it goes; taking its staff up again first carries on from there (Hold, TakeUp).
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
        // Its staff put down: building nothing (TwinsDuel stacks only the drawn staff's) and slowing nothing
        private bool m_held;

        internal int Stacks => m_stacks;
        internal bool Held => m_held;
        // What's left of the regen it slows (TwinsLog)
        internal float RegenLeft => Left;

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
            m_held = false;
        }

        // Its staff put down, at `stacks` whatever it was
        internal void Hold(int stacks)
        {
            m_stacks = stacks;
            m_held = true;
        }

        // Its staff drawn again: on from where it was held
        internal void TakeUp()
        {
            m_held = false;
        }

        // One more, past m_maxStacks too; whether it's full (from then on, every one)
        internal bool AddStack()
        {
            m_stacks++;
            return m_stacks >= m_maxStacks;
        }

        private float Left => m_held ? 1f : Mathf.Max(0f, 1f - m_regenCut * m_stacks);

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
            string text = $"{m_tooltip} <color=orange>{m_stacks}/{m_maxStacks}</color>";
            return m_held ? $"{text}\n$se_nhx_charge_held <color=orange>{TwinsDuel.HeldStacks}</color>." : text;
        }
    }
}
