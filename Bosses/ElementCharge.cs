using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The element building up on a player who fights the twins with one of the staves (TwinsDuel): Static for the Staff of
    // Storms, Scorch for the Wrath of Muspelheim. The user (2026-11-14): "while the player holds a staff, the stacking
    // debuff for that element accumulates on a timer", "the debuff keeps stacking from the beginning of the fight until
    // the end and the player MUST swap staves to survive".
    // A stack each StackInterval while its staff is the last drawn; each squeezes how the player fights rather than how
    // much they can take: Static slows their eitr's return, Scorch their stamina's, by m_regenCut a stack (at first each
    // made them a step weaker to the element, until the user: "becoming very weak to an element is certain death [...]
    // there is no resistance to lightning in the vanilla game anyway"; they took Claude's proposal: "go with your
    // proposal"). From m_maxStacks on, every stack brings a smite from the element's twin (TwinsDuel.Stack) and a step of
    // SmiteWeakness; it no longer starts again after a smite (the user, 2026-11-15: "the 5 stacks shouldn't disappear
    // when the player it's hit, they should continue to build while they still hold the same staff and then they will
    // get a new smite for every additional stack (6,7 etc). This will punish players for not swapping staves"). Past the
    // last, regen stays at none. Once the other staff is drawn it drains to nothing, slowing nothing meanwhile, and
    // builds on from where it is if its staff is drawn again first (the fight's redesign, agreed with the user
    // 2026-11-15; before it, it was held at 4 stacks until the other staff's charge reached 4).
    // The charge itself is TwinsDuel's, which tells this what to show (Build, Drain), on the player's own game (its
    // SEMan): in hand, its stacks on its icon; draining, the seconds until it's gone (with an "s": the game's own timers
    // show bare seconds, which would read as stacks). It's there for as long as there's any charge, so its icon says the
    // staff isn't clean, and drawing it would be answered at once. Every game sees the drawn staff's charge on the
    // player (ChargeLook, from their ZDO). No time limit of its own: it goes when TwinsDuel says, or with the fight
    internal class ElementCharge : StatusEffect
    {
        internal const string StaticName = "SE_NhxStatic";
        internal const string ScorchName = "SE_NhxScorch";

        public HitData.DamageType m_element = HitData.DamageType.Lightning;
        public int m_maxStacks = 5;
        public float m_regenCut = 0.2f;

        private int m_stacks;
        // The other staff drawn: draining, building nothing and slowing nothing, gone in m_cleanIn seconds
        private bool m_draining;
        private float m_cleanIn;

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
            m_stacks = 0;
            m_draining = false;
            m_cleanIn = 0f;
        }

        // Its staff the last drawn: at `stacks`, each slowing the regen
        internal void Build(int stacks)
        {
            m_stacks = stacks;
            m_draining = false;
        }

        // The other staff drawn: draining, what's left of it gone in `cleanIn` seconds
        internal void Drain(int stacks, float cleanIn)
        {
            m_stacks = stacks;
            m_draining = true;
            m_cleanIn = cleanIn;
        }

        private float Left => m_draining ? 1f : Mathf.Max(0f, 1f - m_regenCut * m_stacks);

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
            return m_draining ? Mathf.CeilToInt(m_cleanIn) + "s" : m_stacks.ToString();
        }

        public override string GetTooltipString()
        {
            return m_draining
                ? $"{m_tooltip}\n$se_nhx_charge_draining <color=orange>{Mathf.CeilToInt(m_cleanIn)}s</color>."
                : $"{m_tooltip} <color=orange>{m_stacks}/{m_maxStacks}</color>";
        }
    }
}
