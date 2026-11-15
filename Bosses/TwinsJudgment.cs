using NhxMagicWeapons.Staves.StaffOfStorms;
using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;
using UnityEngine;
using Wrath = NhxMagicWeapons.Staves.WrathOfMuspelheim.Staff;

namespace NhxMagicWeapons.Bosses
{
    // The elements' judgment (the user, 2026-11-14: "The players should be allowed to be unarmed, but if they equip any
    // other weapon, both bosses should turn to them and channel their powerful beams until the player dies. 'The elements
    // have judged you and found you wanting' [...] should appear on the screen"; then: "The judgement is final, if the
    // elements deem you unworthy, you die. Simple as that. And yes, only the 2 staves are allowed, nothing else", with the
    // 3 s of warning Claude suggested, as a weapon picked up with empty hands is equipped by itself).
    // On the player's own game, while they're in the fight (TwinsDuel): anything in their hands but the two staves (a
    // weapon, a tool, a torch, a shield; food and drinks are never held) starts the warning; put away in time, nothing
    // happens. Still held after Warning seconds: judged, for good. Their ZDO says so (JudgedKey), and the twins turn to them
    // and stand, channelling (TwinsAI: Role.Judging); their game draws both beams from the twins' hands (the Staff of
    // Storms' bolt, the Wrath of Muspelheim's flames, networked) and takes TickShare of their health each Tick, past
    // armor, blocking and dodging (Character.ApplyDamage), until they die. In god mode they don't: the game keeps them at 1
    internal static class TwinsJudgment
    {
        private const float Warning = 3f;
        private const float Tick = 0.5f;
        private const float TickShare = 0.25f;
        // The beams are drawn again this often (the bolt shows 0.3 s, the flames a little longer), at the staves' level 20
        private const float BeamInterval = 0.2f;
        private const int BeamLevel = 20;

        internal static readonly int JudgedKey = "nhx_twins_judged".GetStableHashCode();

        private static float s_warning = -1f;
        private static bool s_judged;
        private static float s_tickTimer;
        private static float s_beamTimer;
        // Whether the player has been inside the twins' arena (an altar's ring) in this fight: leaving it then is judged
        // as a forbidden weapon is (Claude's suggestion, 2026-11-14, taken with the altar: "the rune around the arena
        // [...] would solve everything"); coming in from outside isn't (a player back from the dead walks in freely)
        private static bool s_entered;

        // Judged: until death, wherever they go (TwinsDuel keeps them in the fight)
        internal static bool Judged => s_judged;

        // What's in their hands, for the log
        private static string Held(Player player)
        {
            string right = player.m_rightItem != null ? Localization.instance.Localize(player.m_rightItem.m_shared.m_name) : "nothing";
            return player.m_leftItem != null ? right + " and " + Localization.instance.Localize(player.m_leftItem.m_shared.m_name) : right;
        }

        // Every frame of the fight, for the local player (TwinsDuel.Update); Reset when it ends
        internal static void Update(Player player, float dt)
        {
            if (s_judged)
            {
                Sentence(player, dt);
                return;
            }
            bool fled = Fled(player);
            if (!fled && !Forbidden(player))
            {
                s_warning = -1f;
                return;
            }
            if (s_warning < 0f)
            {
                s_warning = Warning;
                player.Message(MessageHud.MessageType.Center, fled ? "$msg_nhx_arena_warning" : "$msg_nhx_judgment_warning");
                TwinsLog.Event($"{TwinsLog.Name(player)} is warned: {(fled ? "out of the ring" : "holding " + Held(player))}.");
            }
            s_warning -= dt;
            if (s_warning > 0f)
            {
                return;
            }
            s_judged = true;
            s_tickTimer = Tick;
            s_beamTimer = 0f;
            player.m_nview.GetZDO().Set(JudgedKey, true);
            player.Message(MessageHud.MessageType.Center, "$msg_nhx_judged");
            TwinsLog.Event($"{TwinsLog.Name(player)} is judged{(fled ? " (out of the ring)" : ", holding " + Held(player))}.");
            foreach (Player other in Player.GetAllPlayers())
            {
                if (other && other != player && Vector3.Distance(other.transform.position, player.transform.position) <= TwinsBond.TellRange)
                {
                    other.Message(MessageHud.MessageType.Center, player.GetPlayerName() + " $msg_nhx_judged_other");
                }
            }
        }

        // Out of the arena of the nearest twins after having been in it
        private static bool Fled(Player player)
        {
            CallerBoss.NearestLiving(player.transform.position, Element.None, out CallerBoss twin);
            if (!TwinsBond.Arena(twin, out Vector3 arena))
            {
                return false;
            }
            bool inside = Utils.DistanceXZ(player.transform.position, arena) <= TwinsAltars.ArenaRadius + TwinsRules.ArenaGrace;
            if (inside)
            {
                s_entered = true;
            }
            return s_entered && !inside;
        }

        internal static void Reset(Player player)
        {
            s_warning = -1f;
            s_judged = false;
            s_entered = false;
            if (player && player.m_nview && player.m_nview.IsValid())
            {
                player.m_nview.GetZDO().Set(JudgedKey, false);
            }
        }

        // Anything in hand but the two staves
        private static bool Forbidden(Player player)
        {
            ItemDrop.ItemData right = player.m_rightItem;
            return (right != null && TwinsDuel.ElementOf(right) == Element.None) || player.m_leftItem != null;
        }

        private static void Sentence(Player player, float dt)
        {
            CallerBoss.NearestLiving(player.transform.position, Element.Storm, out CallerBoss storm);
            CallerBoss.NearestLiving(player.transform.position, Element.Fire, out CallerBoss fire);
            Vector3 target = player.GetCenterPoint();

            s_beamTimer -= dt;
            if (s_beamTimer <= 0f)
            {
                s_beamTimer = BeamInterval;
                if (storm && LightningStep.s_bolt)
                {
                    LightningBolt.Arc(LightningStep.s_bolt, storm.m_humanoid, BossHands.Between(storm.m_humanoid, storm.transform.position), target, BeamLevel);
                }
                if (fire && Wrath.Flames)
                {
                    Vector3 hands = BossHands.Between(fire.m_humanoid, fire.transform.position);
                    Vector3 toward = target - hands;
                    if (toward.sqrMagnitude > 0.01f)
                    {
                        FlameLook.SetReach(Object.Instantiate(Wrath.Flames, hands, Quaternion.LookRotation(toward)), toward.magnitude);
                    }
                }
            }

            s_tickTimer -= dt;
            if (s_tickTimer > 0f)
            {
                return;
            }
            s_tickTimer = Tick;
            HitData hit = new HitData();
            hit.m_damage.m_damage = player.GetMaxHealth() * TickShare;
            hit.m_point = target;
            hit.m_dir = Vector3.down;
            hit.m_staggerMultiplier = 0f;
            hit.m_hitType = HitData.HitType.EnemyHit;
            Character judge = storm ? storm.m_humanoid : fire ? fire.m_humanoid : null;
            if (judge)
            {
                hit.SetAttacker(judge);
            }
            player.ApplyDamage(hit, true, true);
        }
    }
}
