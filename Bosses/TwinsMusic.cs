using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Networking;

namespace NhxMagicWeapons.Bosses
{
    // The twins' music (the user, 2026-11-14: "Can we also include our own music?"). The game plays a boss's music through
    // an event: while a boss's bar shows, every game forces the event its m_bossEvent names (RandEventSystem.GetForcedEvent)
    // and plays that event's m_forceMusic (MusicMan.HandleEventMusic). The twins name EventName, an event of ours (added to
    // each game's RandEventSystem: no spawns, no weather, never random), whose music is ours: the tracks in the DLL (the
    // project's Music\ folder as it was built: .ogg, .wav or .mp3; several: one of them each time; EmbeddedAudio), loaded
    // when the game starts and added to MusicMan as MusicName, looping. Without any, the event plays the music of the
    // Ashlands' own boss (Fader's event's), so the fight isn't silent.
    // At first the tracks weren't embedded but shipped in a Music folder next to the DLL ("minutes of sound would make the
    // DLL huge": the one track is 4.5 MB); the user, 2026-11-15, told what it would cost: "yes, embed them in the DLL"
    internal static class TwinsMusic
    {
        internal const string EventName = "nhx_twins";
        private const string MusicName = "nhx_twins";
        private const string FallbackBoss = "Fader";
        // Its volume (the game's music is about 1), and how long it takes to come in
        private const float Volume = 1f;
        private const float FadeIn = 3f;

        private static string s_fallbackEvent;

        // When the game's prefabs are there (CallerBosses.Add): which event the fallback music is in
        internal static void Prepare()
        {
            GameObject boss = PrefabManager.Instance.GetPrefab(FallbackBoss);
            Character character = boss ? boss.GetComponent<Character>() : null;
            s_fallbackEvent = character ? character.m_bossEvent : null;
        }

        // A sound file's kind by its extension (the voices' too: TwinsVoices)
        internal static AudioType Type(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".ogg":
                    return AudioType.OGGVORBIS;
                case ".wav":
                    return AudioType.WAV;
                case ".mp3":
                    return AudioType.MPEG;
                default:
                    return AudioType.UNKNOWN;
            }
        }

        // The twins' event, in each game's event list
        [HarmonyPatch(typeof(RandEventSystem), "Awake")]
        private static class Event
        {
            private static void Postfix(RandEventSystem __instance)
            {
                if (__instance.m_events.Exists(e => e != null && e.m_name == EventName))
                {
                    return;
                }
                string music = MusicName;
                // Whether the DLL has tracks, without writing them out (a dedicated server plays nothing)
                if (EmbeddedAudio.Names(EmbeddedAudio.Music).Count == 0)
                {
                    RandomEvent fallback = s_fallbackEvent == null ? null : __instance.m_events.Find(e => e != null && e.m_name == s_fallbackEvent);
                    music = fallback != null ? fallback.m_forceMusic : "";
                    // The first test without tracks had no boss music at all (the log, 2026-11-14): said here, so it shows
                    if (string.IsNullOrEmpty(music))
                    {
                        Plugin.Log.LogWarning($"The twins' fight has no music: no tracks in the DLL, and no music in {FallbackBoss}'s event ({s_fallbackEvent ?? "none"}).");
                    }
                }
                Plugin.Log.LogInfo($"The twins' fight plays the music {(string.IsNullOrEmpty(music) ? "of wherever it is" : music)}.");
                __instance.m_events.Add(new RandomEvent
                {
                    m_name = EventName,
                    m_enabled = true,
                    m_random = false,
                    m_duration = 3600f,
                    m_nearBaseOnly = false,
                    m_pauseIfNoPlayerInArea = false,
                    m_forceMusic = music,
                    m_forceEnvironment = "",
                    m_startMessage = "",
                    m_endMessage = "",
                    m_spawn = new List<SpawnSystem.SpawnData>(),
                });
            }
        }

        // Our tracks, loaded once (MusicMan lasts the whole game); not on a dedicated server, which plays nothing (as the
        // voices: TwinsVoices.Load)
        [HarmonyPatch(typeof(MusicMan), "Awake")]
        private static class Load
        {
            private static void Postfix(MusicMan __instance)
            {
                if (MusicMan.instance != __instance || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    return;
                }
                List<string> tracks = EmbeddedAudio.Files(EmbeddedAudio.Music);
                if (tracks.Count > 0)
                {
                    __instance.StartCoroutine(LoadTracks(__instance, tracks));
                }
            }
        }

        // The fight's music loops for as long as the fight goes on (the user, 2026-11-15: "And can we have the music
        // loop?"). It's made to loop, but with the game's "Continuous music" setting off (as the user has it), the game
        // stops any track as it ends (MusicMan.UpdateMusic: "Music stopped after finishing, because continous music is
        // disabled") and won't start the same one again for a while. So while it's the twins' music that plays and the
        // fight still calls for it (the event's music), the game's music update runs as with the setting on; once the
        // fight ends, the game stops it as usual. (At first StopMusic itself was skipped: the loop worked, but the game
        // logged its line every frame of each loop's last 3 s, 256 lines in one test)
        [HarmonyPatch(typeof(MusicMan), "UpdateMusic")]
        private static class KeepPlaying
        {
            // The setting turned on for this update, to turn back off after it (even if it throws: the game saves it)
            private static bool s_turnedOn;

            private static void Prefix(MusicMan __instance)
            {
                MusicMan.NamedMusic current = __instance.m_currentMusic;
                AudioSource source = __instance.m_musicSource;
                if (Settings.ContinousMusic || current == null || current.m_name != MusicName || __instance.m_queuedMusic != null
                    || !source || !source.isPlaying || !RandEventSystem.instance || RandEventSystem.instance.GetMusicOverride() != MusicName)
                {
                    return;
                }
                Settings.ContinousMusic = true;
                s_turnedOn = true;
            }

            private static void Finalizer()
            {
                if (s_turnedOn)
                {
                    s_turnedOn = false;
                    Settings.ContinousMusic = false;
                }
            }
        }

        private static IEnumerator LoadTracks(MusicMan music, List<string> tracks)
        {
            List<AudioClip> clips = new List<AudioClip>();
            foreach (string track in tracks)
            {
                string uri = "file:///" + track.Replace('\\', '/');
                using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, Type(track)))
                {
                    // Kept compressed in memory, decoded as it plays
                    ((DownloadHandlerAudioClip)request.downloadHandler).compressed = true;
                    yield return request.SendWebRequest();
                    AudioClip clip = request.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(request) : null;
                    if (clip)
                    {
                        clip.name = Path.GetFileNameWithoutExtension(track);
                        clips.Add(clip);
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"Couldn't load the twins' music {Path.GetFileName(track)}: {request.error}");
                    }
                }
            }
            if (clips.Count == 0)
            {
                yield break;
            }
            MusicMan.NamedMusic named = new MusicMan.NamedMusic
            {
                m_name = MusicName,
                m_clips = clips.ToArray(),
                m_volume = Volume,
                m_fadeInTime = FadeIn,
                m_alwaysFadeout = true,
                m_loop = true,
                m_resume = false,
                m_enabled = true,
            };
            music.m_music.Add(named);
            music.m_musicHashes[MusicName.GetStableHashCode()] = named;
            Plugin.Log.LogInfo($"The twins' music: {clips.Count} track{(clips.Count == 1 ? "" : "s")}.");
        }
    }

    // Both twins' bars at the top of the screen. The game's interface lines several bosses' bars up side by side, centered
    // (seen in the user's test, 2026-11-14, D:\test.jpg: they sat symmetrically about the middle); at first the
    // Firecaller's was also pushed down here, which only put it off level ("The health bars don't apprear just right"). Now
    // only their order is kept, the Stormcaller's first (on the left), whichever was shown first: the layout follows the
    // bars' order among their parent's children.
    // The game calls ShowHud every frame for every creature in sight, so the twins are found without GetComponent
    // (CallerBoss.Of)
    [HarmonyPatch(typeof(EnemyHud), "ShowHud")]
    internal static class BossBars
    {
        private static void Postfix(EnemyHud __instance, Character c)
        {
            CallerBoss boss = CallerBoss.Of(c);
            if (!boss || boss.Element != Element.Fire || !c.IsBoss() || !__instance.m_huds.TryGetValue(c, out EnemyHud.HudData fire) || !fire.m_gui)
            {
                return;
            }
            foreach (KeyValuePair<Character, EnemyHud.HudData> hud in __instance.m_huds)
            {
                CallerBoss other = CallerBoss.Of(hud.Key);
                if (!other || other.Element != Element.Storm || !hud.Value.m_gui || hud.Value.m_gui.transform.parent != fire.m_gui.transform.parent)
                {
                    continue;
                }
                int storm = hud.Value.m_gui.transform.GetSiblingIndex();
                if (fire.m_gui.transform.GetSiblingIndex() < storm)
                {
                    fire.m_gui.transform.SetSiblingIndex(storm);
                }
                return;
            }
        }
    }
}
