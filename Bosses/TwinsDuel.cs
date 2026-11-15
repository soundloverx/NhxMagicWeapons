using NhxMagicWeapons.Staves.StaffOfStorms;
using UnityEngine;
using Storms = NhxMagicWeapons.Staves.StaffOfStorms.Staff;
using Wrath = NhxMagicWeapons.Staves.WrathOfMuspelheim.Staff;

namespace NhxMagicWeapons.Bosses
{
    internal enum Element
    {
        None = 0,
        Storm = 1,
        Fire = 2,
    }

    // The twins' fight's core, as the user decided it (2026-11-14), on each player's own game for its player:
    // - The fight: from coming within EngageRadius of a living twin, or into the ring of an arena whose twins live, to
    //   being farther than LeaveRadius from both and out of the ring (or dying). As the twins count it (TwinsAI.InReach):
    //   at first the ring didn't count here, so a player in it but far from both twins was hunted with nothing building
    //   on them; the user, 2026-11-15: "entering the ring during a fight should place the player in combat, being then
    //   affected by anything that entails"
    // - A staff drawn: the element of the last of the two staves the player took in hand. Putting it away, drinking,
    //   eating, changes nothing ("The swap is whenever the other staff is equipped, not when the current one is
    //   unequipped or the player drinks a health potion"); drawing the other one is a swap. Kept in the player's ZDO with
    //   the moment, for the twins' targeting (TwinsAI: each hunts whoever drew its staff last)
    // - The element builds on the player from their first draw to the end of the fight, never pausing ("the player MUST
    //   swap staves to survive"): a stack each StackInterval (ElementCharge: Static or Scorch, slowing eitr's or stamina's
    //   return), and at the last the element's twin smites them (Overload), unavoidable, and it starts again; each overload
    //   that comes again without a swap in between hits OverloadGrowth times harder than the last (Claude's proposal,
    //   2026-11-14, taken by the user: ignoring it once is survivable, not forever). Every game sees the charge on them
    //   (ChargeLook, from their ZDO)
    // - A swap clears the old element and calls the new staff's twin (CallerBoss.RPC_Punish): it smites the player at once
    //   ("dealing moderate damage and removing the accumulated lightning debuffs, then comet towards that player and
    //   proceed to following them instead"). The first draw of a fight isn't a swap: no smite
    internal static class TwinsDuel
    {
        internal const float EngageRadius = 50f;
        private const float LeaveRadius = 80f;
        private const float StackInterval = 5f;
        internal const int MaxStacks = 5;
        // Each stack slows eitr's (Static) or stamina's (Scorch) return by this much
        private const float RegenCut = 0.2f;
        // The smites: the swap's and the full charge's, moderate (150 is 56 after 100 armor), the full charge's half again
        // as much each time it comes again without a swap (85, 127, 190...); on the ground, this wide (their look only:
        // they hit the one player)
        internal const float SwapDamage = 150f;
        private const float OverloadDamage = 150f;
        private const float OverloadGrowth = 1.5f;
        internal const float SmiteRadius = 1.5f;
        private const float OverloadRadius = 2.2f;
        // The full charge goes this long after its smite (it was for the smite to find the player weakened; kept, short,
        // so the count shows 5 as it strikes)
        private const float ClearDelay = 0.3f;
        // How often every player near a twin is given the charge's look (ChargeLook)
        private const float LookInterval = 0.5f;

        internal static readonly int ElementKey = "nhx_twins_element".GetStableHashCode();
        internal static readonly int DrawnKey = "nhx_twins_drawn".GetStableHashCode();
        internal static readonly int StacksKey = "nhx_twins_stacks".GetStableHashCode();
        private static readonly int s_staticHash = ElementCharge.StaticName.GetStableHashCode();
        private static readonly int s_scorchHash = ElementCharge.ScorchName.GetStableHashCode();

        // The runes the smites are shown with (BossAttacks)
        internal static GameObject s_stormRune;
        internal static GameObject s_fireRune;

        private static bool s_inFight;
        private static Element s_drawn;
        private static float s_stackTimer;
        private static Element s_clearing;
        private static float s_clearTimer;
        // Overloads since the last swap
        private static int s_overloads;
        private static float s_lookTimer;

        internal static void Add()
        {
            ElementCharge.Add(ElementCharge.StaticName, "$se_nhx_static", "$se_nhx_static_tooltip", HitData.DamageType.Lightning, Storms.Icon, MaxStacks, RegenCut);
            ElementCharge.Add(ElementCharge.ScorchName, "$se_nhx_scorch", "$se_nhx_scorch_tooltip", HitData.DamageType.Fire, Wrath.Icon, MaxStacks, RegenCut);
        }

        // Every player near a twin, on this game, shows their charge (ChargeLook reads it from their ZDO)
        private static void GiveLooks(float dt)
        {
            s_lookTimer -= dt;
            if (s_lookTimer > 0f)
            {
                return;
            }
            s_lookTimer = LookInterval;
            if (CallerBoss.All.Count == 0 || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                return;
            }
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player && !player.GetComponent<ChargeLook>())
                {
                    player.gameObject.AddComponent<ChargeLook>();
                }
            }
        }

        internal static Element ElementOf(ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return Element.None;
            }
            string name = item.m_shared.m_name;
            return name == Storms.NameKey ? Element.Storm : name == Wrath.NameKey ? Element.Fire : Element.None;
        }

        // Every frame (Plugin.Update), for the local player
        internal static void Update(float dt)
        {
            GiveLooks(dt);
            Player player = Player.m_localPlayer;
            if (!player || player.IsDead() || !player.m_nview || !player.m_nview.IsValid())
            {
                if (s_inFight)
                {
                    Leave(player);
                }
                return;
            }
            float nearest = CallerBoss.NearestLiving(player.transform.position, Element.None, out _);
            bool inRing = InRing(player.transform.position);
            if (!s_inFight)
            {
                if (nearest > EngageRadius && !inRing)
                {
                    return;
                }
                s_inFight = true;
                SetDrawn(player, Element.None);
                TwinsLog.Event($"{TwinsLog.Name(player)} joins the fight ({nearest:0} m from the nearest twin{(inRing ? ", inside the ring" : "")}).");
            }
            // A judged player stays in it until they die, however far they run (while the twins live); anyone in the ring
            // stays in it, however far the twins are (the ring is wider than LeaveRadius from edge to edge)
            else if (nearest > LeaveRadius && !inRing && (!TwinsJudgment.Judged || float.IsInfinity(nearest)))
            {
                Leave(player);
                return;
            }

            // Anything else in hand, and the elements judge (TwinsJudgment)
            TwinsJudgment.Update(player, dt);
            // The twins' second phase: the burning way, the arcs between players (TwinsPhase)
            if (!TwinsJudgment.Judged)
            {
                TwinsPhase.UpdatePlayer(player, dt);
            }
            Element held = ElementOf(player.m_rightItem);
            if (held != Element.None && held != s_drawn)
            {
                Draw(player, held);
            }
            if (s_clearing != Element.None)
            {
                s_clearTimer -= dt;
                if (s_clearTimer <= 0f)
                {
                    Clear(player, s_clearing);
                    s_clearing = Element.None;
                }
            }
            if (s_drawn != Element.None)
            {
                s_stackTimer -= dt;
                if (s_stackTimer <= 0f)
                {
                    s_stackTimer += StackInterval;
                    Stack(player, s_drawn);
                }
            }
        }

        // Whether `point` is within the ring of an arena whose twins live (an altar's: TwinsBond.Arena), its edge's grace
        // included, as the twins count it (TwinsAI.InReach)
        internal static bool InRing(Vector3 point)
        {
            foreach (CallerBoss boss in CallerBoss.All)
            {
                if (boss && boss.m_humanoid && !boss.m_humanoid.IsDead() && TwinsBond.Arena(boss, out Vector3 arena)
                    && Utils.DistanceXZ(point, arena) <= TwinsAltars.ArenaRadius + TwinsRules.ArenaGrace)
                {
                    return true;
                }
            }
            return false;
        }

        private static void Draw(Player player, Element held)
        {
            Element old = s_drawn;
            SetDrawn(player, held);
            s_stackTimer = StackInterval;
            if (old == Element.None)
            {
                TwinsLog.Event($"{TwinsLog.Name(player)} draws the {StaffName(held)} (first draw).");
                return;
            }
            // A swap: the old element's charge goes (and its overloads' growth), and the new staff's twin answers
            Clear(player, old);
            if (s_clearing == old)
            {
                s_clearing = Element.None;
            }
            s_overloads = 0;
            CallerBoss.NearestLiving(player.transform.position, held, out CallerBoss twin);
            TwinsLog.Event($"{TwinsLog.Name(player)} swaps to the {StaffName(held)}: {(twin ? "the " + TwinsLog.Name(twin) + " answers" : "no living twin of it to answer")}.");
            if (twin)
            {
                twin.m_nview.InvokeRPC(CallerBoss.PunishRpc, player.GetZDOID());
            }
        }

        private static string StaffName(Element element)
        {
            return element == Element.Storm ? "Staff of Storms" : "Wrath of Muspelheim";
        }

        private static void SetDrawn(Player player, Element element)
        {
            s_drawn = element;
            ZDO zdo = player.m_nview.GetZDO();
            zdo.Set(ElementKey, (int)element);
            zdo.Set(DrawnKey, ZNet.instance ? ZNet.instance.GetTime().Ticks : 0L);
        }

        private static void Stack(Player player, Element element)
        {
            SEMan seman = player.GetSEMan();
            int hash = ChargeHash(element);
            if (!(seman.GetStatusEffect(hash) is ElementCharge charge))
            {
                seman.AddStatusEffect(hash);
                SetStacks(player, 1);
                return;
            }
            if (s_clearing == element)
            {
                return;
            }
            bool full = charge.AddStack();
            SetStacks(player, charge.Stacks);
            if (!full)
            {
                return;
            }
            // Full: the element's twin smites them, harder each time without a swap, then it starts again
            CallerBoss.NearestLiving(player.transform.position, element, out CallerBoss twin);
            float smite = OverloadDamage * Mathf.Pow(OverloadGrowth, s_overloads);
            TwinsLog.Event($"{TwinsLog.Name(player)} overloads with {(element == Element.Storm ? "Static" : "Scorch")} ({s_overloads + 1} since the last swap): a smite of {smite:0} from the {(twin ? TwinsLog.Name(twin) : "elements")}.");
            Smite(element, player, twin ? twin.m_humanoid : null, smite, OverloadRadius);
            s_overloads++;
            player.Message(MessageHud.MessageType.Center, element == Element.Storm ? "$msg_nhx_overload_storm" : "$msg_nhx_overload_fire");
            s_clearing = element;
            s_clearTimer = ClearDelay;
        }

        private static void Clear(Player player, Element element)
        {
            if (player && element != Element.None)
            {
                player.GetSEMan().RemoveStatusEffect(ChargeHash(element), true);
                SetStacks(player, 0);
            }
        }

        // For every game to see (ChargeLook)
        private static void SetStacks(Player player, int stacks)
        {
            if (player && player.m_nview && player.m_nview.IsValid())
            {
                player.m_nview.GetZDO().Set(StacksKey, stacks);
            }
        }

        private static void Leave(Player player)
        {
            TwinsLog.Event($"{TwinsLog.Name(player)} leaves the fight{(player && player.IsDead() ? " (dead)" : "")}.");
            s_inFight = false;
            s_clearing = Element.None;
            s_overloads = 0;
            TwinsJudgment.Reset(player);
            if (player && player.m_nview && player.m_nview.IsValid())
            {
                Clear(player, Element.Storm);
                Clear(player, Element.Fire);
                SetDrawn(player, Element.None);
            }
            s_drawn = Element.None;
        }

        private static int ChargeHash(Element element)
        {
            return element == Element.Storm ? s_staticHash : s_scorchHash;
        }

        // A twin's smite on one player, unavoidable (no dodging or blocking it): its rune flashing under them, the storm's
        // strike or Muspelheim's blast on it (seen by every game), and `damage` of the element, from `attacker` if there's
        // one. On any game: the hit goes to the player's own
        internal static void Smite(Element element, Character victim, Character attacker, float damage, float radius)
        {
            if (!victim)
            {
                return;
            }
            GameObject rune = element == Element.Storm ? s_stormRune : s_fireRune;
            if (rune)
            {
                ElementRune.Create(rune, LightningStrike.Ground(victim.transform.position), 0f, radius);
            }
            HitData hit = new HitData();
            if (element == Element.Storm)
            {
                hit.m_damage.m_lightning = damage;
            }
            else
            {
                hit.m_damage.m_fire = damage;
            }
            hit.m_point = victim.GetCenterPoint();
            hit.m_dir = Vector3.down;
            hit.m_blockable = false;
            hit.m_dodgeable = false;
            hit.m_ranged = true;
            // Lightning staggers as blunt, slash and pierce do (HitData.GetTotalStaggerDamage), fire doesn't: unavoidable,
            // a smite doesn't also stun (the user, 2026-11-14: "The swap to lightning stuns my character")
            hit.m_staggerMultiplier = 0f;
            hit.m_hitType = HitData.HitType.EnemyHit;
            if (attacker)
            {
                hit.SetAttacker(attacker);
            }
            victim.Damage(hit);
        }
    }
}
