using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System.IO;
using UnityEngine;

namespace NhxMagicWeapons
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid, BepInDependency.DependencyFlags.HardDependency)]
    // Our models and effects are in Magic Supremacy's asset bundle, and Unity can't load the same bundle twice: with
    // both installed, whichever loads it second would have none of its items
    [BepInIncompatibility("Dreanegade.Magic_Supremacy")]
    // Every game needs the items, and the bolts and strikes the others' games create for everyone to see
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "NhxMagicWeapons";
        private const string PluginName = "NhxMagicWeapons";
        private const string PluginVersion = "1.0.0";

        // What the mod uses of Magic Supremacy 3.1.5's asset bundle (Tools\slim_bundle.py), in the plugin's folder
        private const string BundleFile = "nhx_magic_weapons";

        internal static ManualLogSource Log { get; private set; }
        internal static AssetBundle Assets { get; private set; }

        private void Awake()
        {
            Log = Logger;

            string bundlePath = Path.Combine(Path.GetDirectoryName(Info.Location), BundleFile);
            Assets = AssetBundle.LoadFromFile(bundlePath);
            if (Assets == null)
            {
                Log.LogError($"Couldn't load {bundlePath}, so there are no magic weapons.");
                return;
            }

            AddLocalizations();
            CommandManager.Instance.AddConsoleCommand(new ItemLevelCommand());
            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            PrefabManager.OnVanillaPrefabsAvailable += AddWeapons;
            PrefabManager.OnPrefabsRegistered += AudioRouting.Apply;

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        // The weapons copy settings and effects from the game's own, which are only there once the main menu loads
        private static void AddWeapons()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddWeapons;
            Staves.StaffOfStorms.Staff.Add();
        }

        private static void AddLocalizations()
        {
            CustomLocalization localization = LocalizationManager.Instance.GetLocalization();
            foreach (string resource in typeof(Plugin).Assembly.GetManifestResourceNames())
            {
                // NhxMagicWeapons.Localizations.<language>.json
                string[] parts = resource.Split('.');
                if (parts.Length != 4 || parts[1] != "Localizations")
                {
                    continue;
                }
                using (Stream stream = typeof(Plugin).Assembly.GetManifestResourceStream(resource))
                using (StreamReader reader = new StreamReader(stream))
                {
                    localization.AddJsonFile(parts[2], reader.ReadToEnd());
                }
            }
        }
    }
}
