using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Bosses
{
    // The twins' items (the user, 2026-11-15), each the game's model picked in the showroom, tinted and glowing as there,
    // with its element's motes, its icon drawn from it (Jotunn's RenderManager):
    // - The Stormheart (the Stormcaller's, a Morgen heart in the storm's blue) and Surtr's Tear (the Firecaller's, a dragon
    //   tear in Muspelheim's orange): "4 of each per kill" (TwinsBond.Won), each piece of the matching set needing one
    //   to be crafted (Armor\StormcallerArmor)
    // - The Lodestone of the Elements (a thunder stone in both colors): "Thunder stone, Surtling core, 10 grausten, 2
    //   charcoal resin" at the Galdr Table; used from the inventory or the hotbar it pins the nearest altar on the map
    //   (as a Vegvisir: Game.DiscoverClosestLocation) and stays; offered at an altar it calls the twins and goes
    //   (TwinsAltar.UseItem)
    internal static class TwinsItems
    {
        internal const string StormheartName = "NhxStormheart";
        internal const string SurtrsTearName = "NhxSurtrsTear";
        internal const string LodestoneName = "NhxLodestone";
        internal const string LodestoneKey = "$item_nhx_lodestone";

        // Each victory's drops (the user's): 4 of each
        internal const int DropsPerKill = 4;

        internal static GameObject Stormheart { get; private set; }
        internal static GameObject SurtrsTear { get; private set; }
        internal static GameObject Lodestone { get; private set; }

        // Before the armor (whose recipes ask for the drops)
        internal static void Add()
        {
            Stormheart = AddItem(StormheartName, "MorgenHeart", "$item_nhx_stormheart", Looks.Storm, 2.5f, false, null);
            SurtrsTear = AddItem(SurtrsTearName, "DragonTear", "$item_nhx_surtrstear", Looks.Fire, 2.5f, true, null);
            Lodestone = AddItem(LodestoneName, "Thunderstone", LodestoneKey, Looks.Both, 1.5f, null, new ItemConfig
            {
                CraftingStation = CraftingStations.GaldrTable,
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig("Thunderstone", 1),
                    new RequirementConfig("SurtlingCore", 1),
                    new RequirementConfig("Grausten", 10),
                    new RequirementConfig("CharcoalResin", 2),
                },
            });
        }

        // A copy of the game's item `basePrefab`, tinted and glowing in `color`, with sparks (storm), embers (fire) or both
        // (null), and a recipe if `config` has one
        private static GameObject AddItem(string name, string basePrefab, string key, Color color, float glow, bool? fire, ItemConfig config)
        {
            config = config ?? new ItemConfig();
            config.Name = key;
            config.Description = key + "_description";
            CustomItem item = new CustomItem(name, basePrefab, config);
            GameObject prefab = item.ItemPrefab;
            if (!prefab)
            {
                Plugin.Log.LogWarning($"The game has no {basePrefab}: no {name}.");
                return null;
            }
            Looks.Tint(prefab, color, glow);
            Looks.Glow(prefab.transform, Vector3.zero, color, 1f, 2.5f);
            if (fire != true)
            {
                Looks.Motes(prefab.transform, Vector3.zero, false, 0.15f, 4f);
            }
            if (fire != false)
            {
                Looks.Motes(prefab.transform, Vector3.zero, true, 0.15f, 4f);
            }
            // Its icon drawn from its tinted model; the base item's if that can't be done
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                try
                {
                    Sprite icon = RenderManager.Instance.Render(prefab, RenderManager.IsometricRotation);
                    if (icon)
                    {
                        item.ItemDrop.m_itemData.m_shared.m_icons = new[] { icon };
                    }
                }
                catch (System.Exception exception)
                {
                    Plugin.Log.LogWarning($"No icon drawn for {name} ({exception.Message}): it has the {basePrefab}'s.");
                }
            }
            ItemManager.Instance.AddItem(item);
            return prefab;
        }

        // The lodestone used: the nearest altar pinned on the map, unless it's being offered at one (the game first
        // offers an item to what the player looks at: Humanoid.UseItem)
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        private static class UseLodestone
        {
            private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, bool fromInventoryGui)
            {
                if (item == null || item.m_shared.m_name != LodestoneKey || !(__instance is Player player) || player != Player.m_localPlayer)
                {
                    return true;
                }
                if (!fromInventoryGui)
                {
                    GameObject hover = player.GetHoverObject();
                    if (hover && hover.GetComponentInParent<TwinsAltar>())
                    {
                        return true;
                    }
                }
                if (Game.instance)
                {
                    Game.instance.DiscoverClosestLocation(TwinsAltars.LocationName, player.transform.position, PinName, (int)Minimap.PinType.Boss);
                    s_askedAt = Time.time;
                }
                return false;
            }
        }

        // What the lodestone says comes with the server's answer (the pin), or its silence: the server answers only when
        // it finds an altar (Game.RPC_DiscoverClosestLocation), and at first "pulls toward" showed with no pin at all
        // (the user, 2026-11-15: "nothing happens and there is no map marker")
        private const string PinName = "$piece_nhx_twins_altar";
        private const float AnswerTime = 3f;
        private static float s_askedAt = -1f;

        [HarmonyPatch(typeof(Game), "RPC_DiscoverLocationResponse")]
        private static class Answer
        {
            private static void Postfix(string pinName)
            {
                if (pinName == PinName && s_askedAt >= 0f)
                {
                    s_askedAt = -1f;
                    Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "$msg_nhx_lodestone_pull");
                }
            }
        }

        // Every frame (Plugin.Update): no answer in time, no altar
        internal static void Update()
        {
            if (s_askedAt >= 0f && Time.time - s_askedAt > AnswerTime)
            {
                s_askedAt = -1f;
                Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "$msg_nhx_lodestone_none");
            }
        }
    }
}
