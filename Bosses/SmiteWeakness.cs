using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using NhxMagicWeapons.Staves;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // What the twins' overload smites leave on a player (TwinsDuel.Stack): Stormstruck from the Stormcaller's, Flamestruck
    // from the Firecaller's. The user (2026-11-15): "the smite that happens when a player reaches 5 stacks of either fire
    // or lightning debuff will apply a 1 minute long debuff which reduces resistances to that element. And this debuff
    // should stack and refresh if the player gets hit again before it expires".
    // A stack is a step down the game's ladder, as the staves' own weakness takes it (Staves\ElementWeakness.Weaker): from
    // resistant (the set's cloak) to normal, then weak, then very weak. Each overload smite adds one and brings it back to
    // Duration; all of it goes Duration after the last. Given after the smite's hit (that smite hits as it did before), it
    // stays through a swap: holding a staff too long costs a minute more of being weaker to its element, to everything of
    // it (the twins' spells, their second phase), not only the smites.
    // Taken after every other effect on the player (ApplyLast): a fire resistance mead drunk after it doesn't put the
    // resistance back (Apply takes a mead's "resistant" over "weak", and the effects go in the order they came); a mead
    // drunk before it is, as the cloak, a rung higher to start from.
    // On the player's own game (its SEMan), as the charge it comes from; the count is on its icon (as Static's and
    // Scorch's), the time left in its tooltip
    internal class SmiteWeakness : StatusEffect
    {
        internal const string StormName = "SE_NhxStormstruck";
        internal const string FireName = "SE_NhxFlamestruck";
        internal const float Duration = 60f;
        // From very resistant to very weak: more would change nothing
        private const int MaxSteps = 4;

        public HitData.DamageType m_element = HitData.DamageType.Lightning;

        private int m_steps;

        internal int Steps => m_steps;

        // Registered with Jotunn, so its hash finds it (SEMan.AddStatusEffect looks it up in ObjectDB)
        internal static StatusEffect Add(string name, string key, string tooltip, HitData.DamageType element, Sprite icon)
        {
            SmiteWeakness weakness = ScriptableObject.CreateInstance<SmiteWeakness>();
            weakness.name = name;
            weakness.m_name = key;
            weakness.m_tooltip = tooltip;
            weakness.m_icon = icon;
            weakness.m_ttl = Duration;
            weakness.m_element = element;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(weakness, false));
            return weakness;
        }

        public override void Setup(Character character)
        {
            base.Setup(character);
            m_steps = 1;
        }

        // Another smite before it went (SEMan.AddStatusEffect with resetTime)
        public override void ResetTime()
        {
            base.ResetTime();
            if (m_steps < MaxSteps)
            {
                m_steps++;
            }
        }

        // Run out or taken off (SEMan stops it while it's still on the player): in the fight's log, with the resistance it
        // leaves (no steps: ApplyLast counts none)
        public override void Stop()
        {
            base.Stop();
            if (!m_character || m_character != Player.m_localPlayer)
            {
                return;
            }
            int steps = m_steps;
            m_steps = 0;
            string name = m_element == HitData.DamageType.Lightning ? "Stormstruck" : "Flamestruck";
            TwinsLog.Event($"{TwinsLog.Name(m_character)}'s {name} {(GetRemaningTime() <= 0f ? "wears off" : "is taken off early")} at {steps} step{(steps == 1 ? "" : "s")}: "
                + $"{m_element.ToString().ToLowerInvariant()} resistance back to {m_character.GetDamageModifier(m_element)}.");
        }

        public override string GetIconText()
        {
            return m_steps.ToString();
        }

        public override string GetTooltipString()
        {
            return $"{m_tooltip} <color=orange>{m_steps}/{MaxSteps}, {GetTimeString(GetRemaningTime(), true)}</color>";
        }

        // Every resistance the player has from their armor and their other effects is in by now
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ApplyDamageMods))]
        private static class ApplyLast
        {
            private static void Postfix(SEMan __instance, ref HitData.DamageModifiers mods)
            {
                foreach (StatusEffect effect in __instance.m_statusEffects)
                {
                    if (!(effect is SmiteWeakness weakness))
                    {
                        continue;
                    }
                    if (weakness.m_element == HitData.DamageType.Lightning)
                    {
                        mods.m_lightning = ElementWeakness.Weaker(mods.m_lightning, weakness.m_steps);
                    }
                    else if (weakness.m_element == HitData.DamageType.Fire)
                    {
                        mods.m_fire = ElementWeakness.Weaker(mods.m_fire, weakness.m_steps);
                    }
                }
            }
        }
    }
}
