using HarmonyLib;
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
    // player (ChargeLook, from their ZDO). No time limit of its own: it goes when TwinsDuel says, or with the fight.
    // From its first stack, while its staff is in hand, it also hastens the other's regen by m_regenBoost, flat whatever
    // its stacks: Static stamina's, Scorch eitr's, so a staff's turn pays back what the other's took. The user
    // (2026-11-20), after running out of stamina with the Firecaller on them: "boost stamina regen once I have one Storm
    // staff debuff and boost eitr regen once I have one Muspelheim staff debuff [...] the regen boosts would have no
    // effect if I am holding the opposite staff"; then "the boost must be flat, not increasing with the stacks". Draining,
    // it does nothing either way.
    // Its own resource's regen is capped, not only slowed (the user, 2026-11-22: "Let's go with A"): in a test fight a
    // player's meads, Rested and gear had eitr regen at x4.5, and a Static of 3 still left x2.6, so neither resource was
    // ever short. With every effect on the player (and for eitr the gear's, which Player.UpdateStats adds after them),
    // it's at most what the charge leaves: x0.8 at 1 stack, x0.4 at 3, none from 5 (Ceiling). The boost to the other
    // stays a share of the rest
    internal class ElementCharge : StatusEffect
    {
        internal const string StaticName = "SE_NhxStatic";
        internal const string ScorchName = "SE_NhxScorch";

        public HitData.DamageType m_element = HitData.DamageType.Lightning;
        public int m_maxStacks = 5;
        public float m_regenCut = 0.2f;
        public float m_regenBoost = 0.5f;

        private int m_stacks;
        // The other staff drawn: draining, building nothing and slowing nothing, gone in m_cleanIn seconds
        private bool m_draining;
        private float m_cleanIn;

        // Registered with Jotunn, so its hash finds it (SEMan.AddStatusEffect looks it up in ObjectDB)
        internal static StatusEffect Add(string name, string key, string tooltip, HitData.DamageType element, Sprite icon, int maxStacks, float regenCut, float regenBoost)
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
            charge.m_regenBoost = regenBoost;
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

        // Its own resource's regen: slowed a step a stack. The other's: hastened, flat, from its first stack
        private float Left => m_draining ? 1f : Mathf.Max(0f, 1f - m_regenCut * m_stacks);
        private float Boost => m_draining || m_stacks < 1 ? 1f : 1f + m_regenBoost;

        public override void ModifyEitrRegen(ref float eitrRegen)
        {
            eitrRegen *= m_element == HitData.DamageType.Lightning ? Left : Boost;
        }

        public override void ModifyStaminaRegen(ref float staminaRegen)
        {
            staminaRegen *= m_element == HitData.DamageType.Fire ? Left : Boost;
        }

        // The cap on `element`'s resource for this SEMan's character: what its charge of that element leaves, in hand from
        // its first stack (none before, or draining)
        private static bool Ceiling(SEMan seman, HitData.DamageType element, out float ceiling)
        {
            ceiling = 1f;
            foreach (StatusEffect effect in seman.GetStatusEffects())
            {
                if (effect is ElementCharge charge && charge.m_element == element && !charge.m_draining && charge.m_stacks >= 1)
                {
                    ceiling = charge.Left;
                    return true;
                }
            }
            return false;
        }

        // Static's cap on eitr regen, after every effect's share (SEMan's). The gear's comes after it in Player.UpdateStats,
        // so what's left for the effects is the cap less the gear's: the sum is at most the cap (and never below none)
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyEitrRegen))]
        private static class EitrCeiling
        {
            private static void Postfix(SEMan __instance, ref float eitrMultiplier)
            {
                if (Ceiling(__instance, HitData.DamageType.Lightning, out float ceiling))
                {
                    float gear = __instance.m_character is Player player ? player.GetEquipmentEitrRegenModifier() : 0f;
                    eitrMultiplier = Mathf.Min(eitrMultiplier, ceiling - gear);
                }
            }
        }

        // Scorch's cap on stamina regen (nothing is added after it there)
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyStaminaRegen))]
        private static class StaminaCeiling
        {
            private static void Postfix(SEMan __instance, ref float staminaMultiplier)
            {
                if (Ceiling(__instance, HitData.DamageType.Fire, out float ceiling))
                {
                    staminaMultiplier = Mathf.Min(staminaMultiplier, ceiling);
                }
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
