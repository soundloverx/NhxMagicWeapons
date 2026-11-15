using System.Collections.Generic;
using HarmonyLib;
using NhxMagicWeapons.Staves.StaffOfStorms;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The twins die together (the user, 2026-11-14: "Let's go with 1 and 3", 1 being "if one falls, the survivor raises it
    // after about 30 s unless you finish it first"; and for one player "maybe 45 s"): two twins summoned together are
    // linked (each has the other's ZDOID in its ZDO: Link). When one falls (its owner's game, Character.OnDeath), it tells
    // its twin, wherever it's run (a routed RPC to the twin's ZDO: Fell); the survivor's game then marks where it fell with
    // the fallen's rune, lighting up ring by ring over the window as a countdown (ElementRune, no caster: harmless), tells
    // the players around, and when the window is out, raises its twin there at half its health (Raise). Fall within the
    // window, and nothing is raised: the fight is won (Won: the Stormhearts and Surtr's Tears). The window is RaiseDelay,
    // or SoloDelay with one player in the fight (TwinsAI.InReach).
    // While its twin is down, the survivor fights alone (Alone; the fight's redesign, agreed with the user 2026-11-15, its
    // change 7): it enters its second phase at once, whatever its health (Fell: TwinsPhase.TwinFell), hunts whoever can
    // hurt it (TwinsAI), and its fallen twin's element is silent on the players (TwinsDuel.Silence).
    // Summoned together (Summon: an altar's offering, or nhx_boss summon), they're linked, and an altar's twins know its
    // arena (Arena: TwinsJudgment, TwinsRules). Twins spawned one by one (devcommands' spawn) aren't linked: they fight,
    // but alone, and drop nothing
    internal static class TwinsBond
    {
        private const float RaiseDelay = 30f;
        private const float SoloDelay = 45f;
        // The countdown rune's size, and the raised twin's health
        private const float RuneRadius = 4f;
        private const float RaisedHealth = 0.5f;
        // Players this far from the twins hear what happens to them (TwinsJudgment's news too): 80 m at first, but from one
        // edge of the ring to the other is 84 m, and anyone in the ring is in the fight (TwinsDuel.InRing)
        internal const float TellRange = 100f;

        private const string FellRpc = "NhxTwinFell";
        // Summoned (by an altar or nhx_boss summon): this far apart, each arriving in its element (its rune flashing)
        private const float SummonApart = 10f;
        private const float ArrivalRadius = 3f;

        private static readonly KeyValuePair<int, int> s_twinKey = ZDO.GetHashZDOID("nhx_twin");
        // The arena they were called into (an altar's), if any: its middle
        private static readonly int s_arenaKey = "nhx_twins_arena".GetStableHashCode();
        private static readonly int s_hasArenaKey = "nhx_twins_has_arena".GetStableHashCode();
        private static readonly KeyValuePair<int, int> s_runeKey = ZDO.GetHashZDOID("nhx_twin_rune");
        private static readonly int s_raiseAtKey = "nhx_twin_raise_at".GetStableHashCode();
        private static readonly int s_fallenAtKey = "nhx_twin_fallen_at".GetStableHashCode();
        private static readonly int s_raisedKey = "nhx_twin_raised".GetStableHashCode();
        // When they were summoned (ticks), kept by a raised twin: their hold on the arena's creatures waits a little
        // (TwinsStasis)
        private static readonly int s_sinceKey = "nhx_twins_since".GetStableHashCode();

        internal static void Register(CallerBoss boss)
        {
            boss.m_nview.Register<Vector3>(FellRpc, (sender, point) => Fell(boss, point));
        }

        // On the game that made both (the summon)
        internal static void Link(CallerBoss a, CallerBoss b)
        {
            a.m_nview.GetZDO().Set(s_twinKey, b.m_nview.GetZDO().m_uid);
            b.m_nview.GetZDO().Set(s_twinKey, a.m_nview.GetZDO().m_uid);
        }

        // Both twins at `center`, side by side across `toward` (facing it), linked, arriving in their elements; their arena
        // `arena`'s, if any (an altar's: TwinsAltar). On the summoner's game, which runs them at first
        internal static bool Summon(Vector3 center, Vector3 toward, Vector3? arena)
        {
            GameObject stormPrefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(CallerBosses.StormName) : null;
            GameObject firePrefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(CallerBosses.FireName) : null;
            if (!stormPrefab || !firePrefab)
            {
                return false;
            }
            toward.y = 0f;
            toward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, toward);
            Vector3 stormAt = LightningStrike.Ground(center + side * SummonApart * 0.5f + Vector3.up * 50f);
            Vector3 fireAt = LightningStrike.Ground(center - side * SummonApart * 0.5f + Vector3.up * 50f);
            Quaternion facing = Quaternion.LookRotation(toward);
            CallerBoss storm = Object.Instantiate(stormPrefab, stormAt, facing).GetComponent<CallerBoss>();
            CallerBoss fire = Object.Instantiate(firePrefab, fireAt, facing).GetComponent<CallerBoss>();
            if (storm && fire && storm.m_nview && storm.m_nview.IsValid() && fire.m_nview && fire.m_nview.IsValid())
            {
                Link(storm, fire);
                long now = Now;
                storm.m_nview.GetZDO().Set(s_sinceKey, now);
                fire.m_nview.GetZDO().Set(s_sinceKey, now);
                if (arena.HasValue)
                {
                    SetArena(storm, arena.Value);
                    SetArena(fire, arena.Value);
                }
            }
            if (TwinsDuel.s_stormRune)
            {
                ElementRune.Create(TwinsDuel.s_stormRune, stormAt, 0f, ArrivalRadius);
            }
            if (TwinsDuel.s_fireRune)
            {
                ElementRune.Create(TwinsDuel.s_fireRune, fireAt, 0f, ArrivalRadius);
            }
            Tell(center, "$msg_nhx_twins_summoned");
            TwinsLog.Event($"Summoned the twins at {TwinsLog.At(center)}{(arena.HasValue ? ", in the arena at " + TwinsLog.At(arena.Value) : ", no arena")}, by {TwinsLog.Name(Player.m_localPlayer)}.");
            TwinsVoices.Say(TwinsVoices.Start, center, "summoned");
            return true;
        }

        // How long ago these twins were summoned (seconds; long ago for twins summoned before it was kept)
        internal static float Since(CallerBoss boss)
        {
            ZDO zdo = boss && boss.m_nview && boss.m_nview.IsValid() ? boss.m_nview.GetZDO() : null;
            long since = zdo != null ? zdo.GetLong(s_sinceKey) : 0L;
            return since == 0L ? float.PositiveInfinity : (float)System.TimeSpan.FromTicks(Now - since).TotalSeconds;
        }

        // This twin's twin's ZDO (on any game), or null
        internal static ZDO TwinZDO(CallerBoss boss)
        {
            ZDO zdo = boss && boss.m_nview && boss.m_nview.IsValid() ? boss.m_nview.GetZDO() : null;
            ZDOID twin = zdo != null ? zdo.GetZDOID(s_twinKey) : ZDOID.None;
            return twin.IsNone() ? null : ZDOMan.instance.GetZDO(twin);
        }

        // Whether this twin fights alone (on any game, from its ZDO): its twin fallen, to be raised when its countdown is
        // out; or with no twin at all (spawned alone), and no living one of the other element about
        internal static bool Alone(CallerBoss boss)
        {
            ZDO zdo = boss && boss.m_nview && boss.m_nview.IsValid() ? boss.m_nview.GetZDO() : null;
            if (zdo == null)
            {
                return false;
            }
            if (zdo.GetLong(s_raiseAtKey) != 0L)
            {
                return true;
            }
            return zdo.GetZDOID(s_twinKey).IsNone()
                && float.IsInfinity(CallerBoss.NearestLiving(boss.transform.position, TwinsDuel.Other(boss.Element), out _));
        }

        private static void SetArena(CallerBoss boss, Vector3 arena)
        {
            boss.m_nview.GetZDO().Set(s_arenaKey, arena);
            boss.m_nview.GetZDO().Set(s_hasArenaKey, true);
        }

        // The arena's middle these twins fight in, if they were called into one (on any game, from the twin's ZDO)
        internal static bool Arena(CallerBoss boss, out Vector3 arena)
        {
            arena = Vector3.zero;
            ZDO zdo = boss && boss.m_nview && boss.m_nview.IsValid() ? boss.m_nview.GetZDO() : null;
            if (zdo == null || !zdo.GetBool(s_hasArenaKey))
            {
                return false;
            }
            arena = zdo.GetVec3(s_arenaKey, Vector3.zero);
            return true;
        }

        private static long Now => ZNet.instance ? ZNet.instance.GetTime().Ticks : 0L;

        // A raised twin comes back at half its health (CallerBoss.Start, on its owner's game)
        internal static void OnStart(CallerBoss boss)
        {
            ZDO zdo = boss.m_nview.GetZDO();
            if (zdo.GetBool(s_raisedKey))
            {
                zdo.Set(s_raisedKey, false);
                boss.m_humanoid.SetHealth(boss.m_humanoid.GetMaxHealth() * RaisedHealth);
            }
        }

        // On the owner's game, every half second (CallerBoss): the raise, when its time has come
        internal static void Update(CallerBoss boss)
        {
            ZDO zdo = boss.m_nview.GetZDO();
            long raiseAt = zdo.GetLong(s_raiseAtKey);
            if (raiseAt == 0L || Now < raiseAt)
            {
                return;
            }
            zdo.Set(s_raiseAtKey, 0L);
            Raise(boss, zdo.GetVec3(s_fallenAtKey, boss.transform.position));
        }

        // A twin falls, on its owner's game: its twin learns of it, or, if its twin had already fallen, the fight is won
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Death
        {
            private static void Prefix(Character __instance)
            {
                CallerBoss boss = __instance.GetComponent<CallerBoss>();
                if (!boss || !boss.m_nview || !boss.m_nview.IsValid() || !boss.m_nview.IsOwner())
                {
                    return;
                }
                ZDO zdo = boss.m_nview.GetZDO();
                ZDOID twin = zdo.GetZDOID(s_twinKey);
                // Its countdown, if it was raising its twin, goes with it
                ZDOID rune = zdo.GetZDOID(s_runeKey);
                GameObject runeObject = rune.IsNone() ? null : ZNetScene.instance.FindInstance(rune);
                ZNetView runeView = runeObject ? runeObject.GetComponent<ZNetView>() : null;
                if (runeView && runeView.IsOwner())
                {
                    ZNetScene.instance.Destroy(runeObject);
                }
                if (twin.IsNone())
                {
                    TwinsLog.Event($"{TwinsLog.Name(boss)} had no twin (spawned alone): nothing more.");
                    return;
                }
                if (zdo.GetLong(s_raiseAtKey) != 0L || ZDOMan.instance.GetZDO(twin) == null)
                {
                    TwinsLog.Event(zdo.GetLong(s_raiseAtKey) != 0L ? $"{TwinsLog.Name(boss)} fell while raising its twin: won." : $"{TwinsLog.Name(boss)} fell, its twin already gone: won.");
                    Won(boss);
                    return;
                }
                TwinsLog.Event($"{TwinsLog.Name(boss)} fell: telling its twin.");
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, twin, FellRpc, boss.transform.position);
            }
        }

        // Its twin fell at `point`: on the game that runs this one, the countdown starts
        private static void Fell(CallerBoss boss, Vector3 point)
        {
            if (!boss.m_nview.IsOwner())
            {
                return;
            }
            string name = TwinsLog.Name(boss);
            if (!boss.m_humanoid || boss.m_humanoid.IsDead())
            {
                TwinsLog.Event($"{name} heard its twin fell, but is dead itself: no countdown.");
                return;
            }
            ZDO zdo = boss.m_nview.GetZDO();
            if (zdo.GetLong(s_raiseAtKey) != 0L)
            {
                TwinsLog.Event($"{name} heard its twin fell, but is already raising it.");
                return;
            }
            int players = 0;
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player && !player.IsDead() && TwinsAI.InReach(boss, player))
                {
                    players++;
                }
            }
            float delay = players <= 1 ? SoloDelay : RaiseDelay;
            Vector3 ground = LightningStrike.Ground(point);
            zdo.Set(s_fallenAtKey, ground);
            zdo.Set(s_raiseAtKey, Now + System.TimeSpan.FromSeconds(delay).Ticks);
            Element fallen = boss.Element == Element.Storm ? Element.Fire : Element.Storm;
            GameObject runePrefab = fallen == Element.Storm ? TwinsDuel.s_stormRune : TwinsDuel.s_fireRune;
            ElementRune rune = runePrefab ? ElementRune.Create(runePrefab, ground, delay, RuneRadius) : null;
            zdo.Set(s_runeKey, rune ? rune.GetComponent<ZNetView>().GetZDO().m_uid : ZDOID.None);
            Tell(boss.transform.position, fallen == Element.Storm ? "$msg_nhx_twin_fell_storm" : "$msg_nhx_twin_fell_fire");
            TwinsLog.Event($"{name} will raise its twin at {TwinsLog.At(ground)} in {delay:0} s ({players} player(s) in the fight); its rune {(rune ? "placed" : "missing")}.");
            TwinsVoices.Say(fallen == Element.Storm ? TwinsVoices.BlueBusy : TwinsVoices.RedBusy, ground, (fallen == Element.Storm ? "the Stormcaller" : "the Firecaller") + " fell");
            // Alone, it enters its second phase at once (its line after the fallen twin's: said after it, it waits)
            if (boss.m_phase)
            {
                boss.m_phase.TwinFell();
            }
        }

        // The fallen twin back, at half its health, where it fell, linked to this one
        private static void Raise(CallerBoss boss, Vector3 point)
        {
            Element fallen = boss.Element == Element.Storm ? Element.Fire : Element.Storm;
            GameObject prefab = ZNetScene.instance.GetPrefab(fallen == Element.Storm ? CallerBosses.StormName : CallerBosses.FireName);
            if (!prefab)
            {
                return;
            }
            Vector3 toward = boss.transform.position - point;
            toward.y = 0f;
            Quaternion rotation = toward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-toward) : Quaternion.identity;
            CallerBoss raised = Object.Instantiate(prefab, point, rotation).GetComponent<CallerBoss>();
            if (!raised || !raised.m_nview || !raised.m_nview.IsValid())
            {
                return;
            }
            raised.m_nview.GetZDO().Set(s_raisedKey, true);
            // As armored as its twin, in the same arena
            raised.m_nview.GetZDO().Set(CallerBoss.LevelKey, boss.m_nview.GetZDO().GetInt(CallerBoss.LevelKey, CallerBoss.DefaultLevel));
            if (Arena(boss, out Vector3 arena))
            {
                SetArena(raised, arena);
            }
            raised.m_nview.GetZDO().Set(s_sinceKey, boss.m_nview.GetZDO().GetLong(s_sinceKey));
            Link(boss, raised);
            // Its countdown rune strikes as it rises (the storm's lightning or Muspelheim's blast, harmless) and goes
            boss.m_nview.GetZDO().Set(s_runeKey, ZDOID.None);
            Tell(point, fallen == Element.Storm ? "$msg_nhx_twin_rises_storm" : "$msg_nhx_twin_rises_fire");
            TwinsLog.Event($"{TwinsLog.Name(boss)} raised the {(fallen == Element.Storm ? "Stormcaller" : "Firecaller")} at {TwinsLog.At(point)}, at half health.");
        }

        // The pair beaten: their drops (TwinsItems: 4 Stormhearts and 4 Surtr's Tears), where the last fell. Only here, not
        // at each twin's death, so a twin let rise again can't be farmed; and not through CharacterDrop, so CLLC's loot
        // multipliers leave them as the user set them
        private static void Won(CallerBoss boss)
        {
            Tell(boss.transform.position, "$msg_nhx_twins_vanquished");
            TwinsLog.Event($"The twins are beaten: drops at {TwinsLog.At(boss.transform.position)}.");
            TwinsVoices.Say(TwinsVoices.End, boss.transform.position, "both beaten");
            List<KeyValuePair<GameObject, int>> drops = new List<KeyValuePair<GameObject, int>>();
            if (TwinsItems.Stormheart)
            {
                drops.Add(new KeyValuePair<GameObject, int>(TwinsItems.Stormheart, TwinsItems.DropsPerKill));
            }
            if (TwinsItems.SurtrsTear)
            {
                drops.Add(new KeyValuePair<GameObject, int>(TwinsItems.SurtrsTear, TwinsItems.DropsPerKill));
            }
            if (drops.Count > 0)
            {
                CharacterDrop.DropItems(drops, boss.transform.position + Vector3.up, 1f, false);
            }
        }

        // A message in the middle of the screen of every player near `point`
        internal static void Tell(Vector3 point, string message)
        {
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player && Vector3.Distance(player.transform.position, point) <= TellRange)
                {
                    player.Message(MessageHud.MessageType.Center, message);
                }
            }
        }
    }
}
