using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // Sharing a staff drains both players (the fight's redesign, its change 8, agreed with the user 2026-11-17): with both
    // players on one staff, both twins chase whoever drew it last (TwinsAI) and the other shoots unharmed, so that state
    // is made short and costly. The user: "after 2 seconds of only one type of staff being in play, the players are
    // tethered with a new animation and color and they start taking damage every 2 seconds. The damage starts off at 2%
    // health and doubles with every hit [...] a bloody red magic effect tethers each player to the altar [...] No boss
    // immunity. The players get a voice line and they'll know something's wrong."
    // On each player's own game, for its player, every frame of their fight (TwinsDuel.Update):
    // - Only one staff in play (Shared): every living player in the fight who has drawn a staff and isn't judged has
    //   drawn the same one, and there are two of them at least (their ZDOs: TwinsDuel.ElementKey). A partner dead, back
    //   with nothing drawn yet, or judged doesn't count, and alone it never happens. Only at an altar's twins (Arena:
    //   devcommands' spawn makes twins with none)
    // - Off while a twin is down (TwinsDuel.Silence: both players must use its staff then), and once it rises, until its
    //   second phase has come (its 5 s transition, so its voice line is said first: the user, 2026-11-17, "enable the
    //   staff check after that, so that he can also say his voice line"), at most RiseWaitMax
    // - After Grace of it, the player is tethered: their ZDO says so (DrainedKey) and every game draws the tether from
    //   them to the twins' crystals over the altar (DrainTether); the twin whose staff nobody holds complains of it
    //   (TwinsVoices.Sharing), which also tells them which staff to draw. HitEvery later, and every HitEvery after, they
    //   lose FirstShare of their health, twice as much each time (2, 4, 8, 16, 32, 64%: the 6th kills unless they healed,
    //   the 7th whatever they did), as damage of no type, as the judgment's: past armor and resistances, no burning, no
    //   stagger. In god mode the game keeps them at 1 (Character.ApplyDamage)
    // - Both staves in play again (a swap), a twin down, a partner dead: the tether goes. The next time starts at
    //   FirstShare again, after a fresh Grace
    // The twins can still be hurt meanwhile: sharing is a short, costly choice. To see it alone, nhx_boss drain gives the
    // player a stand-in partner on the staff in their hand (StandIn), seen by this rule only; drawing the other ends it
    internal static class TwinsDrain
    {
        private const float Grace = 2f;
        private const float HitEvery = 2f;
        private const float FirstShare = 0.02f;
        // A risen twin's second phase is waited for at most this long (seconds; its transition is 5: TwinsPhase)
        private const float RiseWaitMax = 10f;

        // The player is tethered (every game draws it: DrainTether), and how many times they've been drained since
        // (each hit flares it)
        internal static readonly int DrainedKey = "nhx_twins_drained".GetStableHashCode();
        internal static readonly int DrainHitsKey = "nhx_twins_drain_hits".GetStableHashCode();

        // How long only one staff has been in play, and the tether, if there's one: its hits so far, the next one's
        // time, since when, its largest (a share of the health)
        private static float s_shared;
        private static bool s_tethered;
        private static int s_hits;
        private static float s_hitTimer;
        private static float s_tetheredAt;
        private static float s_largest;
        // nhx_boss drain's stand-in partner: the staff it holds (None: no stand-in)
        private static Element s_standIn;
        // A twin just risen, whose second phase the rule waits for, and since when
        private static Element s_risen;
        private static float s_risenAt;
        // The fight so far, for its summary (TwinsDuel.Summarize)
        private static int s_tethers;
        private static float s_tetheredFor;
        private static float s_fightLargest;

        // Every frame of the fight, for the local player (TwinsDuel.Update): `twin` the nearest of its fight, `drawn` the
        // last staff they drew, `silent` a fallen twin's element (TwinsDuel.Silence)
        internal static void Update(Player player, CallerBoss twin, Element drawn, Element silent, float dt)
        {
            if (s_standIn != Element.None && s_standIn != drawn)
            {
                TwinsLog.Event($"{TwinsLog.Name(player)} draws the other staff: the stand-in partner goes.");
                s_standIn = Element.None;
            }
            string off = Off(player, twin, silent);
            Element shared = off == null ? Shared(player, twin, drawn) : Element.None;
            if (shared == Element.None)
            {
                if (s_tethered)
                {
                    Untether(player, off ?? "both staves are in play again, or no one else holds that staff");
                }
                else if (s_shared > 0f)
                {
                    TwinsLog.Event($"{TwinsLog.Name(player)}: both staves in play again after {s_shared:0.0} s, within the grace.");
                }
                s_shared = 0f;
                return;
            }
            if (!s_tethered)
            {
                if (s_shared <= 0f)
                {
                    TwinsLog.Event($"{TwinsLog.Name(player)}: only the {Staff(shared)} in play ({Partners(player, twin, shared)}): tethered in {Grace:0} s unless one of them swaps.");
                }
                s_shared += dt;
                if (s_shared >= Grace)
                {
                    Tether(player, shared);
                }
                return;
            }
            s_hitTimer -= dt;
            if (s_hitTimer <= 0f)
            {
                s_hitTimer = HitEvery;
                Drain(player);
            }
        }

        // Why the rule is off now, or null
        private static string Off(Player player, CallerBoss twin, Element silent)
        {
            if (TwinsJudgment.Judged)
            {
                return "judged";
            }
            if (!TwinsBond.Arena(twin, out _))
            {
                return "twins with no altar";
            }
            if (silent != Element.None)
            {
                return "a twin is down";
            }
            if (s_risen != Element.None)
            {
                CallerBoss.NearestLiving(player.transform.position, s_risen, out CallerBoss risen);
                bool come = risen && risen.m_phase && risen.m_phase.Enraged && !risen.m_phase.Transforming;
                if (!come && Time.time - s_risenAt < RiseWaitMax)
                {
                    return "a risen twin's second phase is still coming";
                }
                TwinsLog.Event($"{TwinsLog.Name(player)}: the risen twin's second phase has {(come ? "come" : $"not come within {RiseWaitMax:0} s")}: sharing a staff is drained again.");
                s_risen = Element.None;
            }
            return null;
        }

        // The one staff in play, if only one is: every living player in this fight who has drawn a staff and isn't judged
        // has drawn the same, two of them at least, the stand-in counting as one. None otherwise
        private static Element Shared(Player player, CallerBoss twin, Element drawn)
        {
            if (drawn == Element.None)
            {
                return Element.None;
            }
            int holders = s_standIn == drawn ? 2 : 1;
            foreach (Player other in Player.GetAllPlayers())
            {
                Element theirs = Drawn(other, player, twin);
                if (theirs == Element.None)
                {
                    continue;
                }
                if (theirs != drawn)
                {
                    return Element.None;
                }
                holders++;
            }
            return holders >= 2 ? drawn : Element.None;
        }

        // The staff another player in this fight last drew, as their ZDO says: None for no one in it, dead, judged or
        // with nothing drawn
        private static Element Drawn(Player other, Player player, CallerBoss twin)
        {
            if (!other || other == player || other.IsDead() || !TwinsRing.Inside(twin, other.transform.position))
            {
                return Element.None;
            }
            ZDO zdo = other.m_nview && other.m_nview.IsValid() ? other.m_nview.GetZDO() : null;
            return zdo == null || zdo.GetBool(TwinsJudgment.JudgedKey) ? Element.None : (Element)zdo.GetInt(TwinsDuel.ElementKey);
        }

        // Who else holds `shared`, for the log
        private static string Partners(Player player, CallerBoss twin, Element shared)
        {
            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
            foreach (Player other in Player.GetAllPlayers())
            {
                if (Drawn(other, player, twin) == shared)
                {
                    names.Add(TwinsLog.Name(other));
                }
            }
            if (s_standIn == shared)
            {
                names.Add("a stand-in partner");
            }
            return "with " + string.Join(", ", names);
        }

        private static void Tether(Player player, Element shared)
        {
            s_tethered = true;
            s_hits = 0;
            s_hitTimer = HitEvery;
            s_tetheredAt = Time.time;
            s_largest = 0f;
            s_tethers++;
            SetDrained(player, true);
            TwinsLog.Event($"{TwinsLog.Name(player)} is tethered to the altar: only the {Staff(shared)} in play for {Grace:0} s. "
                + $"The drain begins in {HitEvery:0} s at {100f * FirstShare:0}% of their health, doubling every {HitEvery:0} s.");
            // The twin whose staff nobody holds complains of it
            CallerBoss.NearestLiving(player.transform.position, TwinsDuel.Other(shared), out CallerBoss leftOut);
            if (leftOut)
            {
                TwinsVoices.Sharing(leftOut, player);
            }
        }

        // A hit of the drain: FirstShare of the player's health, twice as much as the last
        private static void Drain(Player player)
        {
            float share = FirstShare * Mathf.Pow(2f, s_hits);
            s_hits++;
            s_largest = Mathf.Max(s_largest, share);
            s_fightLargest = Mathf.Max(s_fightLargest, share);
            float before = player.GetHealth();
            HitData hit = new HitData();
            hit.m_damage.m_damage = player.GetMaxHealth() * share;
            hit.m_point = player.GetCenterPoint();
            hit.m_dir = Vector3.down;
            hit.m_staggerMultiplier = 0f;
            hit.m_hitType = HitData.HitType.EnemyHit;
            player.ApplyDamage(hit, true, true);
            if (player.m_nview && player.m_nview.IsValid())
            {
                player.m_nview.GetZDO().Set(DrainHitsKey, player.m_nview.GetZDO().GetInt(DrainHitsKey) + 1);
            }
            TwinsLog.Event($"{TwinsLog.Name(player)} is drained ({s_hits}): {100f * share:0}% of their health, {before:0} -> {player.GetHealth():0}.");
        }

        private static void Untether(Player player, string why)
        {
            float lasted = Time.time - s_tetheredAt;
            s_tetheredFor += lasted;
            s_tethered = false;
            SetDrained(player, false);
            TwinsLog.Event($"{TwinsLog.Name(player)}'s tether goes ({why}) after {lasted:0.0} s and {s_hits} hit{(s_hits == 1 ? "" : "s")}"
                + $"{(s_hits > 0 ? $", the largest {100f * s_largest:0}% of their health" : "")}.");
            s_hits = 0;
        }

        private static void SetDrained(Player player, bool drained)
        {
            if (player && player.m_nview && player.m_nview.IsValid())
            {
                player.m_nview.GetZDO().Set(DrainedKey, drained);
            }
        }

        // A fallen twin has risen (TwinsDuel.Silence): the rule waits for its second phase to come
        internal static void Risen(Element element)
        {
            s_risen = element;
            s_risenAt = Time.time;
        }

        // nhx_boss drain: a stand-in partner on the staff in hand, or none if there's one already. What to tell the player
        internal static string StandIn(Player player)
        {
            if (s_standIn != Element.None)
            {
                s_standIn = Element.None;
                TwinsLog.Event($"{TwinsLog.Name(player)} sends the stand-in partner away.");
                return "The stand-in partner goes.";
            }
            if (!TwinsDuel.InFight || !TwinsRing.Fight(player.transform.position, out CallerBoss twin) || !TwinsBond.Arena(twin, out _))
            {
                return "Only in the twins' fight at an altar: inside its ring, while they live.";
            }
            Element drawn = TwinsDuel.Drawn;
            if (drawn == Element.None)
            {
                return "Draw a staff first: the stand-in partner holds the same.";
            }
            s_standIn = drawn;
            TwinsLog.Event($"{TwinsLog.Name(player)} calls a stand-in partner on the {Staff(drawn)}.");
            return $"A stand-in partner holds the {Staff(drawn)} with you: you're tethered in {Grace:0} s, until you draw the {Staff(TwinsDuel.Other(drawn))} "
                + "(nothing happens while a twin is down).";
        }

        // As the local player joins the fight (TwinsDuel)
        internal static void StartFight()
        {
            s_tethers = 0;
            s_tetheredFor = 0f;
            s_fightLargest = 0f;
        }

        // As they leave it (TwinsDuel.Leave): the tether, the stand-in and the wait for a risen twin go
        internal static void Reset(Player player)
        {
            if (s_tethered)
            {
                Untether(player, "the fight is over for them");
            }
            SetDrained(player, false);
            s_shared = 0f;
            s_standIn = Element.None;
            s_risen = Element.None;
        }

        // For the fight's summary (TwinsDuel.Summarize)
        internal static string Summary()
        {
            return s_tethers == 0 ? "never tethered to the altar"
                : $"tethered to the altar {s_tethers} time{(s_tethers == 1 ? "" : "s")}, {s_tetheredFor:0} s in all, the largest drain {100f * s_fightLargest:0}% of their health";
        }

        private static string Staff(Element element)
        {
            return element == Element.Storm ? "Staff of Storms" : "Wrath of Muspelheim";
        }
    }
}
