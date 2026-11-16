using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace NhxMagicWeapons.Bosses
{
    // The twins' voices, the user's recordings (2026-11-15: "I have some sound files I would like to play during the
    // fight"), in the DLL (the project's Sounds\ as it was built: EmbeddedAudio; a Sounds folder next to the DLL at first),
    // by their file names:
    // - both-intro: both of them, as they're summoned (TwinsBond.Summon); bluered-start until 2026-11-16's recordings
    // - blue-stasis or red-stasis: as they hold the creatures in their arena (TwinsStasis), once per hold, at most every
    //   StasisCooldown. At first either at random, every 5 s at most; the user, 2026-11-15, after four blue-stasis in a
    //   row: "We could also try to alternate the blue-stasis.ogg and red-stasis.ogg as well so we don't get the same voice
    //   over and over, but with a cooldown of 10 seconds". So the first of a game's is either, then they take turns (on
    //   the game that speaks for the arena: another game taking over may repeat one once)
    // - both-defeat: both beaten (TwinsBond.Won); bluered-end until then
    // - blue-busy, red-busy: the Stormcaller, the Firecaller fallen, as the rune that will raise it appears (TwinsBond.Fell)
    // - blue-p2, red-p2: the Stormcaller, the Firecaller entering its second phase (TwinsPhase.Enrage), a raised twin too
    //   (it comes back in it). The user, 2026-11-15: "They shouldn't overlap, so maybe queue the 2 sounds if they happen
    //   at the same time": as every line, the second waits for the first. A raised twin's comes first (the user,
    //   2026-11-16: "the voice line gaining priority over anything else when the boss resurrects, but without stopping
    //   voice lines already in progress"): as the judgment's, after the line playing and ahead of those waiting
    // - the taunts: a twin turning to a player whose name starts with "dodo" or "dovah" (any case; the character's
    //   name). Turning to a player is a new target or starting to hunt them (TwinsAI): alone, a player only changes the
    //   twins' roles, at each swap. At first only the Stormcaller to "dodo" (blue-dodo) and the Firecaller to "dovah"
    //   (red-dovahkiin); the user, 2026-11-16, with new recordings: each twin to each name, cycling through its lines for
    //   that name: "When the Firecaller targets Dodo*, it should cycle through these sounds: red-dodo1.ogg,
    //   red-dodo2.ogg", and red-dovah, blue-dodo, blue-dovah alike. A pairing's lines are however many the DLL has,
    //   numbered from 1 (Lines: a third recording is a file dropped in Sounds\); how many times a twin has said each
    //   name is kept on it, so a game taking it over goes on from there. When it may speak (Targeted):
    //   - one taunt at most every TauntCooldown for the pair (the user, 2026-11-15, having heard blue-dodo once in a solo
    //     fight: "Perhaps the cooldown in combination with the 50% chance is a bit excessive")
    //   - never three in a row (Claude's proposal, 2026-11-16, of three it simulated; the user: "go with A and the held
    //     turn"): no taunt that would be the third running by the same twin (while its twin lives), the third running
    //     to the same name (while someone answering to the other is in the fight), or the last one's pairing again
    //     (while another could be had). The pair's last two taunts are kept with the later one's time, on the twin
    //     that said it. Each round of swaps offers two pairings, and the other two at the next (the Stormcaller to Dodo
    //     and the Firecaller to Dovah, then the Stormcaller to Dovah and the Firecaller to Dodo): a taunt at every
    //     round changes the twin or the name, never both, and both changing every time would be two pairings only,
    //     for ever. So, with the swaps in step: blue-dodo, blue-dovah, red-dovah, red-dodo, round and round, and one
    //     of the two offered can always be said.
    //     Before that the twins took strict turns (the user, 2026-11-15: "the first line is blue-dodo, then fighting,
    //     30 seconds pass, then the bosses will swap targets again, the next line would be red-dovahkiin"), and each
    //     evened out its names (2026-11-16: with turns alone, "we will only hear the bosses' lines for the same player
    //     because they alternate targets"): the names came four in a row, and a round of swaps passed in silence each
    //     time the name changed (45 s or more without a taunt in nine fights of ten, simulated; none in this)
    //   - a turn the cooldown alone refuses is held (the same proposal's): if the cooldown ends within TauntHold and
    //     the twin is still after that player, it taunts them then. The players swap every 20 to 25 s, each on their
    //     own clock, so the second to swap after a taunt was often a few seconds short of the cooldown, and their
    //     taunt lost. A turn a line refuses isn't held: it gives way. What's held is the turn, not a line (the user,
    //     2026-11-16: "make sure the line is still valid when its time comes. So that the boss won't switch to follow
    //     Dovahkiin and then play the queued Dodo voice line"): nothing waits in the queue, and when the cooldown ends
    //     the line is picked for whoever the twin is after at that moment, its target looked at again just before
    //     (TwinsAI). Turned to someone else meanwhile, it has dropped the hold and taken a new turn, to them; after no
    //     one, or its staff drawn by another player whose draw hasn't reached this game yet (CallerBoss.RPC_Punish),
    //     it has dropped it too
    //   - those rules give way after TauntPatience without a taunt, so that nothing they didn't foresee can keep them
    //     quiet
    //   - taunts give way to every other line (the user, 2026-11-16): none while a line plays or waits, on the game that
    //     decides (its turn isn't used up: the next swap will do) and on each that hears it (dropped)
    //   - their first is the Stormcaller's, as the intro ends (Opening), to whoever it's after by then. The turns the
    //     twins take as they're summoned give way to the intro, and until this nothing was said before the first swap;
    //     the user, 2026-11-16: "we can have the first taunt queued after the intro, let's say the Stormcaller boss
    //     taunts first". Decided once the intro is over rather than queued behind it as it starts: by then every
    //     player's staff is known to the game that runs the twins (in the first half second it may be after the wrong
    //     one), and no game has to let a taunt wait. If it isn't after a player it would taunt, the first swap's is
    //     the first, as before
    // - blue-impatient, red-impatient: the Stormcaller, the Firecaller punishing a player's swap to its staff still
    //   charged (CallerBoss.RPC_Punish: smitten at once, no rune, and rushed at; the user, 2026-11-16: "when the
    //   Stormcaller rushes to a player who swapped their staff without waiting for the debuff to fall"). Said with the
    //   smite, whether or not the rush itself happens (the twin already near, or busy with its second phase). A swap is
    //   also a turn to that player, and the taunt it may bring gives way to this. At first at most every 10 s for each
    //   twin; then the user, 2026-11-16, chose FileCooldown for every line instead
    // - blue-kill, red-kill: a player dying in the fight, whatever killed them but the judgment (the user, 2026-11-16:
    //   "When a player dies, play blue-kill.ogg or red-kill.ogg, cycling through them on every kill"). The dying player's
    //   game tells the game that runs the nearest twin (KillRpc), which picks the next one from the count the pair keeps
    //   (the higher of theirs: either may be told) and says it; the first death of a fight gets blue-kill
    // - blue-judgement: a player judged (TwinsJudgment: anything but the two staves still held as the warning ends; the
    //   user, 2026-11-16: "for when a player is judged and killed for wielding the wrong weapon/tool [...] It should gain
    //   priority over any other voice line if judgement happens"). Said by the judged player's game as the sentence
    //   falls (the beams kill them in 2 s). It comes before every other line (Hear): it waits for the line playing
    //   (at first it cut it short; the user, 2026-11-16: "It's ok if we wait for previous sounds to finish (not the
    //   music, of course)"), and goes ahead of those waiting, which follow it if MaxWait allows; behind a line that came
    //   first before it (a raised twin's), if one waits still: the first of those to come is the first said. And the judged player's
    //   death has no kill line (PlayerDeath), the judgment having spoken of it (the next death gets the one it would have
    //   had)
    // Whoever decides says it to every game (a routed RPC), and each game whose player is within HearRange of where it
    // happens plays it, as a voice in their ears (not from a place), through the game's interface sounds (which follow the
    // effects volume). A dead player hears from where they fell until they're back (their player is gone meanwhile). One
    // at a time: a line waits for the one playing, and is dropped if it waited longer than MaxWait.
    // No file twice within FileCooldown (the user, 2026-11-16: "go with 20s per file for all lines"): each game drops a
    // line it took less than that ago, wherever it was decided, so the same recording never comes back to back (one
    // twin's impatient line, kills in a wipe: blue-kill, red-kill, then nothing; two players judged together: one
    // judgment line). The game that decided still counts it as said (a dropped blue-kill still makes the next death's
    // red-kill). A line the DLL hasn't is a line that isn't said.
    // both-defeat drops what waits as it comes (Hear; Claude's finding, 2026-11-16, fixed at the user's word): the
    // second twin killed a moment after the first, its twin's busy line (4 to 5 s since that day's recordings) and its
    // own second phase's were still to be said, so the twins' last words came up to 8 s late, after a dead twin's
    // line, or with a kill line waiting too, past MaxWait and not at all. The line playing is left to end
    internal static class TwinsVoices
    {
        internal const string Start = "both-intro";
        internal const string End = "both-defeat";
        internal const string BlueStasis = "blue-stasis";
        internal const string RedStasis = "red-stasis";
        internal const string BlueBusy = "blue-busy";
        internal const string RedBusy = "red-busy";
        internal const string BlueP2 = "blue-p2";
        internal const string RedP2 = "red-p2";
        private const string BlueImpatient = "blue-impatient";
        private const string RedImpatient = "red-impatient";
        internal const string Judgment = "blue-judgement";

        // Each twin's taunts for each name: <pairing>1, <pairing>2... in the DLL, said in order, round and round (Lines)
        private static readonly string[] Pairings = { "blue-dodo", "blue-dovah", "red-dodo", "red-dovah" };
        // A player's death in the fight, said in this order, round and round
        private static readonly string[] Kills = { "blue-kill", "red-kill" };

        private const string VoiceRpc = "NhxTwinsVoice";
        // A line that goes ahead of those waiting (Say's `first`)
        private const string FirstVoiceRpc = "NhxTwinsVoiceFirst";
        private const string KillRpc = "NhxTwinsKill";
        // As the fight's messages (TwinsBond.TellRange): anyone in the ring hears them
        private const float HearRange = TwinsBond.TellRange;
        private const float MaxWait = 10f;
        private const float Volume = 1f;
        private const float StasisCooldown = 10f;
        // The names they answer to, and how often (the user's: "alternating between them with a 30s cooldown"; 2026-11-16:
        // "Reduce the cooldown to 20s")
        private const string DodoPrefix = "dodo";
        private const string DovahPrefix = "dovah";
        private const float TauntCooldown = 20f;
        // Without a taunt for this long, the next turn to a name speaks, whatever the last two were
        private const float TauntPatience = 60f;
        // A turn that comes this close to the cooldown's end is held until it (seconds)
        private const float TauntHold = 6f;
        // Each game drops a taunt heard this soon after the last it heard: two twins run by two games could decide at the
        // same moment, before either sees the other's (their ZDOs). Well under TauntCooldown, so that a taunt said a full
        // cooldown after the last isn't taken for one (each game's clock and the network's delay differ a little)
        private const float TauntGuard = 15f;
        // Each line, at most this often on a game
        private const float FileCooldown = 20f;
        // The twins' first taunt comes this long after their intro's end (seconds: every game is done with the intro by
        // then, each having started it a little apart), or within this long after that if a line is in its way
        private const float OpeningGap = 0.3f;
        private const float OpeningWindow = 5f;

        private static readonly int s_tauntKey = "nhx_twin_taunt_at".GetStableHashCode();
        // A twin's last taunt (Pairing's code), and the pair's one before it: the twin that spoke last has the pair's
        // last two
        private static readonly int s_lastKey = "nhx_twin_taunt_last".GetStableHashCode();
        private static readonly int s_beforeKey = "nhx_twin_taunt_before".GetStableHashCode();
        // How many taunts a twin has said to each name: which of its lines is next
        private static readonly int s_dodoSaidKey = "nhx_twin_taunts_dodo".GetStableHashCode();
        private static readonly int s_dovahSaidKey = "nhx_twin_taunts_dovah".GetStableHashCode();
        // How many players' deaths a twin has spoken of: which kill line is next (the higher of the pair's)
        private static readonly int s_killsKey = "nhx_twins_kills".GetStableHashCode();
        private static readonly Dictionary<string, AudioClip> s_clips = new Dictionary<string, AudioClip>();
        // Each pairing's lines in the DLL (Lines), found once
        private static readonly Dictionary<string, List<string>> s_lines = new Dictionary<string, List<string>>();
        // The lines waiting, in the order they'll be said: those that come first at the front (Hear)
        private static readonly List<Waiting> s_queue = new List<Waiting>();
        // When this game last took each line (Time.time): FileCooldown
        private static readonly Dictionary<string, float> s_lastTaken = new Dictionary<string, float>();
        private static AudioSource s_source;
        private static float s_lastStasis = -StasisCooldown;
        private static string s_lastStasisLine;
        private static float s_lastTaunt = -TauntGuard;
        // Where the local player is, or was last (Update): a dead player has none until they're back
        private static Vector3 s_ears;
        private static bool s_hasEars;

        private static long Now => ZNet.instance ? ZNet.instance.GetTime().Ticks : 0L;

        // A line waiting its turn: since when (Time.time), and whether it goes ahead of the others
        private struct Waiting
        {
            public string m_line;
            public float m_since;
            public bool m_first;
        }

        // ---- Saying ----

        // `line` for every player near `point`, `why` for the log; `first`: after the line playing, ahead of those waiting
        internal static void Say(string line, Vector3 point, string why, bool first = false)
        {
            TwinsLog.Event($"Says {line}{(first ? " (first)" : "")}: {why}.");
            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, first ? FirstVoiceRpc : VoiceRpc, line, point);
            }
        }

        // The twins hold `count` more creatures in their arena (on the game that speaks for it: TwinsStasis)
        internal static void Stasis(Vector3 point, int count)
        {
            if (Time.time - s_lastStasis < StasisCooldown)
            {
                TwinsLog.Event($"No stasis line for {count} more held: the last was {Time.time - s_lastStasis:0.#} s ago (every {StasisCooldown:0} s at most).");
                return;
            }
            s_lastStasis = Time.time;
            s_lastStasisLine = s_lastStasisLine == BlueStasis ? RedStasis
                : s_lastStasisLine == RedStasis ? BlueStasis
                : Random.value < 0.5f ? BlueStasis : RedStasis;
            Say(s_lastStasisLine, point, $"{count} more creature(s) held");
        }

        // A twin punishes `player`'s swap to its staff still charged (on its owner's game: CallerBoss.RPC_Punish)
        internal static void Impatient(CallerBoss boss, Player player)
        {
            if (!boss.m_nview || !boss.m_nview.IsValid() || !player)
            {
                return;
            }
            Say(boss.Element == Element.Storm ? BlueImpatient : RedImpatient, player.transform.position,
                $"{TwinsLog.Name(boss)} punishes {TwinsLog.Name(player)}'s swap to a staff still charged");
        }

        // A twin turns to `player` (on its owner's game: TwinsAI)
        internal static void Targeted(CallerBoss boss, Player player)
        {
            boss.m_tauntHeld = 0f;
            Taunt(boss, player, false, false);
        }

        // A twin looks at its role and turns to no one new (on its owner's game: TwinsAI): the taunt it holds for the
        // cooldown's end, while it's still after that player; or the pair's first (Opening)
        internal static void Pending(CallerBoss boss)
        {
            if (boss.m_tauntHeld <= 0f)
            {
                Opening(boss);
                return;
            }
            if (Time.time > boss.m_tauntHeld || !(boss.m_target is Player player))
            {
                boss.m_tauntHeld = 0f;
                TwinsLog.Event($"{TwinsLog.Name(boss)}'s held taunt goes unsaid: {(boss.m_target is Player ? $"the cooldown didn't end within {TauntHold:0} s" : "it's after no player now")}.");
                return;
            }
            Taunt(boss, player, false, true);
        }

        // The twins' first taunt: the Stormcaller's, to the player it's after once their intro is over. Only twins just
        // summoned, who haven't taunted yet: a twin raised later, or one whose first taunt a swap brought, has none
        private static void Opening(CallerBoss boss)
        {
            if (boss.Element != Element.Storm || !(boss.m_target is Player player) || !boss.m_nview || !boss.m_nview.IsValid())
            {
                return;
            }
            float since = TwinsBond.Since(boss);
            float after = (s_clips.TryGetValue(Start, out AudioClip intro) && intro ? intro.length : 0f) + OpeningGap;
            if (since < after || since > after + OpeningWindow)
            {
                return;
            }
            ZDO twin = TwinsBond.TwinZDO(boss);
            if (boss.m_nview.GetZDO().GetLong(s_tauntKey) != 0L || (twin != null && twin.GetLong(s_tauntKey) != 0L))
            {
                return;
            }
            Taunt(boss, player, true, false);
        }

        // A twin's taunt to `player`, if it may speak now: as it turns to them, as the pair's first (`opening`), or as the
        // cooldown that refused its turn ends (`held`)
        private static void Taunt(CallerBoss boss, Player player, bool opening, bool held)
        {
            string name = player ? player.GetPlayerName() : null;
            string prefix = Prefix(name);
            if (!boss.m_nview || !boss.m_nview.IsValid() || prefix == null)
            {
                boss.m_tauntHeld = 0f;
                return;
            }
            string turn = opening ? $"{TwinsLog.Name(boss)} opens on {name}, the intro over"
                : held ? $"{TwinsLog.Name(boss)} is still after {name}, the cooldown over"
                : $"{TwinsLog.Name(boss)} turns to {name}";
            List<string> lines = Lines(Pairing(boss.Element, prefix));
            if (lines.Count == 0)
            {
                TwinsLog.Event($"{turn}: no {Pairing(boss.Element, prefix)} lines in the DLL.");
                return;
            }
            ZDO zdo = boss.m_nview.GetZDO();
            string otherPrefix = prefix == DodoPrefix ? DovahPrefix : DodoPrefix;
            int said = zdo.GetInt(SaidKey(prefix));
            string line = lines[said % lines.Count];
            ZDO twin = TwinsBond.TwinZDO(boss);
            long mine = zdo.GetLong(s_tauntKey);
            long theirs = twin != null ? twin.GetLong(s_tauntKey) : 0L;
            long last = Math.Max(mine, theirs);
            float since = last != 0L ? (float)TimeSpan.FromTicks(Now - last).TotalSeconds : float.PositiveInfinity;
            if (since < TauntCooldown)
            {
                // Still holding its turn: nothing new to say of it
                if (held)
                {
                    return;
                }
                // The cooldown nearly over: its turn is held until then (Pending)
                if (TauntCooldown - since <= TauntHold)
                {
                    boss.m_tauntHeld = Time.time + TauntHold;
                    TwinsLog.Event($"{turn}, {line} held: the last taunt was {since:0} s ago (every {TauntCooldown:0} s at most), so in {TauntCooldown - since:0.#} s if it's still after them.");
                    return;
                }
                TwinsLog.Event($"{turn}, no {line}: the last taunt was {since:0} s ago (every {TauntCooldown:0} s at most).");
                return;
            }
            boss.m_tauntHeld = 0f;
            if (Busy())
            {
                TwinsLog.Event($"{turn}, no {line}: it gives way to the line {(s_source && s_source.isPlaying ? "playing" : "waiting")}.");
                return;
            }
            // The pair's last two taunts, from the twin that spoke last
            ZDO latest = mine >= theirs ? zdo : twin;
            int lastPairing = last != 0L ? latest.GetInt(s_lastKey) : 0;
            int before = last != 0L ? latest.GetInt(s_beforeKey) : 0;
            int pairing = Code(boss.Element, prefix);
            if (since < TauntPatience)
            {
                // Never three in a row: by a twin while its twin lives, to a name while someone answering to the other is
                // in the fight; nor the last one's pairing again, while there's another to be had
                bool otherName = Listens(boss, otherPrefix);
                if (pairing == lastPairing && (twin != null || otherName))
                {
                    TwinsLog.Event($"{turn}, no {line}: the last taunt was {Pairing(boss.Element, prefix)} too.");
                    return;
                }
                if (twin != null && By(lastPairing) == boss.Element && By(before) == boss.Element)
                {
                    TwinsLog.Event($"{turn}, no {line}: the last two taunts were its own, the next is the {(boss.Element == Element.Storm ? "Firecaller" : "Stormcaller")}'s.");
                    return;
                }
                if (otherName && To(lastPairing) == prefix && To(before) == prefix)
                {
                    TwinsLog.Event($"{turn}, no {line}: the last two taunts were to {prefix}*, the next is to {otherPrefix}*.");
                    return;
                }
            }
            zdo.Set(s_tauntKey, Now);
            zdo.Set(SaidKey(prefix), said + 1);
            zdo.Set(s_lastKey, pairing);
            zdo.Set(s_beforeKey, lastPairing);
            Say(line, boss.transform.position, last != 0L && since >= TauntPatience ? $"{turn}, the first taunt in {since:0} s" : turn);
        }

        private static int SaidKey(string prefix)
        {
            return prefix == DodoPrefix ? s_dodoSaidKey : s_dovahSaidKey;
        }

        // A taunt's pairing as a number for the twins' ZDOs (s_lastKey, s_beforeKey): 0 is none, and By and To read it
        private static int Code(Element element, string prefix)
        {
            return (int)element * 2 + (prefix == DovahPrefix ? 1 : 0);
        }

        private static Element By(int code)
        {
            return code == 0 ? Element.None : (Element)(code / 2);
        }

        private static string To(int code)
        {
            return code == 0 ? null : code % 2 == 1 ? DovahPrefix : DodoPrefix;
        }

        // A line playing or waiting on this game
        private static bool Busy()
        {
            return s_queue.Count > 0 || (s_source && s_source.isPlaying);
        }

        // The name the twins taunt `name` by (DodoPrefix or DovahPrefix), or null
        private static string Prefix(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            return name.StartsWith(DodoPrefix, StringComparison.OrdinalIgnoreCase) ? DodoPrefix
                : name.StartsWith(DovahPrefix, StringComparison.OrdinalIgnoreCase) ? DovahPrefix
                : null;
        }

        // A twin's taunts to a name: blue-dodo, blue-dovah, red-dodo, red-dovah (Pairings)
        private static string Pairing(Element element, string prefix)
        {
            return (element == Element.Storm ? "blue-" : "red-") + prefix;
        }

        // A pairing's lines in the DLL, in order: <pairing>1, <pairing>2... as far as the numbers go
        private static List<string> Lines(string pairing)
        {
            if (s_lines.TryGetValue(pairing, out List<string> lines))
            {
                return lines;
            }
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in EmbeddedAudio.Names(EmbeddedAudio.Sounds))
            {
                names.Add(Path.GetFileNameWithoutExtension(file));
            }
            lines = new List<string>();
            while (names.Contains(pairing + (lines.Count + 1)))
            {
                lines.Add(pairing + (lines.Count + 1));
            }
            s_lines[pairing] = lines;
            return lines;
        }

        private static bool IsTaunt(string line)
        {
            foreach (string pairing in Pairings)
            {
                if (Lines(pairing).Contains(line))
                {
                    return true;
                }
            }
            return false;
        }

        // A living player in the fight who answers to `prefix`
        private static bool Listens(CallerBoss boss, string prefix)
        {
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player && !player.IsDead() && TwinsRing.Inside(boss, player.transform.position) && Prefix(player.GetPlayerName()) == prefix)
                {
                    return true;
                }
            }
            return false;
        }

        // The local player dies (Player.OnDeath, on their own game; it doesn't call Character's): in a fight, the game that
        // runs the nearest twin speaks of it, unless they were judged (the judgment's line spoke of it)
        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class PlayerDeath
        {
            private static void Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || !TwinsRing.Fight(__instance.transform.position, out CallerBoss twin))
                {
                    return;
                }
                if (TwinsJudgment.Judged)
                {
                    TwinsLog.Event($"No kill line for {TwinsLog.Name(__instance)}: judged, {Judgment} spoke of it.");
                    return;
                }
                twin.m_nview.InvokeRPC(ZNetView.Everybody, KillRpc, __instance.GetPlayerName(), __instance.transform.position);
            }
        }

        // On each twin's every game (CallerBoss.Awake)
        internal static void Register(CallerBoss boss)
        {
            boss.m_nview.Register<string, Vector3>(KillRpc, (sender, name, point) => Killed(boss, name, point));
        }

        // A player named `name` died at `point` in this twin's fight: on the game that runs it, the next kill line
        private static void Killed(CallerBoss boss, string name, Vector3 point)
        {
            if (!boss.m_nview || !boss.m_nview.IsValid() || !boss.m_nview.IsOwner())
            {
                return;
            }
            ZDO zdo = boss.m_nview.GetZDO();
            ZDO twin = TwinsBond.TwinZDO(boss);
            int kills = Math.Max(zdo.GetInt(s_killsKey), twin != null ? twin.GetInt(s_killsKey) : 0);
            zdo.Set(s_killsKey, kills + 1);
            Say(Kills[kills % Kills.Length], point, $"{name} died (death {kills + 1} of the fight)");
        }

        [HarmonyPatch(typeof(Game), "Start")]
        private static class RegisterVoice
        {
            private static void Postfix()
            {
                ZRoutedRpc.instance?.Register<string, Vector3>(VoiceRpc, (sender, line, point) => Hear(line, point, false));
                ZRoutedRpc.instance?.Register<string, Vector3>(FirstVoiceRpc, (sender, line, point) => Hear(line, point, true));
            }
        }

        // ---- Hearing ----

        // `line` said at `point` (the judgment's always first)
        private static void Hear(string line, Vector3 point, bool first)
        {
            first |= line == Judgment;
            Player player = Player.m_localPlayer;
            if (player)
            {
                s_ears = player.transform.position;
                s_hasEars = true;
            }
            else if (!s_hasEars)
            {
                return;
            }
            float distance = Vector3.Distance(s_ears, point);
            if (distance > HearRange)
            {
                TwinsLog.Event($"Not hearing {line}: {distance:0} m away (within {HearRange:0} m).");
                return;
            }
            if (!s_clips.ContainsKey(line))
            {
                TwinsLog.Event($"Not hearing {line}: no such line in the DLL.");
                return;
            }
            if (s_lastTaken.TryGetValue(line, out float taken) && Time.time - taken < FileCooldown)
            {
                TwinsLog.Event($"Not hearing {line}: it came {Time.time - taken:0.#} s ago (every {FileCooldown:0} s at most).");
                return;
            }
            // Never both taunts: one that comes on the other's heels is dropped; and a taunt gives way to any other line
            if (IsTaunt(line))
            {
                if (Time.time - s_lastTaunt < TauntGuard)
                {
                    TwinsLog.Event($"Not hearing {line}: another taunt came {Time.time - s_lastTaunt:0.#} s ago.");
                    return;
                }
                if (Busy())
                {
                    TwinsLog.Event($"Not hearing {line}: it gives way to the line {(s_source && s_source.isPlaying ? "playing" : "waiting")}.");
                    return;
                }
                s_lastTaunt = Time.time;
            }
            // Both beaten: what still waited to be said (a fallen twin's line, its twin's second phase, a kill) isn't said
            // after them, and their last words come as soon as the line playing is done
            if (line == End && s_queue.Count > 0)
            {
                List<string> dropped = new List<string>();
                foreach (Waiting waits in s_queue)
                {
                    dropped.Add(waits.m_line);
                }
                TwinsLog.Event($"Heard {line}: the twins are beaten, so not saying {string.Join(", ", dropped)}.");
                s_queue.Clear();
            }
            bool waiting = Busy();
            s_lastTaken[line] = Time.time;
            Waiting heard = new Waiting { m_line = line, m_since = Time.time, m_first = first };
            // Judged, or a twin raised: it comes next, once the line playing is done, ahead of those waiting (behind one that
            // came first before it)
            int at = s_queue.Count;
            if (first)
            {
                at = 0;
                while (at < s_queue.Count && s_queue[at].m_first)
                {
                    at++;
                }
            }
            if (at < s_queue.Count)
            {
                List<string> behind = new List<string>();
                for (int i = at; i < s_queue.Count; i++)
                {
                    behind.Add(s_queue[i].m_line);
                }
                s_queue.Insert(at, heard);
                TwinsLog.Event($"Heard {line}: {(line == Judgment ? "a player is judged" : "a twin is raised")}, so it goes ahead of {string.Join(", ", behind)}.");
                return;
            }
            s_queue.Add(heard);
            TwinsLog.Event(waiting ? $"Heard {line}, waiting its turn ({s_queue.Count} in line)." : $"Heard {line}.");
        }

        // Every frame (Plugin.Update): the next line once the last is done
        internal static void Update()
        {
            if (Player.m_localPlayer)
            {
                s_ears = Player.m_localPlayer.transform.position;
                s_hasEars = true;
            }
            // Out of the world: no ears left behind in it
            else if (!ZNet.instance)
            {
                s_hasEars = false;
            }
            if (s_queue.Count == 0 || (s_source && s_source.isPlaying))
            {
                return;
            }
            Waiting next = s_queue[0];
            s_queue.RemoveAt(0);
            float waited = Time.time - next.m_since;
            if (waited > MaxWait || !s_clips.TryGetValue(next.m_line, out AudioClip clip))
            {
                TwinsLog.Event($"Dropped {next.m_line}: it waited {waited:0.#} s (at most {MaxWait:0} s).");
                return;
            }
            if (!s_source)
            {
                GameObject go = new GameObject("NhxTwinsVoices");
                UnityEngine.Object.DontDestroyOnLoad(go);
                s_source = go.AddComponent<AudioSource>();
                s_source.playOnAwake = false;
                s_source.spatialBlend = 0f;
                s_source.priority = 0;
            }
            if (AudioMan.instance)
            {
                s_source.outputAudioMixerGroup = AudioMan.instance.m_guiMixer;
            }
            s_source.clip = clip;
            s_source.volume = Volume;
            s_source.Play();
            TwinsLog.Event(waited > 0.5f ? $"Playing {next.m_line} ({clip.length:0.#} s), after waiting {waited:0.#} s." : $"Playing {next.m_line} ({clip.length:0.#} s).");
        }

        // ---- Loading ----

        // Once, as the game starts (Plugin.Awake); not on a dedicated server
        internal static IEnumerator Load()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                yield break;
            }
            List<string> files = EmbeddedAudio.Files(EmbeddedAudio.Sounds);
            if (files.Count == 0)
            {
                Plugin.Log.LogInfo("The twins' voices: none in the DLL, so they're silent.");
                yield break;
            }
            foreach (string file in files)
            {
                AudioType type = TwinsMusic.Type(file);
                using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip("file:///" + file.Replace('\\', '/'), type))
                {
                    yield return request.SendWebRequest();
                    AudioClip clip = request.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(request) : null;
                    if (clip)
                    {
                        clip.name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                        s_clips[clip.name] = clip;
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"Couldn't load the twins' voice {Path.GetFileName(file)}: {request.error}");
                    }
                }
            }
            Plugin.Log.LogInfo($"The twins' voices: {s_clips.Count} ({string.Join(", ", s_clips.Keys)}).");
        }
    }
}
