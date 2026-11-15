using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The twins' altar, a location of the mod's own (the user, 2026-11-15: altar C of the showroom, "simple and also looks
    // interesting enough"; for the arena's edge, the twisted pillar bases "with the glowing crystals on top"):
    // - The dais: a ruined Ashlands floor, the sigil's three rings turning on it (blue, violet, orange), four broken
    //   pillars at its corners, the twins' crystals circling over its middle (TwinsAltar: offer a lodestone there)
    // - The arena: Stones twisted pillar bases ArenaRadius out, each crowned with a floating crystal, storm and fire in
    //   turn, with a light and motes, settled on the ground as it spawns (SnapToGround)
    // - The ground leveled and cleared about the dais (TerrainModifier), and no building within NoBuildRadius (the
    //   game's no-build zone of locations: Location.m_noBuild, as at its boss altars)
    // Built as the game builds its locations: every game makes the location's looks from a networked LocationProxy, so an
    // altar placed by command is the same as a generated one.
    // Where (the user: "3 generated altars plus an admin command for one [...] careful not to overlap with other
    // important buildings (dungeons or even Fader's arena). Since the world is already generated, maybe we need an admin
    // command to spawn the 3 altars?"): a new world generates Quantity of them in the Ashlands by itself; on a world
    // already made, the game only places locations in zones not yet generated, so nhx_altar generate (an admin, run by
    // the server) finds spots for them (Generate) and nhx_altar place puts one where the admin stands (Place). Both
    // register the altar as a location, so a lodestone finds it (Game.DiscoverClosestLocation)
    internal static class TwinsAltars
    {
        internal const string LocationName = "NhxTwinsAltar";
        internal const float ArenaRadius = 40f;
        private const float NoBuildRadius = 46f;
        private const int Quantity = 3;
        private const int Stones = 12;
        // The altars this far apart at least (meters), and this far from any other location's edge
        private const float AltarsApart = 1200f;
        private const float LocationMargin = 40f;
        // No player's building within this much of an altar placed by generate
        private const float BaseMargin = 100f;
        // The arena's ground at most this far above or below the dais's anywhere (meters): it's leveled, and less to
        // level is less of a bank at its edge (12 at first, before the whole arena was leveled)
        private const float MaxArenaRise = 6f;
        private const int Tries = 200000;

        private const string PlaceRpc = "NhxAltarPlace";
        private const string GenerateRpc = "NhxAltarGenerate";
        private const string ReportRpc = "NhxAltarReport";

        private static readonly List<string> s_missing = new List<string>();

        // ---- The location ----

        // Made once: Jotunn keeps its custom locations for every world loaded after. (Not asked of
        // ZoneManager.GetCustomLocation, which throws for a name it doesn't have: the first test's log, 2026-11-15, where
        // that left the game with no altar at all, so generate placed none and the lodestones found none)
        private static bool s_added;

        internal static void AddLocation()
        {
            ZoneManager.OnVanillaLocationsAvailable -= AddLocation;
            if (s_added)
            {
                return;
            }
            s_added = true;
            GameObject root = ZoneManager.Instance.CreateLocationContainer(LocationName);
            Location location = root.AddComponent<Location>();
            location.m_exteriorRadius = NoBuildRadius;
            location.m_clearArea = true;
            location.m_noBuild = true;
            location.m_noBuildRadiusOverride = NoBuildRadius;
            // The offering is the dais's alone: on its own object, so the ring's stones don't find it above them (the
            // game looks for what's hovered up from what's under the crosshair; the user, 2026-11-15: "I could interact
            // with the crystals floating at the edges of the arena to summon the bosses")
            GameObject dais = new GameObject("dais");
            dais.transform.SetParent(root.transform, false);
            dais.AddComponent<TwinsAltar>();

            // The whole arena leveled to the dais's ground, blending into the land just past the stones (the user: "Do you
            // think the area can be leveled [...]?"; at first only the dais's 7 m was); the dais's ground cleared
            TerrainModifier terrain = root.AddComponent<TerrainModifier>();
            terrain.m_level = true;
            terrain.m_levelRadius = ArenaRadius + 2f;
            terrain.m_square = false;
            terrain.m_smooth = true;
            terrain.m_smoothRadius = ArenaRadius + 8f;
            terrain.m_paintCleared = true;
            terrain.m_paintRadius = 8f;
            terrain.m_paintType = TerrainModifier.PaintType.Dirt;

            s_missing.Clear();
            BuildDais(dais.transform);
            BuildStones(root.transform);
            if (s_missing.Count > 0)
            {
                Plugin.Log.LogWarning("The twins' altar is without: " + string.Join(", ", s_missing) + " (not in the game).");
            }

            ZoneManager.Instance.AddCustomLocation(new CustomLocation(root, false, new LocationConfig
            {
                Biome = Heightmap.Biome.AshLands,
                Quantity = Quantity,
                Priotized = true,
                ExteriorRadius = NoBuildRadius,
                ClearArea = true,
                MinAltitude = 3f,
                MaxTerrainDelta = 6f,
                MinDistanceFromSimilar = AltarsApart,
                RandomRotation = true,
            }));
        }

        // The ruined floor, its rune rings, the broken pillars at its corners and the twins' crystals over it
        private static void BuildDais(Transform root)
        {
            GameObject floor = Looks.Copy("Ashlands_Ruins_Floor_6x6", root, Vector3.zero, Quaternion.identity, 1f, s_missing);
            float top = 0.3f;
            if (floor)
            {
                // Sunk a little, its top a little above the leveled ground (heights from the root, wherever it's made)
                float ground = root.position.y;
                Bounds bounds = Looks.MeshBounds(floor);
                floor.transform.localPosition = new Vector3(0f, ground - bounds.min.y - 0.25f, 0f);
                top = Looks.MeshBounds(floor).max.y - ground;
            }
            Vector3 middle = new Vector3(0f, top + 0.03f, 0f);
            Looks.Disc(root, middle, 2.6f, 0, Looks.Storm, 8f);
            Looks.Disc(root, middle, 2.6f, 1, Looks.Both, -5f);
            Looks.Disc(root, middle, 2.6f, 2, Looks.Fire, 3f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = Quaternion.AngleAxis(45f + 90f * i, Vector3.up) * Vector3.forward * 3.6f;
                Looks.Copy("Ashlands_Pillar4_tip_broken1", root, new Vector3(corner.x, top, corner.z), Quaternion.Euler(0f, 90f * i, 0f), 0.6f, s_missing);
            }
            Looks.Twins(root, middle + Vector3.up * 2.2f, 0.55f);
        }

        // The arena's edge: twisted pillar bases crowned with crystals, storm and fire in turn
        private static void BuildStones(Transform root)
        {
            for (int i = 0; i < Stones; i++)
            {
                bool fire = i % 2 == 1;
                Vector3 at = Quaternion.AngleAxis(360f * i / Stones, Vector3.up) * Vector3.forward * ArenaRadius;
                GameObject holder = new GameObject("stone" + i);
                holder.transform.SetParent(root, false);
                holder.transform.localPosition = at;
                holder.transform.localRotation = Quaternion.LookRotation(-at);
                holder.AddComponent<SnapToGround>();
                GameObject stone = Looks.Copy("Ashlands_Ruins_twist_PillarBaseSmall", holder.transform, Vector3.zero, Quaternion.identity, 1f, s_missing);
                float top = 1f;
                if (stone)
                {
                    Bounds bounds = Looks.MeshBounds(stone);
                    stone.transform.localPosition = new Vector3(0f, -bounds.min.y + holder.transform.position.y - 0.1f, 0f);
                    top = Looks.MeshBounds(stone).max.y - holder.transform.position.y;
                }
                Color color = fire ? Looks.Fire : Looks.Storm;
                GameObject crystal = Looks.Crystal(holder.transform, fire, 0.45f);
                if (crystal)
                {
                    crystal.transform.localPosition = new Vector3(0f, top + 0.35f, 0f);
                    crystal.AddComponent<LookSpin>().m_speed = 30f;
                }
                Looks.Glow(holder.transform, new Vector3(0f, top + 0.4f, 0f), color, 1.8f, 6f);
                Looks.Motes(holder.transform, new Vector3(0f, top + 0.35f, 0f), fire, 0.3f, 6f);
            }
        }

        // ---- Placing them ----

        // On every game, when its routed RPCs are there
        [HarmonyPatch(typeof(Game), "Start")]
        private static class Rpcs
        {
            private static void Postfix()
            {
                ZRoutedRpc.instance.Register<Vector3, float>(PlaceRpc, RPC_Place);
                ZRoutedRpc.instance.Register<int>(GenerateRpc, RPC_Generate);
                ZRoutedRpc.instance.Register<Vector3>(CleanRpc, RPC_Clean);
                ZRoutedRpc.instance.Register<string>(ReportRpc, RPC_Report);
            }
        }

        // From an admin's console (AltarCommand): run by the server
        internal static void RequestPlace(Vector3 point, float yaw) => ZRoutedRpc.instance.InvokeRoutedRPC(PlaceRpc, point, yaw);

        internal static void RequestGenerate(int count) => ZRoutedRpc.instance.InvokeRoutedRPC(GenerateRpc, count);

        private static void RPC_Place(long sender, Vector3 point, float yaw)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer() || !IsAdmin(sender))
            {
                return;
            }
            Report(sender, Place(point, yaw, out string why)
                ? $"An altar of the elements now stands at {Where(point)}, its arena cleared ({s_cleared} things removed)."
                : "No altar: " + why);
        }

        private static void RPC_Generate(long sender, int count)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer() || !IsAdmin(sender))
            {
                return;
            }
            Report(sender, Generate(count <= 0 ? Quantity : count));
        }

        private static void RPC_Report(long sender, string text)
        {
            if (Console.instance)
            {
                Console.instance.Print(text);
            }
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, text);
        }

        private static void Report(long peer, string text) => ZRoutedRpc.instance.InvokeRoutedRPC(peer, ReportRpc, text);

        // The server's own player (a host) or an admin on its list
        private static bool IsAdmin(long sender)
        {
            if (sender == ZRoutedRpc.instance.m_id)
            {
                return true;
            }
            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            return peer != null && peer.m_socket != null && ZNet.instance.IsAdmin(peer.m_socket.GetHostName());
        }

        private static string Where(Vector3 point) => $"{point.x:0}, {point.z:0}";

        private static ZoneSystem.ZoneLocation Location()
        {
            return ZoneSystem.instance ? ZoneSystem.instance.m_locations.Find(l => l != null && l.m_prefabName == LocationName) : null;
        }

        // An altar at `point`, if it's clear of every other location; registered (for lodestones) and built
        private static bool Place(Vector3 point, float yaw, out string why)
        {
            ZoneSystem.ZoneLocation location = Location();
            if (location == null)
            {
                why = "the altar isn't in the game (see the log).";
                return false;
            }
            if (ZoneSystem.instance.ZoneHasLocation(point))
            {
                why = "this zone already has a location.";
                return false;
            }
            string near = NearLocation(point);
            if (near != null)
            {
                why = $"too close to {near}.";
                return false;
            }
            point.y = ZoneSystem.instance.GetGroundHeight(point, out float height) ? height : WorldGenerator.instance.GetHeight(point.x, point.z);
            AccessTools.Method(typeof(ZoneSystem), "RegisterLocation").Invoke(ZoneSystem.instance, new object[] { location, point, true });
            GameObject proxy = Object.Instantiate(ZoneSystem.instance.m_locationProxyPrefab, point, Quaternion.Euler(0f, yaw, 0f));
            proxy.GetComponent<LocationProxy>().SetLocation(LocationName, Random.Range(0, 99999), true);
            s_cleared = ClearDebris(point);
            why = null;
            return true;
        }

        // How much the last Place cleared, for its report
        private static int s_cleared;

        // ---- Clearing the arena ----

        // The arena cleared of what the world had grown or left on it (the user, 2026-11-15: "it was full of debris which
        // prevented the bosses from teleporting next to me. Do you think the area can be leveled and everything on it
        // removed when placing the altar?"): every object within ArenaRadius + ClearMargin of `point` that's vegetation
        // (the game's vegetation list: rocks, trees, bushes, plants) or debris (rocks and ore that break, trees and logs,
        // things that break, things to pick), and that no player built; never a building piece, a creature or an item.
        // A world generated with the altar never grows any there (its location clears its area); one placed later
        // needs this. On the server, which holds every object: it takes each over and destroys it, for every game. How
        // many went
        private const float ClearMargin = 4f;

        private static int ClearDebris(Vector3 point)
        {
            HashSet<int> vegetation = new HashSet<int>();
            foreach (ZoneSystem.ZoneVegetation entry in ZoneSystem.instance.m_vegetation)
            {
                if (entry != null && entry.m_prefab)
                {
                    vegetation.Add(entry.m_prefab.name.GetStableHashCode());
                }
            }
            float reach = ArenaRadius + ClearMargin;
            List<ZDO> doomed = new List<ZDO>();
            Dictionary<int, bool> debris = new Dictionary<int, bool>();
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (zdo == null || Utils.DistanceXZ(zdo.GetPosition(), point) > reach || zdo.GetLong(ZDOVars.s_creator) != 0L)
                {
                    continue;
                }
                int prefab = zdo.GetPrefab();
                if (!debris.TryGetValue(prefab, out bool isDebris))
                {
                    isDebris = vegetation.Contains(prefab) || IsDebris(ZNetScene.instance.GetPrefab(prefab));
                    debris[prefab] = isDebris;
                }
                if (isDebris)
                {
                    doomed.Add(zdo);
                }
            }
            foreach (ZDO zdo in doomed)
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(zdo);
            }
            return doomed.Count;
        }

        // A prefab of what grows or lies about: never a building piece, a creature, an item, a chest or a tombstone
        private static bool IsDebris(GameObject prefab)
        {
            if (!prefab || prefab.GetComponent<Piece>() || prefab.GetComponent<WearNTear>() || prefab.GetComponent<Character>()
                || prefab.GetComponent<ItemDrop>() || prefab.GetComponent<LocationProxy>() || prefab.GetComponent<Container>()
                || prefab.GetComponent<TombStone>())
            {
                return false;
            }
            return prefab.GetComponent<MineRock>() || prefab.GetComponent<MineRock5>() || prefab.GetComponent<TreeBase>()
                || prefab.GetComponent<TreeLog>() || prefab.GetComponent<Destructible>() || prefab.GetComponent<Pickable>();
        }

        // An admin's ask to clear the arena of the altar nearest them (one placed before clearing came, 2026-11-15)
        internal static void RequestClean(Vector3 point) => ZRoutedRpc.instance.InvokeRoutedRPC(CleanRpc, point);

        private const string CleanRpc = "NhxAltarClean";
        private const float CleanReach = 150f;

        private static void RPC_Clean(long sender, Vector3 point)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer() || !IsAdmin(sender))
            {
                return;
            }
            Vector3 altar = Vector3.zero;
            float nearest = float.PositiveInfinity;
            foreach (ZoneSystem.LocationInstance instance in ZoneSystem.instance.GetLocationList())
            {
                float distance = Utils.DistanceXZ(instance.m_position, point);
                if (instance.m_location.m_prefabName == LocationName && distance < nearest)
                {
                    nearest = distance;
                    altar = instance.m_position;
                }
            }
            Report(sender, nearest > CleanReach ? $"No altar within {CleanReach:0} m." : $"The arena at {Where(altar)} is cleared: {ClearDebris(altar)} things removed.");
        }

        // The location whose edge is within reach of an altar's at `point`, or null
        private static string NearLocation(Vector3 point)
        {
            foreach (ZoneSystem.LocationInstance instance in ZoneSystem.instance.GetLocationList())
            {
                float reach = Mathf.Max(instance.m_location.m_exteriorRadius, instance.m_location.m_interiorRadius) + NoBuildRadius + LocationMargin;
                if (Utils.DistanceXZ(instance.m_position, point) < reach)
                {
                    return instance.m_location.m_prefabName;
                }
            }
            return null;
        }

        // Up to `wanted` altars in the world, the ones missing placed in the Ashlands: on flat ground out of the lava,
        // above the sea, clear of every location (dungeons, Fader's arena...) and of the players' buildings, apart
        private static string Generate(int wanted)
        {
            if (Location() == null)
            {
                return "No altar: it isn't in the game (see the log).";
            }
            List<Vector3> altars = new List<Vector3>();
            foreach (ZoneSystem.LocationInstance instance in ZoneSystem.instance.GetLocationList())
            {
                if (instance.m_location.m_prefabName == LocationName)
                {
                    altars.Add(instance.m_position);
                }
            }
            if (altars.Count >= wanted)
            {
                return $"There are already {altars.Count} altars: none placed.";
            }
            HashSet<Vector2Int> bases = PlayerBuildings();
            List<string> placed = new List<string>();
            float radius = WorldGenerator.worldSize;
            for (int attempt = 0; attempt < Tries && altars.Count < wanted; attempt++)
            {
                Vector2 random = Random.insideUnitCircle * radius;
                Vector3 point = new Vector3(random.x, 0f, random.y);
                if (!GoodSpot(point, altars, bases))
                {
                    continue;
                }
                if (Place(point, Random.Range(0f, 360f), out _))
                {
                    altars.Add(point);
                    placed.Add(Where(point));
                }
            }
            return placed.Count == 0 ? "No spot found for an altar." : $"Altars placed at: {string.Join("; ", placed)}.";
        }

        private static bool GoodSpot(Vector3 point, List<Vector3> altars, HashSet<Vector2Int> bases)
        {
            WorldGenerator world = WorldGenerator.instance;
            if (world.GetBiome(point.x, point.z) != Heightmap.Biome.AshLands)
            {
                return false;
            }
            foreach (Vector3 altar in altars)
            {
                if (Utils.DistanceXZ(altar, point) < AltarsApart)
                {
                    return false;
                }
            }
            // The arena's ground: Ashlands, above the sea, out of the lava, and the dais's flat enough
            float water = ZoneSystem.instance.m_waterLevel;
            float center = world.GetHeight(point.x, point.z);
            for (int ring = 0; ring <= 4; ring++)
            {
                float distance = ArenaRadius * ring / 4f;
                for (int i = 0; i < (ring == 0 ? 1 : 8); i++)
                {
                    Vector3 sample = point + Quaternion.AngleAxis(45f * i, Vector3.up) * Vector3.forward * distance;
                    float height = world.GetHeight(sample.x, sample.z);
                    if (height < water + 2f || ZoneSystem.IsLavaPreHeightmap(sample, 0.4f)
                        || world.GetBiome(sample.x, sample.z) != Heightmap.Biome.AshLands
                        || (distance <= 10f && Mathf.Abs(height - center) > 2.5f) || Mathf.Abs(height - center) > MaxArenaRise)
                    {
                        return false;
                    }
                }
            }
            if (ZoneSystem.instance.ZoneHasLocation(point) || NearLocation(point) != null)
            {
                return false;
            }
            Vector2Int cell = Cell(point);
            int cells = Mathf.CeilToInt((ArenaRadius + BaseMargin) / CellSize);
            for (int x = -cells; x <= cells; x++)
            {
                for (int z = -cells; z <= cells; z++)
                {
                    if (bases.Contains(new Vector2Int(cell.x + x, cell.y + z)))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private const float CellSize = 64f;

        private static Vector2Int Cell(Vector3 point) => new Vector2Int(Mathf.FloorToInt(point.x / CellSize), Mathf.FloorToInt(point.z / CellSize));

        // Where players have built (the server holds every object; what a player built has its creator)
        private static HashSet<Vector2Int> PlayerBuildings()
        {
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (zdo != null && zdo.GetLong(ZDOVars.s_creator) != 0L)
                {
                    cells.Add(Cell(zdo.GetPosition()));
                }
            }
            return cells;
        }
    }

    // The altar's dais (on its own object, the floor, pillars, runes and crystals under it): looked at, it asks for a
    // lodestone; offered one, it calls the twins over
    // it, linked, their arena the altar's (TwinsBond.Summon), and keeps the lodestone. Not while twins are already near
    internal class TwinsAltar : MonoBehaviour, Hoverable, Interactable
    {
        // Twins within this of the altar are a fight already going
        private const float Busy = 90f;

        public string GetHoverText()
        {
            return Localization.instance.Localize("$piece_nhx_twins_altar\n[<color=yellow><b>1-8</b></color>] $piece_nhx_twins_offer");
        }

        public string GetHoverName()
        {
            return Localization.instance.Localize("$piece_nhx_twins_altar");
        }

        public float GetHoverOffset()
        {
            return 0f;
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (!hold)
            {
                user.Message(MessageHud.MessageType.Center, "$msg_nhx_altar_offer");
            }
            return false;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared.m_name != TwinsItems.LodestoneKey)
            {
                return false;
            }
            if (CallerBoss.NearestLiving(transform.position, Element.None, out _) < Busy)
            {
                user.Message(MessageHud.MessageType.Center, "$msg_nhx_altar_busy");
                return true;
            }
            Vector3 toward = user.transform.position - transform.position;
            if (!TwinsBond.Summon(transform.position, toward, transform.position))
            {
                return true;
            }
            user.GetInventory().RemoveOneItem(item);
            return true;
        }
    }
}
