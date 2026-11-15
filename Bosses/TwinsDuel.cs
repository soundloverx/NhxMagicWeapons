using System.Collections.Generic;
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
    //   return), and from MaxStacks on the element's twin smites them with each one (Stack), unavoidable; the charge keeps
    //   building (until 2026-11-15 it started again after the smite; the user: "the 5 stacks shouldn't disappear when the
    //   player it's hit, they should continue to build while they still hold the same staff and then they will get a new
    //   smite for every additional stack (6,7 etc)"). Each smite that comes again without a swap in between hits
    //   OverloadGrowth times harder than the last (Claude's proposal, 2026-11-14, taken by the user), and leaves them a
    //   step weaker to the element for a minute (SmiteWeakness: Stormstruck, Flamestruck; the user, 2026-11-15), which a
    //   swap doesn't clear. Every game sees the charge on them (ChargeLook, from their ZDO)
    // - A swap calls the new staff's twin (CallerBoss.RPC_Punish): it smites the player at once ("dealing moderate damage
    //   and removing the accumulated lightning debuffs, then comet towards that player and proceed to following them
    //   instead"). The first draw of a fight isn't a swap: no smite. The old element's charge is held at HeldStacks, up or
    //   down to it, slowing nothing, until the new staff's own reaches HeldStacks, and then goes; taking the old staff up
    //   again before that carries on from HeldStacks. Until 2026-11-15 a swap cleared it at once, which let a player stay
    //   on one staff by swapping away and straight back; the user's rule then: "when swapping the staff, set the previous
    //   staff's stack at 4 (from any value it was at, either 1 or > 5) and keep it at 4 until the current stack reaches 4"
    //   (it slowing nothing while held is Claude's pick: otherwise eitr or stamina would be slowed most of the fight)
    internal static class TwinsDuel
    {
        internal const float EngageRadius = 50f;
        private const float LeaveRadius = 80f;
        private const float StackInterval = 5f;
        internal const int MaxStacks = 5;
        // A staff's charge put down is held at this, and goes once the other's reaches it: one short of a smite
        internal const int HeldStacks = MaxStacks - 1;
        // Each stack slows eitr's (Static) or stamina's (Scorch) return by this much
        private const float RegenCut = 0.2f;
        // The smites: the swap's and the full charge's, moderate (150 is 56 after 100 armor), the full charge's half again
        // as much each time it comes again without a swap (150, 225, 338...: one each StackInterval now, from the fifth
        // stack), before what SmiteWeakness adds; on the ground, this wide (their look only: they hit the one player)
        internal const float SwapDamage = 150f;
        private const float OverloadDamage = 150f;
        private const float OverloadGrowth = 1.5f;
        internal const float SmiteRadius = 1.5f;
        private const float OverloadRadius = 2.2f;
        // How often every player near a twin is given the charge's look (ChargeLook)
        private const float LookInterval = 0.5f;

        internal static readonly int ElementKey = "nhx_twins_element".GetStableHashCode();
        internal static readonly int DrawnKey = "nhx_twins_drawn".GetStableHashCode();
        internal static readonly int StacksKey = "nhx_twins_stacks".GetStableHashCode();
        private static readonly int s_staticHash = ElementCharge.StaticName.GetStableHashCode();
        private static readonly int s_scorchHash = ElementCharge.ScorchName.GetStableHashCode();
        private static readonly int s_stormstruckHash = SmiteWeakness.StormName.GetStableHashCode();
        private static readonly int s_flamestruckHash = SmiteWeakness.FireName.GetStableHashCode();

        // The runes the smites are shown with (BossAttacks)
        internal static GameObject s_stormRune;
        internal static GameObject s_fireRune;

        private static bool s_inFight;
        private static Element s_drawn;
        private static float s_stackTimer;
        // Overloads since the last swap
        private static int s_overloads;
        private static float s_lookTimer;
        private static float s_drawnAt;

        // The fight so far, for its summary in the log when the player leaves it (Summarize; the user, 2026-11-15: "add
        // some logs so that we can properly assess if it worked after the fight"). By Element: none, storm, fire
        private static readonly Element[] s_elements = { Element.Storm, Element.Fire };
        private static string s_playerName = "";
        private static float s_fightTime;
        private static readonly float[] s_timeWith = new float[3];
        private static int s_swaps;
        private static int s_takenBack;
        private static int s_smites;
        private static float s_smiteDamage;
        private static readonly int[] s_mostStacks = new int[3];
        private static readonly int[] s_mostSteps = new int[3];

        internal static void Add()
        {
            ElementCharge.Add(ElementCharge.StaticName, "$se_nhx_static", "$se_nhx_static_tooltip", HitData.DamageType.Lightning, Storms.Icon, MaxStacks, RegenCut);
            ElementCharge.Add(ElementCharge.ScorchName, "$se_nhx_scorch", "$se_nhx_scorch_tooltip", HitData.DamageType.Fire, Wrath.Icon, MaxStacks, RegenCut);
            // With the twins' hearts' icons (their drops: TwinsItems), so they don't look like the charges beside them
            SmiteWeakness.Add(SmiteWeakness.StormName, "$se_nhx_stormstruck", "$se_nhx_stormstruck_tooltip", HitData.DamageType.Lightning, IconOf(TwinsItems.Stormheart, Storms.Icon));
            SmiteWeakness.Add(SmiteWeakness.FireName, "$se_nhx_flamestruck", "$se_nhx_flamestruck_tooltip", HitData.DamageType.Fire, IconOf(TwinsItems.SurtrsTear, Wrath.Icon));
        }

        private static Sprite IconOf(GameObject item, Sprite otherwise)
        {
            ItemDrop drop = item ? item.GetComponent<ItemDrop>() : null;
            Sprite[] icons = drop ? drop.m_itemData.m_shared.m_icons : null;
            return icons != null && icons.Length > 0 && icons[0] ? icons[0] : otherwise;
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
                StartStats(player);
                TwinsLog.Event($"{TwinsLog.Name(player)} joins the fight ({nearest:0} m from the nearest twin{(inRing ? ", inside the ring" : "")}).");
            }
            // A judged player stays in it until they die, however far they run (while the twins live); anyone in the ring
            // stays in it, however far the twins are (the ring is wider than LeaveRadius from edge to edge)
            else if (nearest > LeaveRadius && !inRing && (!TwinsJudgment.Judged || float.IsInfinity(nearest)))
            {
                Leave(player);
                return;
            }
            s_fightTime += dt;
            s_timeWith[(int)s_drawn] += dt;

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
            float heldFor = Time.time - s_drawnAt;
            s_drawnAt = Time.time;
            if (old == Element.None)
            {
                TwinsLog.Event($"{TwinsLog.Name(player)} draws the {StaffName(held)} (first draw).");
                return;
            }
            // A swap: the new staff's charge, if it was held, builds on from where it is (a swap straight back gains
            // nothing); the old one is held until the new one reaches HeldStacks, or goes at once if it already has. The
            // overloads' growth goes (not their SmiteWeakness, which runs out on its own), and the new staff's twin answers
            ElementCharge leaving = Charge(player, old);
            int was = leaving ? leaving.Stacks : 0;
            ElementCharge taken = Charge(player, held);
            if (taken)
            {
                taken.TakeUp();
            }
            SetStacks(player, taken ? taken.Stacks : 0);
            bool takenOver = taken && taken.Stacks >= HeldStacks;
            if (takenOver)
            {
                Clear(player, old);
            }
            else
            {
                Hold(player, old);
            }
            s_overloads = 0;
            s_swaps++;
            if (taken)
            {
                s_takenBack++;
            }
            CallerBoss.NearestLiving(player.transform.position, held, out CallerBoss twin);
            TwinsLog.Event($"{TwinsLog.Name(player)} swaps to the {StaffName(held)} after {heldFor:0} s on the {StaffName(old)}: "
                + $"{ChargeName(old)} {was} -> {(takenOver ? "gone" : $"held at {HeldStacks}")}, "
                + $"{ChargeName(held)} {(taken ? $"taken up again at {taken.Stacks}" : "starts from none")}; "
                + $"{(twin ? "the " + TwinsLog.Name(twin) + " answers" : "no living twin of it to answer")}. Now: {Describe(player, true)}.");
            if (twin)
            {
                twin.m_nview.InvokeRPC(CallerBoss.PunishRpc, player.GetZDOID());
            }
        }

        private static string StaffName(Element element)
        {
            return element == Element.Storm ? "Staff of Storms" : "Wrath of Muspelheim";
        }

        private static string ChargeName(Element element)
        {
            return element == Element.Storm ? "Static" : "Scorch";
        }

        private static Element Other(Element element)
        {
            return element == Element.Storm ? Element.Fire : Element.Storm;
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
            ElementCharge charge = Charge(player, element);
            if (!charge)
            {
                player.GetSEMan().AddStatusEffect(ChargeHash(element));
                SetStacks(player, 1);
                LogStack(player, element, 1, null);
                return;
            }
            bool full = charge.AddStack();
            SetStacks(player, charge.Stacks);
            // The charge in hand has taken over from the one held
            Element other = Other(element);
            string tookOver = null;
            if (charge.Stacks >= HeldStacks && Charge(player, other))
            {
                tookOver = ChargeName(other);
                Clear(player, other);
            }
            LogStack(player, element, charge.Stacks, tookOver);
            if (!full)
            {
                return;
            }
            // Full, and every stack after: the element's twin smites them, harder each time without a swap, and leaves
            // them weaker to it for a while (after the hit: this smite hits as the last one left them)
            CallerBoss.NearestLiving(player.transform.position, element, out CallerBoss twin);
            float smite = OverloadDamage * Mathf.Pow(OverloadGrowth, s_overloads);
            TwinsLog.Event($"{TwinsLog.Name(player)} overloads with {ChargeName(element)} at {charge.Stacks} ({s_overloads + 1} since the last swap): a smite of {smite:0} from the {(twin ? TwinsLog.Name(twin) : "elements")}.");
            Smite(element, player, twin ? twin.m_humanoid : null, smite, OverloadRadius);
            s_overloads++;
            s_smites++;
            s_smiteDamage += smite;
            if (!player.IsDead())
            {
                Weaken(player, element);
            }
            player.Message(MessageHud.MessageType.Center, element == Element.Storm ? "$msg_nhx_overload_storm" : "$msg_nhx_overload_fire");
        }

        // The overload smite's weakness (SmiteWeakness): on, or a step more and its minute again
        private static void Weaken(Player player, Element element)
        {
            SEMan seman = player.GetSEMan();
            int hash = WeaknessHash(element);
            HitData.DamageType type = element == Element.Storm ? HitData.DamageType.Lightning : HitData.DamageType.Fire;
            HitData.DamageModifier before = player.GetDamageModifier(type);
            bool had = seman.GetStatusEffect(hash);
            seman.AddStatusEffect(hash, true);
            int steps = seman.GetStatusEffect(hash) is SmiteWeakness weakness ? weakness.Steps : 0;
            s_mostSteps[(int)element] = Mathf.Max(s_mostSteps[(int)element], steps);
            TwinsLog.Event($"{TwinsLog.Name(player)} is {WeaknessName(element)}{(had ? " again" : "")}: {steps} step{(steps == 1 ? "" : "s")} "
                + $"for {SmiteWeakness.Duration:0} s, {type.ToString().ToLowerInvariant()} resistance {before} -> {player.GetDamageModifier(type)}.");
        }

        // Each stack in the log: what the fight has put on the player, and their regen with every effect on them (the
        // charges', the armor's, meads'...)
        private static void LogStack(Player player, Element element, int stacks, string tookOver)
        {
            s_mostStacks[(int)element] = Mathf.Max(s_mostStacks[(int)element], stacks);
            SEMan seman = player.GetSEMan();
            float eitr = 1f;
            seman.ModifyEitrRegen(ref eitr);
            float stamina = 1f;
            seman.ModifyStaminaRegen(ref stamina);
            TwinsLog.Event($"{TwinsLog.Name(player)}'s {ChargeName(element)} reaches {stacks}{(tookOver != null ? $", taking over: their {tookOver} goes" : "")}. "
                + $"Now: {Describe(player, true)}; regen with every effect: eitr x{eitr:0.00}, stamina x{stamina:0.00}.");
        }

        // "Static 6 in hand (eitr regen x0.00), Scorch 4 held (no effect), Stormstruck 2 (48 s left)": what the fight has
        // put on the player, for the log (TwinsLog's hit lines too, without the regen)
        internal static string Describe(Player player, bool regen)
        {
            if (!player)
            {
                return "nothing";
            }
            List<string> parts = new List<string>();
            foreach (Element element in s_elements)
            {
                ElementCharge charge = Charge(player, element);
                if (charge)
                {
                    string effect = !regen ? "" : charge.Held ? " (no effect)" : $" ({(element == Element.Storm ? "eitr" : "stamina")} regen x{charge.RegenLeft:0.00})";
                    parts.Add($"{ChargeName(element)} {charge.Stacks} {(charge.Held ? "held" : "in hand")}{effect}");
                }
            }
            foreach (Element element in s_elements)
            {
                if (player.GetSEMan().GetStatusEffect(WeaknessHash(element)) is SmiteWeakness weakness)
                {
                    parts.Add($"{WeaknessName(element)} {weakness.Steps} ({weakness.GetRemaningTime():0} s left)");
                }
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "nothing";
        }

        private static string WeaknessName(Element element)
        {
            return element == Element.Storm ? "Stormstruck" : "Flamestruck";
        }

        private static int WeaknessHash(Element element)
        {
            return element == Element.Storm ? s_stormstruckHash : s_flamestruckHash;
        }

        private static void StartStats(Player player)
        {
            s_playerName = TwinsLog.Name(player);
            s_drawnAt = Time.time;
            s_fightTime = 0f;
            System.Array.Clear(s_timeWith, 0, s_timeWith.Length);
            s_swaps = 0;
            s_takenBack = 0;
            s_smites = 0;
            s_smiteDamage = 0f;
            System.Array.Clear(s_mostStacks, 0, s_mostStacks.Length);
            System.Array.Clear(s_mostSteps, 0, s_mostSteps.Length);
            TwinsLog.StartFight();
        }

        // The fight in a few lines, as the player leaves it (or dies)
        private static void Summarize()
        {
            string name = s_playerName;
            TwinsLog.Event($"Fight summary for {name}: {Clock(s_fightTime)} in the fight; the Staff of Storms the last drawn for "
                + $"{Clock(s_timeWith[(int)Element.Storm])}, the Wrath of Muspelheim for {Clock(s_timeWith[(int)Element.Fire])}, "
                + $"no staff drawn yet for {Clock(s_timeWith[(int)Element.None])}.");
            TwinsLog.Event($"Fight summary for {name}: {s_swaps} swaps, {s_takenBack} of them back to a staff whose charge was still held; "
                + $"most stacks: Static {s_mostStacks[(int)Element.Storm]}, Scorch {s_mostStacks[(int)Element.Fire]}; "
                + $"{s_smites} overload smites, {s_smiteDamage:0} before resistances and armor; "
                + $"most steps: Stormstruck {s_mostSteps[(int)Element.Storm]}, Flamestruck {s_mostSteps[(int)Element.Fire]}.");
            TwinsLog.Event($"Fight summary for {name}: {TwinsLog.FightDamage()}.");
        }

        private static string Clock(float seconds)
        {
            int whole = Mathf.FloorToInt(seconds);
            return $"{whole / 60}:{whole % 60:00}";
        }

        // The charge of `element` on the player, if there's one
        private static ElementCharge Charge(Player player, Element element)
        {
            return player.GetSEMan().GetStatusEffect(ChargeHash(element)) as ElementCharge;
        }

        // The staff put down: its charge held at HeldStacks, from whatever it was (none yet too)
        private static void Hold(Player player, Element element)
        {
            ElementCharge charge = Charge(player, element);
            if (!charge)
            {
                charge = player.GetSEMan().AddStatusEffect(ChargeHash(element)) as ElementCharge;
            }
            if (charge)
            {
                charge.Hold(HeldStacks);
            }
        }

        // Its count on the player's ZDO is the drawn staff's, left to the callers
        private static void Clear(Player player, Element element)
        {
            if (player && element != Element.None)
            {
                player.GetSEMan().RemoveStatusEffect(ChargeHash(element), true);
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
            TwinsLog.Event($"{(player ? TwinsLog.Name(player) : s_playerName)} leaves the fight{(player && player.IsDead() ? " (dead)" : "")}.");
            Summarize();
            s_inFight = false;
            s_overloads = 0;
            TwinsJudgment.Reset(player);
            if (player && player.m_nview && player.m_nview.IsValid())
            {
                Clear(player, Element.Storm);
                Clear(player, Element.Fire);
                SetStacks(player, 0);
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
