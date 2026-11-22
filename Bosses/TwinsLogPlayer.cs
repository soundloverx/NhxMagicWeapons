using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The local player's side of the fight in the log (the user, 2026-11-22, the fight to be tuned again for characters
    // with about twice the health, stamina regen and eitr regen of the test one: "Can we make our logs detailed enough to
    // record the fight properly? To log when players drink potions, what buffs they get, etc"). Each game logs its own
    // player, from joining the fight to leaving it:
    // - As they join it, what they bring: health, stamina and eitr, the regen of each with every effect on them, armor,
    //   food, effects, gear and its modifiers, resistances, skills, and the world's settings that aren't the default
    // - In it: what they eat and drink and what it gives, effects gained and lost (not the duel's charges and weaknesses:
    //   TwinsDuel logs those), gear put on and taken off (not the staves: TwinsDuel logs draws), staggers (the twins' too,
    //   on the game that runs them), running out of stamina or eitr, healing (added up by cause, as damage outside hits
    //   is), and every FlushEvery seconds their health, stamina and eitr with the regen multipliers
    // - In the fight's summary: what they ate and drank, what healed them, the stamina and eitr spent, attacks by weapon,
    //   dodges, staggers, and how often they ran out of stamina or eitr
    internal static partial class TwinsLog
    {
        // The game flashes an empty bar on every refused try: running out again within this many seconds of the last
        // flash is the same time
        private const float OutGap = 3f;

        private static readonly Dictionary<string, int> s_consumed = new Dictionary<string, int>();
        private static readonly Dictionary<string, float> s_healed = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> s_attacks = new Dictionary<string, int>();
        private static float s_staminaSpent;
        private static float s_eitrSpent;
        private static int s_dodges;
        private static int s_perfectDodges;
        private static int s_staggers;
        private static int s_outOfStamina;
        private static int s_outOfEitr;
        private static float s_lastOutOfStamina = float.NegativeInfinity;
        private static float s_lastOutOfEitr = float.NegativeInfinity;
        private static int s_waves;
        private static float s_waveEitr;
        private static float s_waveStamina;

        // The local player, in the fight (TwinsDuel), with the log on
        private static bool Mine(Character character)
        {
            return On && character && character == Player.m_localPlayer && TwinsDuel.InFight;
        }

        // With StartFight
        private static void StartPlayer()
        {
            s_consumed.Clear();
            s_healed.Clear();
            s_attacks.Clear();
            s_staminaSpent = 0f;
            s_eitrSpent = 0f;
            s_dodges = 0;
            s_perfectDodges = 0;
            s_staggers = 0;
            s_outOfStamina = 0;
            s_outOfEitr = 0;
            s_lastOutOfStamina = float.NegativeInfinity;
            s_lastOutOfEitr = float.NegativeInfinity;
            s_waves = 0;
            s_waveEitr = 0f;
            s_waveStamina = 0f;
        }

        // The altar's wave took from the local player (TwinsWave)
        internal static void Waved(Player player, bool eitr, float before, float after, float max)
        {
            if (!Mine(player))
            {
                return;
            }
            s_waves++;
            if (eitr)
            {
                s_waveEitr += before - after;
            }
            else
            {
                s_waveStamina += before - after;
            }
            Event($"The altar's wave takes {before - after:0} {(eitr ? "eitr" : "stamina")} from {Name(player)}: {before:0} -> {after:0} of {max:0}.");
        }

        // ---- What they bring ----

        // As the local player joins the fight (TwinsDuel, after its line)
        internal static void Loadout(Player player)
        {
            if (!On || !player)
            {
                return;
            }
            string name = Name(player);
            Event($"{name} brings: {Pools(player)}; armor {player.GetBodyArmor():0.#}; {Regen(player)}.");
            Event($"{name}'s food: {Foods(player)}.");
            Event($"{name}'s effects: {Effects(player)}.");
            Event($"{name}'s gear: {Gear(player)}.");
            Event($"{name}'s resistances: {Resistances(player)}.");
            Event($"{name}'s skills: {SkillLevels(player)}.");
            Event($"World settings: {World()}.");
        }

        private static string Pools(Player player)
        {
            string text = $"health {player.GetHealth():0}/{player.GetMaxHealth():0}, stamina {player.GetStamina():0}/{player.GetMaxStamina():0}, "
                + $"eitr {player.GetEitr():0}/{player.GetMaxEitr():0}";
            if (player.GetMaxAdrenaline() > 0f)
            {
                text += $", adrenaline {player.GetAdrenaline():0}/{player.GetMaxAdrenaline():0}";
            }
            return text;
        }

        // As Player.UpdateStats and UpdateFood work it out: stamina and eitr per second, from their base when full to more
        // when empty, none in an attack or a dodge or for a moment after using some; health every 10 s, from the food
        private static string Regen(Player player)
        {
            float food = 0f;
            foreach (Player.Food eaten in player.GetFoods())
            {
                food += eaten.m_item.m_shared.m_foodRegen;
            }
            float health = HealthRegen(player);
            float stamina = player.m_staminaRegen;
            float eitr = player.m_eiterRegen;
            return $"health regen {food * health:0.#} every 10 s (the food's {food:0.#} x{health:0.00}); "
                + $"stamina regen {stamina:0.#}-{stamina * (1f + player.m_staminaRegenTimeMultiplier):0.#}/s (full-empty) x{StaminaRegen(player):0.00}, "
                + $"{player.m_staminaRegenDelay:0.#} s after using some; "
                + $"eitr regen {eitr:0.#}-{eitr * 2f:0.#}/s (full-empty) x{EitrRegen(player):0.00} (the gear's {player.GetEquipmentEitrRegenModifier():+0.00;-0.00;0} in it), "
                + $"{player.m_eitrRegenDelay:0.#} s after using some";
        }

        // The multipliers on each regen, with every effect on the player (the duel's charges, meads, the armor's set...):
        // Player.UpdateStats adds the gear's eitr regen after the effects' (Static's cap allows for it: ElementCharge)
        internal static float StaminaRegen(Player player)
        {
            float multiplier = 1f;
            player.GetSEMan().ModifyStaminaRegen(ref multiplier);
            return multiplier;
        }

        internal static float EitrRegen(Player player)
        {
            float multiplier = 1f;
            player.GetSEMan().ModifyEitrRegen(ref multiplier);
            return multiplier + player.GetEquipmentEitrRegenModifier();
        }

        private static float HealthRegen(Player player)
        {
            float multiplier = 1f;
            player.GetSEMan().ModifyHealthRegen(ref multiplier);
            return multiplier;
        }

        private static string Foods(Player player)
        {
            List<string> parts = new List<string>();
            foreach (Player.Food food in player.GetFoods())
            {
                ItemDrop.ItemData.SharedData shared = food.m_item.m_shared;
                parts.Add($"{Localize(shared.m_name)} ({food.m_health:0} health, {food.m_stamina:0} stamina, {food.m_eitr:0} eitr now, "
                    + $"of {shared.m_food:0}/{shared.m_foodStamina:0}/{shared.m_foodEitr:0}; regen {shared.m_foodRegen:0.#}; {Clock(FoodSeconds(food.m_time))} left)");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "none";
        }

        // Food burns at the world's rate
        private static float FoodSeconds(float time)
        {
            return Game.m_foodRate > 0f ? time / Game.m_foodRate : time;
        }

        private static string Effects(Player player)
        {
            List<string> parts = new List<string>();
            foreach (StatusEffect effect in player.GetSEMan().GetStatusEffects())
            {
                if (effect && !Ours(effect))
                {
                    parts.Add(Effect(effect));
                }
            }
            return parts.Count > 0 ? string.Join("; ", parts) : "none";
        }

        private static string Gear(Player player)
        {
            List<string> parts = new List<string>();
            Slot(parts, "right hand", player.m_rightItem);
            Slot(parts, "left hand", player.m_leftItem);
            Slot(parts, "head", player.m_helmetItem);
            Slot(parts, "chest", player.m_chestItem);
            Slot(parts, "legs", player.m_legItem);
            Slot(parts, "cape", player.m_shoulderItem);
            Slot(parts, "utility", player.m_utilityItem);
            Slot(parts, "trinket", player.m_trinketItem);
            Slot(parts, "ammo", player.m_ammoItem);
            List<string> modifiers = new List<string>();
            Modifier(modifiers, "movement", player.GetEquipmentMovementModifier());
            Modifier(modifiers, "eitr regen", player.GetEquipmentEitrRegenModifier());
            Modifier(modifiers, "attack stamina", player.GetEquipmentAttackStaminaModifier());
            Modifier(modifiers, "block stamina", player.GetEquipmentBlockStaminaModifier());
            Modifier(modifiers, "dodge stamina", player.GetEquipmentDodgeStaminaModifier());
            Modifier(modifiers, "jump stamina", player.GetEquipmentJumpStaminaModifier());
            Modifier(modifiers, "run stamina", player.GetEquipmentRunStaminaModifier());
            Modifier(modifiers, "heat resistance", player.GetEquipmentHeatResistanceModifier());
            return (parts.Count > 0 ? string.Join(", ", parts) : "nothing")
                + $"; modifiers: {(modifiers.Count > 0 ? string.Join(", ", modifiers) : "none")}";
        }

        private static void Slot(List<string> parts, string slot, ItemDrop.ItemData item)
        {
            if (item != null)
            {
                parts.Add($"{slot} {ItemName(item)}");
            }
        }

        private static void Modifier(List<string> parts, string what, float value)
        {
            if (Mathf.Abs(value) > 0.0001f)
            {
                parts.Add($"{what} {value * 100f:+0;-0}%");
            }
        }

        private static string Resistances(Player player)
        {
            HitData.DamageModifiers mods = player.GetDamageModifiers();
            List<string> parts = new List<string>();
            foreach (HitData.DamageType type in s_types)
            {
                HitData.DamageModifier mod = mods.GetModifier(type);
                if (mod != HitData.DamageModifier.Normal)
                {
                    parts.Add($"{type.ToString().ToLowerInvariant()} {mod}");
                }
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "none";
        }

        // Every skill they have, highest first (the levels as learned: effects that raise one show among the effects)
        private static string SkillLevels(Player player)
        {
            Skills skills = player.GetSkills();
            if (!skills)
            {
                return "unknown";
            }
            List<string> parts = skills.GetSkillList()
                .Where(skill => skill != null && skill.m_info != null && skill.m_level >= 1f)
                .OrderByDescending(skill => skill.m_level)
                .Select(skill => $"{skill.m_info.m_skill} {skill.m_level:0.#}")
                .ToList();
            return parts.Count > 0 ? string.Join(", ", parts) : "none";
        }

        // The world's modifiers, those that aren't the default (the server's, on a server)
        private static string World()
        {
            List<string> parts = new List<string>();
            Rate(parts, "player damage", Game.m_playerDamageRate);
            Rate(parts, "enemy damage", Game.m_enemyDamageRate);
            Rate(parts, "damage taken", Game.m_localDamgeTakenRate);
            Rate(parts, "stamina use", Game.m_staminaRate);
            Rate(parts, "move stamina", Game.m_moveStaminaRate);
            Rate(parts, "stamina regen", Game.m_staminaRegenRate);
            Rate(parts, "eitr use", Game.m_eitrRate);
            Rate(parts, "food burn", Game.m_foodRate);
            Rate(parts, "adrenaline", Game.m_adrenalineRate);
            return parts.Count > 0 ? string.Join(", ", parts) : "the default";
        }

        private static void Rate(List<string> parts, string what, float rate)
        {
            if (Mathf.Abs(rate - 1f) > 0.0001f)
            {
                parts.Add($"{what} x{rate:0.##}");
            }
        }

        // ---- Their health, stamina and eitr as the fight goes (Update, every FlushEvery seconds) ----

        private static void Sample()
        {
            Player player = Player.m_localPlayer;
            if (!Mine(player))
            {
                return;
            }
            Event($"{Name(player)}: {Pools(player)}; regen x{HealthRegen(player):0.00} health, x{StaminaRegen(player):0.00} stamina, x{EitrRegen(player):0.00} eitr.");
        }

        // ---- Eating and drinking ----

        // The item being consumed, and the effects it gave, for one line once it's done (Player.ConsumeItem adds its
        // effect before it eats it as food)
        private static ItemDrop.ItemData s_consuming;
        private static readonly List<string> s_consumeGains = new List<string>();
        private static float s_maxHealthBefore;
        private static float s_maxStaminaBefore;
        private static float s_maxEitrBefore;

        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
        private static class Consumed
        {
            private static void Prefix(Player __instance, ItemDrop.ItemData item)
            {
                s_consuming = null;
                s_consumeGains.Clear();
                if (item == null || !Mine(__instance))
                {
                    return;
                }
                s_consuming = item;
                s_maxHealthBefore = __instance.GetMaxHealth();
                s_maxStaminaBefore = __instance.GetMaxStamina();
                s_maxEitrBefore = __instance.GetMaxEitr();
            }

            private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
            {
                if (s_consuming == null || s_consuming != item)
                {
                    s_consuming = null;
                    return;
                }
                s_consuming = null;
                if (!__result)
                {
                    s_consumeGains.Clear();
                    return;
                }
                ItemDrop.ItemData.SharedData shared = item.m_shared;
                string name = Localize(shared.m_name);
                AddTo(s_consumed, name);
                List<string> gives = new List<string>(s_consumeGains);
                s_consumeGains.Clear();
                if (shared.m_food > 0f)
                {
                    gives.Add($"food: {shared.m_food:0} health, {shared.m_foodStamina:0} stamina, {shared.m_foodEitr:0} eitr, regen {shared.m_foodRegen:0.#}, "
                        + $"for {Clock(FoodSeconds(shared.m_foodBurnTime))}");
                    gives.Add($"max health {s_maxHealthBefore:0} -> {__instance.GetMaxHealth():0}, stamina {s_maxStaminaBefore:0} -> {__instance.GetMaxStamina():0}, "
                        + $"eitr {s_maxEitrBefore:0} -> {__instance.GetMaxEitr():0}");
                }
                string verb = shared.m_food > 0f ? "eats" : shared.m_consumeStatusEffect ? "drinks" : "uses";
                Event($"{Name(__instance)} {verb} {name}{(gives.Count > 0 ? ": " + string.Join("; ", gives) : "")}.");
            }
        }

        // ---- Effects ----

        // The duel's charges and weaknesses, which it logs itself
        private static bool Ours(StatusEffect effect)
        {
            return effect is ElementCharge || effect is SmiteWeakness;
        }

        // "Healing mead for 0:10 (Health: +50)": an effect, the time it has left, what it does (its tooltip past its
        // description)
        private static string Effect(StatusEffect effect)
        {
            string text = EffectName(effect);
            if (effect.m_ttl > 0f)
            {
                text += $" for {Clock(effect.GetRemaningTime())}";
            }
            string does = Does(effect);
            return does.Length > 0 ? $"{text} ({does})" : text;
        }

        private static string EffectName(StatusEffect effect)
        {
            string name = Localize(effect.m_name);
            return string.IsNullOrEmpty(name) ? effect.name : name;
        }

        private static readonly Regex s_tags = new Regex("<[^>]*>");

        private static string Does(StatusEffect effect)
        {
            string tooltip;
            try
            {
                tooltip = effect.GetTooltipString();
            }
            catch
            {
                return "";
            }
            if (string.IsNullOrEmpty(tooltip))
            {
                return "";
            }
            if (!string.IsNullOrEmpty(effect.m_tooltip) && tooltip.StartsWith(effect.m_tooltip))
            {
                tooltip = tooltip.Substring(effect.m_tooltip.Length);
            }
            tooltip = s_tags.Replace(Localize(tooltip), "");
            return string.Join(", ", tooltip.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
        }

        // A new effect on the local player (one already on them only has its time reset, and isn't new)
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), typeof(StatusEffect), typeof(bool), typeof(int), typeof(float), typeof(short))]
        private static class EffectGained
        {
            private static void Postfix(SEMan __instance, StatusEffect __result)
            {
                if (!__result || Ours(__result) || !Mine(__instance.m_character))
                {
                    return;
                }
                string text = Effect(__result);
                if (s_consuming != null)
                {
                    s_consumeGains.Add(text);
                    return;
                }
                Event($"{Name(__instance.m_character)} gains {text}.");
            }
        }

        // An effect leaving the local player, run out or taken off (SEMan stops each either way); not all of them going
        // as they die
        [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.Stop))]
        private static class EffectLost
        {
            private static void Prefix(StatusEffect __instance)
            {
                Character character = __instance.m_character;
                if (Ours(__instance) || !Mine(character) || character.IsDead() || character.GetHealth() <= 0f)
                {
                    return;
                }
                string how = __instance.IsDone() ? "ran out"
                    : __instance.m_ttl > 0f ? $"taken off with {Clock(__instance.GetRemaningTime())} left"
                    : "taken off";
                Event($"{Name(character)} loses {EffectName(__instance)} ({how}).");
            }
        }

        // ---- Gear ----

        // Not the staves: TwinsDuel logs draws
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        private static class Equipped
        {
            private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result)
            {
                if (__result && item != null && TwinsDuel.ElementOf(item) == Element.None && Mine(__instance))
                {
                    Event($"{Name(__instance)} equips {ItemName(item)}.");
                }
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        private static class Unequipped
        {
            private static void Prefix(Humanoid __instance, ItemDrop.ItemData item)
            {
                if (item != null && TwinsDuel.ElementOf(item) == Element.None && Mine(__instance) && __instance.IsItemEquiped(item))
                {
                    Event($"{Name(__instance)} takes off {ItemName(item)}.");
                }
            }
        }

        // ---- Staggers ----

        // On the character's owner: in a hit being taken, said on its line; otherwise a line of its own
        [HarmonyPatch(typeof(Character), "RPC_Stagger")]
        private static class Staggered
        {
            private static void Prefix(Character __instance)
            {
                if (!Logged(__instance) || __instance.IsStaggering())
                {
                    return;
                }
                if (Mine(__instance))
                {
                    s_staggers++;
                }
                if (s_pending != null && s_pending.m_target == __instance)
                {
                    s_pending.m_staggers = true;
                    return;
                }
                Event($"{Name(__instance)} is staggered.");
            }
        }

        // ---- Running out ----

        // The game flashes the bar when something needs more than the player has: stamina for an attack, a dodge, a jump,
        // running; eitr for an attack
        [HarmonyPatch(typeof(Hud), nameof(Hud.StaminaBarEmptyFlash))]
        private static class OutOfStamina
        {
            private static void Prefix() => RanOut(false);
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.EitrBarEmptyFlash))]
        private static class OutOfEitr
        {
            private static void Prefix() => RanOut(true);
        }

        private static void RanOut(bool eitr)
        {
            Player player = Player.m_localPlayer;
            // The altar's wave flashes the bar it drains (TwinsWave): not running out
            if (TwinsWave.Flashing || !Mine(player))
            {
                return;
            }
            float now = Time.time;
            float last = eitr ? s_lastOutOfEitr : s_lastOutOfStamina;
            if (eitr)
            {
                s_lastOutOfEitr = now;
            }
            else
            {
                s_lastOutOfStamina = now;
            }
            if (now - last < OutGap)
            {
                return;
            }
            if (eitr)
            {
                s_outOfEitr++;
                Event($"{Name(player)} is out of eitr ({player.GetEitr():0.#}/{player.GetMaxEitr():0}): an attack refused.");
            }
            else
            {
                s_outOfStamina++;
                Event($"{Name(player)} is out of stamina ({player.GetStamina():0.#}/{player.GetMaxStamina():0}).");
            }
        }

        // ---- Healing ----

        // What is healing, while it runs: an effect (a mead's health over time or up front, Rested...) or the food's regen
        private static SE_Stats s_healingEffect;
        private static bool s_healingFood;

        [HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.UpdateStatusEffect))]
        private static class EffectTick
        {
            private static void Prefix(SE_Stats __instance) => s_healingEffect = __instance;

            private static void Finalizer() => s_healingEffect = null;
        }

        [HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.StartupEffects))]
        private static class EffectStart
        {
            private static void Prefix(SE_Stats __instance) => s_healingEffect = __instance;

            private static void Finalizer() => s_healingEffect = null;
        }

        [HarmonyPatch(typeof(Player), "UpdateFood")]
        private static class FoodTick
        {
            private static void Prefix() => s_healingFood = true;

            private static void Finalizer() => s_healingFood = false;
        }

        // On the character's owner, what it took back (up to its max), added up by cause
        [HarmonyPatch(typeof(Character), "RPC_Heal")]
        private static class Healed
        {
            private static void Prefix(Character __instance, out float __state)
            {
                __state = Logged(__instance) ? __instance.GetHealth() : -1f;
            }

            private static void Postfix(Character __instance, float __state)
            {
                if (__state < 0f)
                {
                    return;
                }
                float healed = __instance.GetHealth() - __state;
                if (healed <= 0f)
                {
                    return;
                }
                string cause = s_healingEffect ? EffectName(s_healingEffect) : s_healingFood ? "food regen" : Source();
                if (string.IsNullOrEmpty(cause))
                {
                    cause = "healing";
                }
                AddTally(__instance, cause, healed, true);
                if (Mine(__instance))
                {
                    AddTo(s_healed, cause, healed);
                }
            }
        }

        // ---- Spending, attacks, dodges (for the summary) ----

        [HarmonyPatch(typeof(Player), "RPC_UseStamina")]
        private static class StaminaUsed
        {
            private static void Prefix(Player __instance, float v)
            {
                if (v > 0f && Mine(__instance))
                {
                    s_staminaSpent += Mathf.Min(v, __instance.m_stamina);
                }
            }
        }

        [HarmonyPatch(typeof(Player), "RPC_UseEitr")]
        private static class EitrUsed
        {
            private static void Prefix(Player __instance, float v)
            {
                if (v > 0f && Mine(__instance))
                {
                    s_eitrSpent += Mathf.Min(v, __instance.m_eitr);
                }
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class Attacked
        {
            private static void Postfix(Humanoid __instance, bool secondaryAttack, bool __result)
            {
                if (!__result || !Mine(__instance))
                {
                    return;
                }
                ItemDrop.ItemData weapon = __instance.GetCurrentWeapon();
                string name = weapon != null ? Localize(weapon.m_shared.m_name) : "nothing";
                AddTo(s_attacks, secondaryAttack ? name + " (secondary)" : name);
            }
        }

        // A queued dodge that goes: Player.UpdateDodge zeroes its timer as it starts one
        [HarmonyPatch(typeof(Player), "UpdateDodge")]
        private static class Dodged
        {
            private static void Prefix(Player __instance, float dt, out bool __state)
            {
                __state = Mine(__instance) && __instance.m_queuedDodgeTimer - dt > 0f;
            }

            private static void Postfix(Player __instance, bool __state)
            {
                if (__state && __instance.m_queuedDodgeTimer == 0f)
                {
                    s_dodges++;
                }
            }
        }

        // A hit landing in a dodge (the game gives stamina back for it once a dodge)
        [HarmonyPatch(typeof(Player), "RPC_HitWhileDodging")]
        private static class PerfectDodge
        {
            private static void Prefix(Player __instance)
            {
                if (Mine(__instance) && __instance.m_nview.IsOwner() && !__instance.m_beenHitWhileDodging)
                {
                    s_perfectDodges++;
                }
            }
        }

        // For the fight's summary (TwinsDuel.Summarize)
        internal static string FightPlayer()
        {
            return $"ate and drank {Counts(s_consumed)}; healed {s_healed.Values.Sum():0} ({Amounts(s_healed)}); "
                + $"spent {s_staminaSpent:0} stamina and {s_eitrSpent:0} eitr; attacks: {Counts(s_attacks)}; "
                + $"{s_dodges} dodges ({s_perfectDodges} with a hit landing in them); staggered {s_staggers} times; "
                + $"ran out of stamina {s_outOfStamina} times and of eitr {s_outOfEitr} times; "
                + $"the altar's waves took {s_waveEitr:0} eitr and {s_waveStamina:0} stamina in {s_waves} waves";
        }

        // ---- Pieces ----

        private static string ItemName(ItemDrop.ItemData item)
        {
            return $"{Localize(item.m_shared.m_name)} (q{item.m_quality})";
        }

        private static string Localize(string text)
        {
            return Localization.instance != null && !string.IsNullOrEmpty(text) ? Localization.instance.Localize(text) : text;
        }

        private static string Clock(float seconds)
        {
            int whole = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{whole / 60}:{whole % 60:00}";
        }

        private static void AddTo(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        private static void AddTo(Dictionary<string, float> amounts, string key, float amount)
        {
            amounts.TryGetValue(key, out float sum);
            amounts[key] = sum + amount;
        }

        private static string Counts(Dictionary<string, int> counts)
        {
            return counts.Count > 0 ? string.Join(", ", counts.OrderByDescending(entry => entry.Value).Select(entry => $"{entry.Key} x{entry.Value}")) : "nothing";
        }

        private static string Amounts(Dictionary<string, float> amounts)
        {
            return amounts.Count > 0 ? string.Join(", ", amounts.OrderByDescending(entry => entry.Value).Select(entry => $"{entry.Value:0} from {entry.Key}")) : "nothing";
        }
    }
}
