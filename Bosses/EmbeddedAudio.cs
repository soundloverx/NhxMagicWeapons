using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The twins' music and voices, inside the DLL (the user, 2026-11-15: "yes, embed them in the DLL", so that the mod is
    // one file to copy onto the server and every player's game, with no Music or Sounds folder to forget): the build puts
    // each file of the project's Music\ and Sounds\ in as a resource named "music/<file>" or "sounds/<file>" (the csproj).
    // Unity decodes sound files only from an address (UnityWebRequestMultimedia: a file:// one here), not from bytes in
    // memory, so each kind is written out to the profile's BepInEx\cache\NhxMagicWeapons\<kind>\ the first time it's
    // asked for in a run, and read from there (TwinsMusic, TwinsVoices). A file already there is written again only when
    // it differs (a new build), and one there that the DLL no longer has is removed
    internal static class EmbeddedAudio
    {
        internal const string Music = "music";
        internal const string Sounds = "sounds";

        // Each kind's folder once written out this run (null: it couldn't be)
        private static readonly Dictionary<string, string> s_folders = new Dictionary<string, string>();

        // The file names of a kind's sounds in the DLL (.ogg, .wav or .mp3), in order
        internal static List<string> Names(string kind)
        {
            string prefix = kind + "/";
            List<string> names = new List<string>();
            foreach (string resource in typeof(Plugin).Assembly.GetManifestResourceNames())
            {
                if (resource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && TwinsMusic.Type(resource) != AudioType.UNKNOWN)
                {
                    names.Add(resource.Substring(prefix.Length));
                }
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        // The paths of a kind's files, written out (once a run); none if they can't be
        internal static List<string> Files(string kind)
        {
            List<string> files = new List<string>();
            string folder = Folder(kind);
            if (folder == null)
            {
                return files;
            }
            foreach (string name in Names(kind))
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path))
                {
                    files.Add(path);
                }
            }
            return files;
        }

        private static string Folder(string kind)
        {
            if (s_folders.TryGetValue(kind, out string written))
            {
                return written;
            }
            s_folders[kind] = null;
            string folder = Path.Combine(Path.Combine(Paths.CachePath, Plugin.PluginGuid), kind);
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning($"Couldn't make {folder} for the twins' {kind}: {exception.Message}");
                return null;
            }

            List<string> names = Names(kind);
            foreach (string name in names)
            {
                string path = Path.Combine(folder, name);
                try
                {
                    byte[] bytes = Read(kind + "/" + name);
                    if (bytes != null && !Same(path, bytes))
                    {
                        File.WriteAllBytes(path, bytes);
                    }
                }
                // Two games started at once from the same profile (two test characters) may both write it: the other's
                // copy is the same
                catch (Exception exception)
                {
                    Plugin.Log.LogWarning($"Couldn't write the twins' {kind} file {name}: {exception.Message}");
                }
            }
            // What an earlier build had and this one hasn't (a track replaced) isn't played any more
            try
            {
                foreach (string file in Directory.GetFiles(folder))
                {
                    if (!names.Exists(name => string.Equals(name, Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)))
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning($"Couldn't tidy {folder}: {exception.Message}");
            }
            s_folders[kind] = folder;
            return folder;
        }

        private static byte[] Read(string resource)
        {
            using (Stream stream = typeof(Plugin).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    return null;
                }
                using (MemoryStream memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }

        // Whether the file at `path` holds exactly `bytes`
        private static bool Same(string path, byte[] bytes)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
            {
                return false;
            }
            byte[] there = File.ReadAllBytes(path);
            for (int i = 0; i < there.Length; i++)
            {
                if (there[i] != bytes[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
