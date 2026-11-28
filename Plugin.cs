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
        private const string PluginVersion = "3.0.0";

        // What the mod uses of Magic Supremacy 3.1.5's asset bundle (Tools\slim_bundle.py), embedded in the DLL
        private const string BundleResource = "nhx_magic_weapons";
        // And of 3.1.7's, under names of its own so that Unity loads it next to the first (Tools\slim_bundle.py): the
        // Lightcaller and Bloodcaller sets, Tempest Eye and Blood Requiem (PrefabItems)
        private const string Bundle317Resource = "nhx_magic_weapons_317";

        internal static ManualLogSource Log { get; private set; }
        internal static AssetBundle Assets { get; private set; }
        internal static AssetBundle Assets317 { get; private set; }
        // The twins' fight in the log (Bosses\TwinsLog), off unless asked for (the user, 2026-11-15, the fight tested:
        // "maybe disable them so as not to spam the player so much"; kept for a later round of balancing)
        internal static ConfigEntry<bool> TwinsFightLog { get; private set; }
        // The twins' movement in the log (Bosses\TwinsMoveLog), apart from the fight log and off unless asked for (the
        // user, 2026-11-22: "it should be easy to turn off because I imagine the log spam")
        internal static ConfigEntry<bool> TwinsMoveLog { get; private set; }

        // Unity reads the bundles from them as their assets are loaded, so they stay open for as long as the game runs
        private static Stream s_bundleStream;
        private static Stream s_bundle317Stream;

        private void Awake()
        {
            Log = Logger;
            TwinsMoveLog = Config.Bind("Debug", "Twins movement log", false,
                "Write how the Stormcaller and Firecaller move to BepInEx's LogOutput.log, a line for each every second on the game that runs them: where they are, how far from their target and whether they gain on them, their speed and heading, what holds them, and what their AI told them to do. Apart from the fight log, and much more of it. For testing.");
            TwinsFightLog = Config.Bind("Debug", "Twins fight log", false,
                "Write the Stormcaller and Firecaller fight to BepInEx's LogOutput.log: every hit on the twins and on your character near them (from whom, with what, resistances, damage taken), and the fight's events (targets, voice lines, deaths, raises, holds, swaps, stacks, Stormstruck and Flamestruck), with a summary when you leave the fight. For testing and balancing.");

            s_bundleStream = typeof(Plugin).Assembly.GetManifestResourceStream(BundleResource);
            Assets = s_bundleStream != null ? AssetBundle.LoadFromStream(s_bundleStream) : null;
            if (Assets == null)
            {
                Log.LogError($"Couldn't load the asset bundle {BundleResource} from the DLL, so there are no magic weapons.");
                return;
            }
            s_bundle317Stream = typeof(Plugin).Assembly.GetManifestResourceStream(Bundle317Resource);
            Assets317 = s_bundle317Stream != null ? AssetBundle.LoadFromStream(s_bundle317Stream) : null;
            if (Assets317 == null)
            {
                Log.LogError($"Couldn't load the asset bundle {Bundle317Resource} from the DLL, so there are no Lightcaller or Bloodcaller sets, Tempest Eye or Blood Requiem.");
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
            Bosses.TwinsWave.Update();
            Bosses.TwinsItems.Update();
            Bosses.TwinsStasis.Update(Time.deltaTime);
            Bosses.TwinsVoices.Update();
            Bosses.TwinsLog.Update(Time.deltaTime);
            Bosses.TwinsMoveLog.Update(Time.deltaTime);
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
            // Magic Supremacy 3.1.7's sets and staves, as its prefabs make them, to try out
            if (Assets317)
            {
                PrefabItems.Add();
            }
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
