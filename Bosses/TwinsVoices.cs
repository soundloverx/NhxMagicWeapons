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
    // - bluered-start: both of them, as they're summoned (TwinsBond.Summon)
    // - blue-stasis or red-stasis: as they hold the creatures in their arena (TwinsStasis), once per hold, at most every
    //   StasisCooldown. At first either at random, every 5 s at most; the user, 2026-11-15, after four blue-stasis in a
    //   row: "We could also try to alternate the blue-stasis.ogg and red-stasis.ogg as well so we don't get the same voice
    //   over and over, but with a cooldown of 10 seconds". So the first of a game's is either, then they take turns (on
    //   the game that speaks for the arena: another game taking over may repeat one once)
    // - bluered-end: both beaten (TwinsBond.Won)
    // - blue-busy, red-busy: the Stormcaller, the Firecaller fallen, as the rune that will raise it appears (TwinsBond.Fell)
    // - blue-p2, red-p2: the Stormcaller, the Firecaller entering its second phase (TwinsPhase.Enrage), a raised twin too
    //   (it comes back in it). The user, 2026-11-15: "They shouldn't overlap, so maybe queue the 2 sounds if they happen
    //   at the same time": as every line, the second waits for the first
    // - blue-dodo: the Stormcaller turning to a player whose name starts with "dodo", red-dovahkiin: the Firecaller
    //   turning to one whose name starts with "dovah" (any case; the character's name). The user, 2026-11-15, having
    //   heard blue-dodo once in a solo fight ("Perhaps the cooldown in combination with the 50% chance is a bit
    //   excessive"): every time, but one taunt at most every TauntCooldown for the pair, and they take turns: "the
    //   first line is blue-dodo, then fighting, 30 seconds pass, then the bosses will swap targets again, the next line
    //   would be red-dovahkiin if "dovah*" is targeted". So after one twin's taunt, the next is the other's while
    //   there's a player in the fight it would say it to; with no such player, the same twin may taunt again
    //   (Targeted). Turning to a player is a new target or starting to hunt them (TwinsAI): alone, a player only
    //   changes the twins' roles, at each swap
    // Whoever decides says it to every game (a routed RPC), and each game whose player is within HearRange of where it
    // happens plays it, as a voice in their ears (not from a place), through the game's interface sounds (which follow the
    // effects volume). One at a time: a line waits for the one playing, and is dropped if it waited longer than MaxWait.
    // A line the DLL hasn't is a line that isn't said
    internal static class TwinsVoices
    {
        internal const string Start = "bluered-start";
        internal const string End = "bluered-end";
        internal const string BlueStasis = "blue-stasis";
        internal const string RedStasis = "red-stasis";
        internal const string BlueBusy = "blue-busy";
        internal const string RedBusy = "red-busy";
        internal const string BlueDodo = "blue-dodo";
        internal const string RedDovahkiin = "red-dovahkiin";
        internal const string BlueP2 = "blue-p2";
        internal const string RedP2 = "red-p2";

        private const string VoiceRpc = "NhxTwinsVoice";
        // As the fight's messages (TwinsBond.TellRange): anyone in the ring hears them
        private const float HearRange = TwinsBond.TellRange;
        private const float MaxWait = 10f;
        private const float Volume = 1f;
        private const float StasisCooldown = 10f;
        // The names they answer to, and how often (the user's: "alternating between them with a 30s cooldown")
        private const string DodoPrefix = "dodo";
        private const string DovahPrefix = "dovah";
        private const float TauntCooldown = 30f;
        // Each game drops a taunt heard this soon after the last it heard: two twins run by two games could decide at the
        // same moment, before either sees the other's (their ZDOs)
        private const float TauntGuard = 20f;

        private static readonly int s_tauntKey = "nhx_twin_taunt_at".GetStableHashCode();
        private static readonly Dictionary<string, AudioClip> s_clips = new Dictionary<string, AudioClip>();
        private static readonly Queue<KeyValuePair<string, float>> s_queue = new Queue<KeyValuePair<string, float>>();
        private static AudioSource s_source;
        private static float s_lastStasis = -StasisCooldown;
        private static string s_lastStasisLine;
        private static float s_lastTaunt = -TauntGuard;

        private static long Now => ZNet.instance ? ZNet.instance.GetTime().Ticks : 0L;

        // ---- Saying ----

        // `line` for every player near `point`, `why` for the log
        internal static void Say(string line, Vector3 point, string why)
        {
            TwinsLog.Event($"Says {line}: {why}.");
            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, VoiceRpc, line, point);
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

        // A twin turns to `player` (on its owner's game: TwinsAI)
        internal static void Targeted(CallerBoss boss, Player player)
        {
            string name = player ? player.GetPlayerName() : null;
            if (!boss.m_nview || !boss.m_nview.IsValid() || !Answers(name, boss.Element))
            {
                return;
            }
            string line = boss.Element == Element.Storm ? BlueDodo : RedDovahkiin;
            string turn = $"{TwinsLog.Name(boss)} turns to {name}";
            ZDO zdo = boss.m_nview.GetZDO();
            ZDO twin = TwinsBond.TwinZDO(boss);
            long mine = zdo.GetLong(s_tauntKey);
            long theirs = twin != null ? twin.GetLong(s_tauntKey) : 0L;
            long last = Math.Max(mine, theirs);
            float since = (float)TimeSpan.FromTicks(Now - last).TotalSeconds;
            if (last != 0L && since < TauntCooldown)
            {
                TwinsLog.Event($"{turn}, no {line}: the last taunt was {since:0} s ago (every {TauntCooldown:0} s at most).");
                return;
            }
            // Taking turns: the one that spoke last waits for the other, while it has someone to say its line to
            if (mine > theirs && twin != null && OtherListens(boss))
            {
                TwinsLog.Event($"{turn}, no {line}: it's the {(boss.Element == Element.Storm ? "Firecaller" : "Stormcaller")}'s turn.");
                return;
            }
            zdo.Set(s_tauntKey, Now);
            Say(line, boss.transform.position, turn);
        }

        private static bool Answers(string name, Element element)
        {
            return !string.IsNullOrEmpty(name) && name.StartsWith(element == Element.Storm ? DodoPrefix : DovahPrefix, StringComparison.OrdinalIgnoreCase);
        }

        // A living player in the fight whom the other twin would taunt
        private static bool OtherListens(CallerBoss boss)
        {
            Element other = boss.Element == Element.Storm ? Element.Fire : Element.Storm;
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player && !player.IsDead() && TwinsAI.InReach(boss, player) && Answers(player.GetPlayerName(), other))
                {
                    return true;
                }
            }
            return false;
        }

        [HarmonyPatch(typeof(Game), "Start")]
        private static class Register
        {
            private static void Postfix()
            {
                ZRoutedRpc.instance?.Register<string, Vector3>(VoiceRpc, RPC_Voice);
            }
        }

        // ---- Hearing ----

        private static void RPC_Voice(long sender, string line, Vector3 point)
        {
            Player player = Player.m_localPlayer;
            if (!player)
            {
                return;
            }
            float distance = Vector3.Distance(player.transform.position, point);
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
            // Never both taunts: one that comes on the other's heels is dropped
            if (line == BlueDodo || line == RedDovahkiin)
            {
                if (Time.time - s_lastTaunt < TauntGuard)
                {
                    TwinsLog.Event($"Not hearing {line}: another taunt came {Time.time - s_lastTaunt:0.#} s ago.");
                    return;
                }
                s_lastTaunt = Time.time;
            }
            bool waiting = s_queue.Count > 0 || (s_source && s_source.isPlaying);
            s_queue.Enqueue(new KeyValuePair<string, float>(line, Time.time));
            TwinsLog.Event(waiting ? $"Heard {line}, waiting its turn ({s_queue.Count} in line)." : $"Heard {line}.");
        }

        // Every frame (Plugin.Update): the next line once the last is done
        internal static void Update()
        {
            if (s_queue.Count == 0 || (s_source && s_source.isPlaying))
            {
                return;
            }
            KeyValuePair<string, float> next = s_queue.Dequeue();
            float waited = Time.time - next.Value;
            if (waited > MaxWait || !s_clips.TryGetValue(next.Key, out AudioClip clip))
            {
                TwinsLog.Event($"Dropped {next.Key}: it waited {waited:0.#} s (at most {MaxWait:0} s).");
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
            TwinsLog.Event(waited > 0.5f ? $"Playing {next.Key} ({clip.length:0.#} s), after waiting {waited:0.#} s." : $"Playing {next.Key} ({clip.length:0.#} s).");
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
