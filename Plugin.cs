using BepInEx;
using BepInEx.Configuration;
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
        private const string PluginVersion = "1.2.0";

        // What the mod uses of Magic Supremacy 3.1.5's asset bundle (Tools\slim_bundle.py), embedded in the DLL
        private const string BundleResource = "nhx_magic_weapons";

        internal static ManualLogSource Log { get; private set; }
        internal static AssetBundle Assets { get; private set; }
        // The twins' fight in the log (Bosses\TwinsLog), off unless asked for (the user, 2026-11-15, the fight tested:
        // "maybe disable them so as not to spam the player so much"; kept for a later round of balancing)
        internal static ConfigEntry<bool> TwinsFightLog { get; private set; }

        // Unity reads the bundle from it as its assets are loaded, so it stays open for as long as the game runs
        private static Stream s_bundleStream;

        private void Awake()
        {
            Log = Logger;
            TwinsFightLog = Config.Bind("Debug", "Twins fight log", false,
                "Write the Stormcaller and Firecaller fight to BepInEx's LogOutput.log: every hit on the twins and on your character near them (from whom, with what, resistances, damage taken), and the fight's events (targets, voice lines, deaths, raises, holds, swaps). For testing and balancing.");

            s_bundleStream = typeof(Plugin).Assembly.GetManifestResourceStream(BundleResource);
            Assets = s_bundleStream != null ? AssetBundle.LoadFromStream(s_bundleStream) : null;
            if (Assets == null)
            {
                Log.LogError($"Couldn't load the asset bundle {BundleResource} from the DLL, so there are no magic weapons.");
                return;
            }

            AddLocalizations();
            CommandManager.Instance.AddConsoleCommand(new ItemLevelCommand());
            CommandManager.Instance.AddConsoleCommand(new ArmorLookCommand());
            CommandManager.Instance.AddConsoleCommand(new ArmorLevelCommand());
            CommandManager.Instance.AddConsoleCommand(new BeamOrbitCommand());
            CommandManager.Instance.AddConsoleCommand(new BossCommand());
            CommandManager.Instance.AddConsoleCommand(new AltarCommand());
            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            PrefabManager.OnVanillaPrefabsAvailable += AddItems;
            PrefabManager.OnPrefabsRegistered += AudioRouting.Apply;
            // The twins' altar, a location: made from the game's Ashlands pieces once they're there
            ZoneManager.OnVanillaLocationsAvailable += Bosses.TwinsAltars.AddLocation;
            // The twins' voices, from the DLL (Bosses\EmbeddedAudio)
            StartCoroutine(Bosses.TwinsVoices.Load());

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        // The twins' fight, for the local player (its stacks and swaps: Bosses\TwinsDuel)
        private void Update()
        {
            Bosses.TwinsDuel.Update(Time.deltaTime);
            Bosses.TwinsItems.Update();
            Bosses.TwinsStasis.Update(Time.deltaTime);
            Bosses.TwinsVoices.Update();
            Bosses.TwinsLog.Update(Time.deltaTime);
        }

        // The weapons copy settings and effects from the game's own, which are only there once the main menu loads, and the
        // recipes need the game's items
        private static void AddItems()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddItems;
            Staves.StaffOfStorms.Staff.Add();
            Staves.WrathOfMuspelheim.Staff.Add();
            // The twins' drops and lodestone, before the armor whose recipes ask for the drops (they need the staves'
            // crystals and particles, so after the staves)
            Bosses.TwinsItems.Add();
            Armor.StormcallerArmor.Add();
            // They wear the sets
            Bosses.CallerBosses.Add();
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
