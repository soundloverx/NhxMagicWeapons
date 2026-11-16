using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The twins' fight in the log (the user, 2026-11-15: "Can we log the damage that goes on bosses and players so we can
    // have a better idea of what happens? As well as events like: Stormcaller targets Dodotester, plays blue-dodo"): lines
    // starting with "[Twins <time>]" in BepInEx's LogOutput.log, each game logging what it sees:
    // - Hits on the twins (on the game that runs the twin) and on the local player while near a twin: from whom, with
    //   what (the staff in their hand; the code that made the hit, when it was made on this game), the damage by type as
    //   it came, the target's resistance to each type, whether it was staggered (the game doubles a hit on a staggered
    //   creature), the scaling by players around (the game's, which CLLC sets), what it took at once and what it will
    //   burn for, its health after. Hits refused, and why
    // - Damage dealt outside a hit (burning, the arcs between players, the burning way, judgment), added up by target
    //   and cause over FlushEvery seconds
    // - Events: summons, roles and targets, voice lines (said, heard, played, dropped), deaths, raises, wins, vanishing,
    //   the second phase and its spells, creatures held, draws and swaps, overloads, judgment, rushes, a fallen twin's
    //   element going silent and sounding again
    // - The staves' duel (TwinsDuel; the user, 2026-11-15: "when players swap staves, the number of stacks, the hits they
    //   take and deliver, the vulnerability debuffs"): every stack with both charges (in hand or draining) and the regen
    //   left, every swap with how long the old staff was out and what became of both charges, Stormstruck and
    //   Flamestruck as they come, grow and wear off with the resistance they leave; on every hit the local player takes,
    //   what the duel has put on them. The local player's hits on a twin another game runs, as sent (that game logs what
    //   it took). And when the player leaves the fight (or dies), a summary: time on each staff, swaps, most stacks,
    //   smites, most steps, damage taken and dealt
    // Off unless the config's "Twins fight log" is on (Plugin.TwinsFightLog; the user, the fight tested: "maybe disable
    // them so as not to spam the player so much")
    internal static class TwinsLog
    {
        private const float FlushEvery = 5f;

        private static bool On => Plugin.TwinsFightLog != null && Plugin.TwinsFightLog.Value;

        internal static void Event(string text)
        {
            if (On)
            {
                Plugin.Log.LogInfo($"[Twins {DateTime.Now:HH:mm:ss.f}] {text}");
            }
        }

        internal static string Name(Character character)
        {
            if (!character)
            {
                return "nobody";
            }
            if (character is Player player)
            {
                return player.GetPlayerName();
            }
            CallerBoss twin = character.GetComponent<CallerBoss>();
            if (twin)
            {
                return twin.Element == Element.Storm ? "Stormcaller" : "Firecaller";
            }
            return Utils.GetPrefabName(character.gameObject);
        }

        internal static string Name(CallerBoss boss) => boss ? Name(boss.m_humanoid) : "nobody";

        internal static string At(Vector3 point) => $"({point.x:0}, {point.z:0})";

        // ---- Hits ----

        // The hit being taken (RPC_Damage doesn't nest)
        private class Pending
        {
            internal Character m_target;
            internal Character m_attacker;
            // On the local player: what the duel had put on them as it came (TwinsDuel.Describe)
            internal string m_duel;
            internal HitData.DamageTypes m_raw;
            internal HitData.DamageModifiers m_mods;
            internal string m_from;
            internal string m_source;
            internal float m_health;
            internal bool m_staggered;
            internal bool m_dodged;
            internal bool m_blocking;
            internal bool m_applied;
            internal float m_took;
            internal float m_scale = 1f;
            internal float m_fire;
            internal float m_spirit;
            internal string m_refused;
        }

        private static Pending s_pending;

        // Damage outside hits, added up
        private class Tally
        {
            internal Character m_target;
            internal string m_cause;
            internal float m_dealt;
            internal int m_count;
        }

        private static readonly Dictionary<string, Tally> s_tallies = new Dictionary<string, Tally>();
        private static readonly List<string> s_flushed = new List<string>();
        private static float s_flushTimer = FlushEvery;

        // The local player's fight, for its summary (TwinsDuel.Summarize): what they took, in hits and otherwise, and
        // what they dealt to twins this game runs (at once, and to burn) or sent to twins another game runs
        private static int s_hitsTaken;
        private static float s_takenFromHits;
        private static float s_takenOther;
        private static int s_hitsLanded;
        private static int s_hitsRefused;
        private static float s_dealtHere;
        private static float s_burnHere;
        private static int s_hitsSent;
        private static float s_sentRaw;

        // As the local player joins the fight (TwinsDuel)
        internal static void StartFight()
        {
            s_hitsTaken = 0;
            s_takenFromHits = 0f;
            s_takenOther = 0f;
            s_hitsLanded = 0;
            s_hitsRefused = 0;
            s_dealtHere = 0f;
            s_burnHere = 0f;
            s_hitsSent = 0;
            s_sentRaw = 0f;
        }

        internal static string FightDamage()
        {
            return $"took {s_takenFromHits:0} in {s_hitsTaken} hits and {s_takenOther:0} otherwise (burning, the arcs, the burning way, judgment); "
                + $"dealt {s_dealtHere:0} at once and {s_burnHere:0} to burn in {s_hitsLanded} hits on twins this game runs ({s_hitsRefused} more refused), "
                + $"and sent {s_hitsSent} hits ({s_sentRaw:0} before resistances) to twins another game runs (its log has what they took)";
        }

        private static bool Logged(Character character)
        {
            if (!On || !character || !character.m_nview || !character.m_nview.IsValid() || !character.m_nview.IsOwner())
            {
                return false;
            }
            if (CallerBoss.IsTwin(character))
            {
                return true;
            }
            return character == Player.m_localPlayer && (TwinsDuel.InFight || TwinsRing.Fight(character.transform.position, out _));
        }

        // A twin's rules refused the hit being taken (TwinsRules)
        internal static void Refuse(Character target, string why)
        {
            if (s_pending != null && s_pending.m_target == target && s_pending.m_refused == null)
            {
                s_pending.m_refused = why;
            }
        }

        // Before any other patch's prefix, so refused hits are seen too
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class HitTaken
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Character __instance, HitData hit)
            {
                s_pending = null;
                if (hit == null || !Logged(__instance))
                {
                    return;
                }
                Character attacker = hit.GetAttacker();
                s_pending = new Pending
                {
                    m_target = __instance,
                    m_attacker = attacker,
                    m_duel = __instance == Player.m_localPlayer ? TwinsDuel.Describe(Player.m_localPlayer, false) : null,
                    m_raw = hit.m_damage,
                    m_mods = __instance.GetDamageModifiers(),
                    m_from = Name(attacker) + Wielding(attacker),
                    m_source = Source(),
                    m_health = __instance.GetHealth(),
                    m_staggered = !__instance.IsPlayer() && __instance.IsStaggering(),
                    m_dodged = hit.m_dodgeable && __instance.IsDodgeInvincible(),
                    m_blocking = hit.m_blockable && __instance.IsBlocking(),
                };
            }

            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Character __instance)
            {
                Pending pending = s_pending;
                if (pending == null || pending.m_target != __instance)
                {
                    return;
                }
                s_pending = null;
                StringBuilder line = new StringBuilder();
                line.Append("Hit on ").Append(Name(__instance)).Append(" from ").Append(pending.m_from);
                if (!string.IsNullOrEmpty(pending.m_source))
                {
                    line.Append(" [").Append(pending.m_source).Append(']');
                }
                line.Append(": ").Append(Damages(pending.m_raw, pending.m_mods));
                if (pending.m_duel != null)
                {
                    line.Append(" [on them: ").Append(pending.m_duel).Append(']');
                }
                if (pending.m_staggered)
                {
                    line.Append(", staggered (x2)");
                }
                if (pending.m_blocking)
                {
                    line.Append(", blocked");
                }
                line.Append(" -> ");
                if (pending.m_refused != null)
                {
                    line.Append("refused (").Append(pending.m_refused).Append(')');
                }
                else if (pending.m_dodged)
                {
                    line.Append("dodged");
                }
                else if (!pending.m_applied && pending.m_fire <= 0f && pending.m_spirit <= 0f)
                {
                    line.Append("nothing taken");
                }
                else
                {
                    line.Append($"took {pending.m_took:0.#}");
                    if (pending.m_fire > 0f || pending.m_spirit > 0f)
                    {
                        line.Append(", to burn for");
                        if (pending.m_fire > 0f)
                        {
                            line.Append($" {pending.m_fire:0.#} fire");
                        }
                        if (pending.m_spirit > 0f)
                        {
                            line.Append($" {pending.m_spirit:0.#} spirit");
                        }
                    }
                    if (Mathf.Abs(pending.m_scale - 1f) > 0.001f)
                    {
                        line.Append($" (players' scaling x{pending.m_scale:0.###})");
                    }
                }
                line.Append("; ").Append(Health(__instance));
                Event(line.ToString());
                Count(pending);
            }
        }

        // For the fight's summary
        private static void Count(Pending pending)
        {
            Player local = Player.m_localPlayer;
            if (!local)
            {
                return;
            }
            if (pending.m_target == local && pending.m_refused == null)
            {
                s_hitsTaken++;
                s_takenFromHits += pending.m_took;
            }
            else if (pending.m_attacker == local && CallerBoss.IsTwin(pending.m_target))
            {
                if (pending.m_refused != null)
                {
                    s_hitsRefused++;
                    return;
                }
                s_hitsLanded++;
                s_dealtHere += pending.m_took;
                s_burnHere += pending.m_fire + pending.m_spirit;
            }
        }

        // The local player's hits on a twin another game runs, as they leave: that game takes them (and logs them). Its
        // resistances aren't known here (its effects are on that game), so the damage is as sent
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class HitSent
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (!On || hit == null || !CallerBoss.IsTwin(__instance) || !__instance.m_nview || !__instance.m_nview.IsValid() || __instance.m_nview.IsOwner())
                {
                    return;
                }
                Character attacker = hit.GetAttacker();
                if (!attacker || attacker != Player.m_localPlayer)
                {
                    return;
                }
                s_hitsSent++;
                s_sentRaw += hit.GetTotalDamage();
                string source = Source();
                Event($"Hit sent to {Name(__instance)} (another game runs it) from {Name(attacker)}{Wielding(attacker)}"
                    + $"{(string.IsNullOrEmpty(source) ? "" : $" [{source}]")}: {Damages(hit.m_damage, new HitData.DamageModifiers())}.");
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        private static class DamageTaken
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Character __instance, out float __state)
            {
                __state = -1f;
                if (s_pending != null && s_pending.m_target == __instance)
                {
                    __state = __instance.GetHealth();
                    s_pending.m_scale = Scale(__instance);
                    return;
                }
                if (Logged(__instance))
                {
                    __state = __instance.GetHealth();
                }
            }

            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Character __instance, HitData hit, float __state)
            {
                if (__state < 0f)
                {
                    return;
                }
                float dealt = __state - __instance.GetHealth();
                if (s_pending != null && s_pending.m_target == __instance)
                {
                    s_pending.m_applied = true;
                    s_pending.m_took += dealt;
                    return;
                }
                if (dealt <= 0f)
                {
                    return;
                }
                if (__instance == Player.m_localPlayer)
                {
                    s_takenOther += dealt;
                }
                string cause = hit != null && hit.m_hitType == HitData.HitType.Burning
                    ? (hit.m_damage.m_spirit > hit.m_damage.m_fire ? "spirit burning" : "burning")
                    : Source();
                string key = __instance.GetInstanceID() + "|" + cause;
                if (!s_tallies.TryGetValue(key, out Tally tally))
                {
                    tally = new Tally { m_target = __instance, m_cause = cause };
                    s_tallies[key] = tally;
                }
                tally.m_dealt += dealt;
                tally.m_count++;
            }
        }

        // The fire and spirit of the hit being taken, as they go on burning (after resistances; the players' scaling
        // comes with each tick)
        [HarmonyPatch(typeof(Character), "AddFireDamage")]
        private static class FireTaken
        {
            private static void Prefix(Character __instance, float damage)
            {
                if (s_pending != null && s_pending.m_target == __instance && damage > 0f)
                {
                    s_pending.m_fire += damage;
                }
            }
        }

        [HarmonyPatch(typeof(Character), "AddSpiritDamage")]
        private static class SpiritTaken
        {
            private static void Prefix(Character __instance, float damage)
            {
                if (s_pending != null && s_pending.m_target == __instance && damage > 0f)
                {
                    s_pending.m_spirit += damage;
                }
            }
        }

        // Deaths near the fight: a twin's (on its game) or the local player's (Player.OnDeath doesn't call Character's)
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Death
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Character __instance) => Died(__instance);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class PlayerDeath
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance) => Died(__instance);
        }

        private static void Died(Character character)
        {
            if (!Logged(character))
            {
                return;
            }
            Flush();
            HitData last = character.m_lastHit;
            Event($"{Name(character)} died at {At(character.transform.position)}, last hit from {(last != null ? Name(last.GetAttacker()) : "nothing")}.");
        }

        // Every frame (Plugin.Update)
        internal static void Update(float dt)
        {
            s_flushTimer -= dt;
            if (s_flushTimer > 0f)
            {
                return;
            }
            s_flushTimer = FlushEvery;
            Flush();
        }

        private static void Flush()
        {
            if (s_tallies.Count == 0)
            {
                return;
            }
            s_flushed.Clear();
            foreach (KeyValuePair<string, Tally> entry in s_tallies)
            {
                Tally tally = entry.Value;
                s_flushed.Add(entry.Key);
                if (!tally.m_target || tally.m_count == 0)
                {
                    continue;
                }
                Event($"{Name(tally.m_target)} took {tally.m_dealt:0.#} from {tally.m_cause} ({tally.m_count} times in the last {FlushEvery:0} s); {Health(tally.m_target)}");
            }
            foreach (string key in s_flushed)
            {
                s_tallies.Remove(key);
            }
        }

        // ---- Pieces of a line ----

        private static readonly HitData.DamageType[] s_types =
        {
            HitData.DamageType.Blunt, HitData.DamageType.Slash, HitData.DamageType.Pierce, HitData.DamageType.Chop,
            HitData.DamageType.Pickaxe, HitData.DamageType.Fire, HitData.DamageType.Frost, HitData.DamageType.Lightning,
            HitData.DamageType.Poison, HitData.DamageType.Spirit,
        };

        private static string Damages(HitData.DamageTypes damage, HitData.DamageModifiers mods)
        {
            StringBuilder text = new StringBuilder();
            if (damage.m_damage > 0f)
            {
                text.Append($"{damage.m_damage:0.#} untyped");
            }
            foreach (HitData.DamageType type in s_types)
            {
                float amount = Amount(damage, type);
                if (amount <= 0f)
                {
                    continue;
                }
                if (text.Length > 0)
                {
                    text.Append(", ");
                }
                text.Append($"{amount:0.#} {type.ToString().ToLowerInvariant()}");
                HitData.DamageModifier mod = mods.GetModifier(type);
                if (mod != HitData.DamageModifier.Normal)
                {
                    text.Append(" (").Append(mod).Append(')');
                }
            }
            return text.Length > 0 ? text.ToString() : "no damage";
        }

        private static float Amount(HitData.DamageTypes damage, HitData.DamageType type)
        {
            switch (type)
            {
                case HitData.DamageType.Blunt: return damage.m_blunt;
                case HitData.DamageType.Slash: return damage.m_slash;
                case HitData.DamageType.Pierce: return damage.m_pierce;
                case HitData.DamageType.Chop: return damage.m_chop;
                case HitData.DamageType.Pickaxe: return damage.m_pickaxe;
                case HitData.DamageType.Fire: return damage.m_fire;
                case HitData.DamageType.Frost: return damage.m_frost;
                case HitData.DamageType.Lightning: return damage.m_lightning;
                case HitData.DamageType.Poison: return damage.m_poison;
                case HitData.DamageType.Spirit: return damage.m_spirit;
                default: return 0f;
            }
        }

        private static string Health(Character character)
        {
            float health = character.GetHealth();
            float max = character.GetMaxHealth();
            return $"health {health:0}/{max:0} ({(max > 0f ? 100f * health / max : 0f):0}%)";
        }

        // What Character.ApplyDamage multiplies by: for a creature, the players around (the game's, which CLLC sets) and
        // the world's damage setting; for a player, the world's damage taken setting
        private static float Scale(Character character)
        {
            if (character.IsPlayer())
            {
                return Game.m_localDamgeTakenRate;
            }
            return (Game.instance ? Game.instance.GetDifficultyDamageScaleEnemy(character.transform.position) : 1f) * Game.m_playerDamageRate;
        }

        // " (Staff of Storms)": what a player has in hand, as every game sees it
        private static string Wielding(Character attacker)
        {
            if (!(attacker is Player player) || !player.m_visEquipment || !ObjectDB.instance)
            {
                return "";
            }
            int hash = player.m_visEquipment.m_currentRightItemHash;
            GameObject prefab = hash != 0 ? ObjectDB.instance.GetItemPrefab(hash) : null;
            ItemDrop item = prefab ? prefab.GetComponent<ItemDrop>() : null;
            if (!item)
            {
                return " (empty-handed)";
            }
            string name = Localization.instance != null ? Localization.instance.Localize(item.m_itemData.m_shared.m_name) : item.name;
            return $" ({name})";
        }

        // The code that made this hit or damage: the first method on the stack that isn't the game's damage, the RPCs
        // carrying it, Harmony's or ours here; a hit from another game's code comes in through the network
        private static readonly HashSet<string> s_passThrough = new HashSet<string>
        {
            nameof(TwinsLog), "Character", "Humanoid", "Player", "ZNetView", "ZRoutedRpc", "RoutedMethod", "RoutedMethod`1",
            "RoutedMethod`2", "RoutedMethod`3", "RoutedMethod`4", "StaffHits", "Action`1", "Action`2", "Action`3",
        };

        private static readonly HashSet<string> s_network = new HashSet<string> { "ZRpc", "ZNet", "ZSteamSocket", "ZPlayFabSocket", "ZDOMan" };

        private static string Source()
        {
            StackTrace trace = new StackTrace(2, false);
            for (int i = 0; i < trace.FrameCount; i++)
            {
                MethodBase method = trace.GetFrame(i)?.GetMethod();
                Type type = method?.DeclaringType;
                if (type == null)
                {
                    continue;
                }
                string methodName = method.Name;
                // A lambda's or a coroutine's class: the class it's in
                while (type.Name.StartsWith("<") && type.DeclaringType != null)
                {
                    if (methodName == "MoveNext" || methodName.StartsWith("<"))
                    {
                        methodName = Inner(type.Name);
                    }
                    type = type.DeclaringType;
                }
                // .NET's own frames too: the game invokes RPCs by reflection (Delegate.DynamicInvoke)
                string space = type.Namespace ?? "";
                if (s_passThrough.Contains(type.Name) || space == "HarmonyLib" || space.StartsWith("MonoMod") || space == "System" || space.StartsWith("System."))
                {
                    continue;
                }
                if (s_network.Contains(type.Name))
                {
                    return "from another game";
                }
                return $"{type.Name}.{Inner(methodName)}";
            }
            return "";
        }

        // "<Fire>d__12" or "<Fire>b__3_0": "Fire"
        private static string Inner(string name)
        {
            int open = name.IndexOf('<');
            int close = name.IndexOf('>');
            return open >= 0 && close > open + 1 ? name.Substring(open + 1, close - open - 1) : name;
        }
    }
}
