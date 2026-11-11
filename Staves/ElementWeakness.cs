using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace NhxMagicWeapons.Staves
{
    // A staff's hits leave what they hit less resistant to its element for a while. The user's friends (2026-11-11): "the
    // staff hits should apply a weakness to its element for 3 seconds on hit to all enemies hit"; then the user: "make it
    // lower resistance by one step as part of the staff's power. and with the armor set bonus, lower resistance by one
    // step per hit instead".
    // A step is one rung down the game's ladder: very resistant, resistant, normal, weak, very weak; the slight ones go to
    // the next rung (slightly resistant to normal, slightly weak to weak). Immune and Ignore stay as they are (picked
    // here: immunity is how the game shapes some fights, e.g. the Frozen King's second phase to lightning).
    // The staff's own (m_stacking false): one step, renewed by each hit. It's the staves' m_attackStatusEffect, which
    // Attack puts on each attack's HitData, and every hit made from that HitData keeps it (chain jumps, blasts...). The
    // set's (m_stacking true): one step more with each hit, down to very weak, all gone 3 s after the last; the set's
    // effect (Armor\LevelSetBonus) puts it on the attack's HitData in place of the staff's.
    // On the target's owner's game: Character.RPC_Damage adds the effect, or renews it (ResetTime) if the target has it,
    // before reading the target's resistances, so the hit that brings a step already counts it
    internal class ElementWeakness : StatusEffect
    {
        internal const float Duration = 3f;
        // From very resistant to very weak
        private const int MaxSteps = 4;

        public HitData.DamageType m_element = HitData.DamageType.Fire;
        public bool m_stacking;

        private int m_steps;

        // Registered with Jotunn, so a hit's hash finds it on every game (ObjectDB)
        internal static StatusEffect Add(string name, string key, string tooltip, HitData.DamageType element, bool stacking, Sprite icon)
        {
            ElementWeakness weakness = ScriptableObject.CreateInstance<ElementWeakness>();
            weakness.name = name;
            weakness.m_name = key;
            weakness.m_tooltip = tooltip;
            weakness.m_icon = icon;
            weakness.m_ttl = Duration;
            weakness.m_element = element;
            weakness.m_stacking = stacking;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(weakness, false));
            return weakness;
        }

        public override void Setup(Character character)
        {
            base.Setup(character);
            m_steps = 1;
        }

        // Another hit
        public override void ResetTime()
        {
            base.ResetTime();
            if (m_stacking && m_steps < MaxSteps)
            {
                m_steps++;
            }
        }

        public override void ModifyDamageMods(ref HitData.DamageModifiers modifiers)
        {
            switch (m_element)
            {
                case HitData.DamageType.Fire:
                    modifiers.m_fire = Weaker(modifiers.m_fire, m_steps);
                    break;
                case HitData.DamageType.Frost:
                    modifiers.m_frost = Weaker(modifiers.m_frost, m_steps);
                    break;
                case HitData.DamageType.Lightning:
                    modifiers.m_lightning = Weaker(modifiers.m_lightning, m_steps);
                    break;
                case HitData.DamageType.Poison:
                    modifiers.m_poison = Weaker(modifiers.m_poison, m_steps);
                    break;
                case HitData.DamageType.Spirit:
                    modifiers.m_spirit = Weaker(modifiers.m_spirit, m_steps);
                    break;
            }
        }

        internal static HitData.DamageModifier Weaker(HitData.DamageModifier modifier, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                switch (modifier)
                {
                    case HitData.DamageModifier.VeryResistant:
                        modifier = HitData.DamageModifier.Resistant;
                        break;
                    case HitData.DamageModifier.Resistant:
                    case HitData.DamageModifier.SlightlyResistant:
                        modifier = HitData.DamageModifier.Normal;
                        break;
                    case HitData.DamageModifier.Normal:
                    case HitData.DamageModifier.SlightlyWeak:
                        modifier = HitData.DamageModifier.Weak;
                        break;
                    case HitData.DamageModifier.Weak:
                        modifier = HitData.DamageModifier.VeryWeak;
                        break;
                    // Very weak, Immune, Ignore
                    default:
                        return modifier;
                }
            }
            return modifier;
        }

        // In the staff's tooltip (under its name) and on a player hit in PvP
        public override string GetTooltipString()
        {
            return $"{m_tooltip} <color=orange>{m_ttl:0.#}s</color>";
        }
    }
}
