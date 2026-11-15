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
    // - A swap (the fight's redesign, agreed with the user 2026-11-15, its changes 2 and 3; the first draw of a fight
    //   isn't one, and gets no answer): the old staff's charge drains evenly to nothing over DrainTime from whatever it
    //   was, slowing nothing meanwhile; drawn again before it's gone, it builds on from where it is, and a swap never
    //   starts a charge's clock again (Build, Drain). So DrainTime on one staff leaves the other clean whatever it had, a
    //   steady rhythm under 25 s never passes 4 stacks, and swapping fast only keeps both charges between 3 and 4. And
    //   the new staff's twin answers (CallerBoss.RPC_Punish): a clean staff (no charge left) with its rune under the
    //   player, striking AnswerDelay later as the twin lands on it, and nothing for who has left it; a staff still
    //   charged at once, with its smite, unavoidable, and its rush. Before the redesign a swap cleared the old charge
    //   (until 2026-11-15: it let a player stay on one staff by swapping away and straight back), then held it at 4
    //   stacks until the new staff's reached 4 (the user's rule of that day), started the new staff's clock again (so
    //   swapping every 4.9 s meant no stack ever landed), and the answer was always the smite and the rush
    // - A fallen twin's element is silent while its twin fights on alone (Silence; the fight's redesign, agreed with the
    //   user 2026-11-15, its change 7: "While a twin is down, its element does nothing to players, so the last window is
    //   one staff, one target and the raise countdown"): its charge goes and builds no more, so no overload comes of it
    //   (until then its smites went on coming "from the elements"), and drawing its staff, the only one that hurts the
    //   survivor (TwinsRules: a staff cannot hurt its own caller), gets no answer. If the twin rises, its charge builds
    //   again from none. The survivor's own element is as ever
    internal static class TwinsDuel
    {
        internal const float EngageRadius = 50f;
        private const float LeaveRadius = 80f;
        private const float StackInterval = 5f;
        internal const int MaxStacks = 5;
        // Once the other staff is drawn, a charge drains to nothing over this long, from whatever it was (seconds)
        private const float DrainTime = 20f;
        // A draining charge this small is gone (stacks)
        private const float CleanBelow = 0.005f;
        // The swap's answer to a clean staff: the twin's rune under the player, striking this long after it shows (the
        // storm's rain runes warn as long), this wide (the comet's mark), for SwapDamage to whoever is still on it
        internal const float AnswerDelay = 1.2f;
        internal const float AnswerRadius = 2.5f;
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
        // The element whose twin has fallen, if one has: silent (Silence)
        private static Element s_silent;
        // Each element's charge on the player, in stacks (by Element): building evenly while its staff is the last drawn,
        // a stack each StackInterval, and draining evenly while the other is, at the pace set as that one was drawn
        private static readonly float[] s_charge = new float[3];
        private static readonly float[] s_drain = new float[3];
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
                s_silent = Element.None;
                System.Array.Clear(s_charge, 0, s_charge.Length);
                System.Array.Clear(s_drain, 0, s_drain.Length);
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
            // A fallen twin's element goes silent, and builds nothing
            Silence(player);
            Element held = ElementOf(player.m_rightItem);
            if (held != Element.None && held != s_drawn)
            {
                Draw(player, held);
            }
            // The last staff drawn builds its charge, the other's drains
            foreach (Element element in s_elements)
            {
                if (element == s_silent)
                {
                    continue;
                }
                if (element == s_drawn)
                {
                    Build(player, element, dt);
                }
                else if (s_charge[(int)element] > 0f)
                {
                    Drain(player, element, dt);
                }
            }
        }

        // The drawn staff's charge builds, from wherever it is: a stack each StackInterval
        private static void Build(Player player, Element element, float dt)
        {
            int e = (int)element;
            bool none = s_charge[e] <= 0f;
            int before = Mathf.FloorToInt(s_charge[e]);
            s_charge[e] += dt / StackInterval;
            if (none)
            {
                Show(player, element);
            }
            int now = Mathf.FloorToInt(s_charge[e]);
            for (int stacks = before + 1; stacks <= now; stacks++)
            {
                Stack(player, element, stacks);
            }
        }

        // The other staff's charge drains, at the pace set as it was put down (Draw), until its staff is clean
        private static void Drain(Player player, Element element, float dt)
        {
            int e = (int)element;
            s_charge[e] -= s_drain[e] * dt;
            // Clean at DrainTime sharp, as the new staff's fourth stack lands: not a frame's rounding later
            if (s_charge[e] > CleanBelow && s_drain[e] > 0f)
            {
                Show(player, element);
                return;
            }
            s_charge[e] = 0f;
            s_drain[e] = 0f;
            Clear(player, element);
            TwinsLog.Event($"{TwinsLog.Name(player)}'s {ChargeName(element)} has drained to nothing: the {StaffName(element)} is clean.");
        }

        // The charge's status effect, as the charge is: there for as long as there's any of it, so the player sees which
        // staff isn't clean, and what answer drawing it would get; with its stacks in hand, or how long until it's gone
        private static void Show(Player player, Element element)
        {
            int e = (int)element;
            ElementCharge effect = Charge(player, element);
            if (s_charge[e] <= 0f)
            {
                if (effect)
                {
                    Clear(player, element);
                }
                return;
            }
            if (!effect)
            {
                effect = player.GetSEMan().AddStatusEffect(ChargeHash(element)) as ElementCharge;
            }
            if (!effect)
            {
                return;
            }
            int stacks = Mathf.FloorToInt(s_charge[e]);
            if (element == s_drawn)
            {
                effect.Build(stacks);
            }
            else
            {
                effect.Drain(stacks, s_drain[e] > 0f ? s_charge[e] / s_drain[e] : 0f);
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

        // Whether `element` is silent: its twin fallen (or never there), the other's fighting on alone (TwinsBond.Alone,
        // from the survivor's ZDO: a twin only not loaded on this game isn't a fallen one)
        private static bool Silent(Player player, Element element)
        {
            CallerBoss.NearestLiving(player.transform.position, Other(element), out CallerBoss survivor);
            return survivor && TwinsBond.Alone(survivor);
        }

        // Every frame of the fight: the fallen twin's element going silent (its charge goes, in hand or draining), and
        // sounding again as the twin rises (its staff in hand, its charge builds again from none)
        private static void Silence(Player player)
        {
            Element silent = Silent(player, Element.Storm) ? Element.Storm : Silent(player, Element.Fire) ? Element.Fire : Element.None;
            if (silent == s_silent)
            {
                return;
            }
            Element was = s_silent;
            s_silent = silent;
            if (was != Element.None)
            {
                TwinsLog.Event($"{TwinsLog.Name(player)}: {ChargeName(was)} is silent no more (its twin is back){(s_drawn == was ? ": it builds again from none" : "")}.");
            }
            if (silent != Element.None)
            {
                int s = (int)silent;
                TwinsLog.Event($"{TwinsLog.Name(player)}: {ChargeName(silent)} goes silent (its twin has fallen): "
                    + $"{(s_charge[s] > 0f ? $"{s_charge[s]:0.0} {(silent == s_drawn ? "in hand" : "draining")} -> gone" : "none on them")}, and it builds no more.");
                s_charge[s] = 0f;
                s_drain[s] = 0f;
                Clear(player, silent);
            }
            if (s_drawn != Element.None && (s_drawn == was || s_drawn == silent))
            {
                s_overloads = 0;
                if (s_drawn == silent)
                {
                    SetStacks(player, 0);
                }
            }
        }

        private static void Draw(Player player, Element held)
        {
            Element old = s_drawn;
            SetDrawn(player, held);
            float heldFor = Time.time - s_drawnAt;
            s_drawnAt = Time.time;
            if (old == Element.None)
            {
                TwinsLog.Event($"{TwinsLog.Name(player)} draws the {StaffName(held)} (first draw).");
                return;
            }
            // A swap: the old staff's charge drains from here, over DrainTime whatever it is; the new staff's, if any is
            // left, builds on from where it is. The overloads' growth goes (not their SmiteWeakness, which runs out on its
            // own), and the new staff's twin answers: with its rune for a clean staff, at once for one still charged.
            // A silent element (its twin fallen: Silence) has no charge to drain or to build on, and no twin to answer
            int o = (int)old;
            int h = (int)held;
            s_drain[o] = s_charge[o] / DrainTime;
            s_drain[h] = 0f;
            bool clean = s_charge[h] <= 0f;
            Show(player, old);
            Show(player, held);
            SetStacks(player, Mathf.FloorToInt(s_charge[h]));
            s_overloads = 0;
            s_swaps++;
            if (!clean)
            {
                s_takenBack++;
            }
            CallerBoss.NearestLiving(player.transform.position, held, out CallerBoss twin);
            TwinsLog.Event($"{TwinsLog.Name(player)} swaps to the {StaffName(held)} after {heldFor:0} s on the {StaffName(old)}: "
                + $"{ChargeName(old)} {(old == s_silent ? "silent" : s_charge[o] > 0f ? $"{s_charge[o]:0.0}, draining over {DrainTime:0} s" : "none")}, "
                + $"{ChargeName(held)} {(held == s_silent ? "silent (its twin has fallen)" : clean ? "none: a clean staff" : $"still {s_charge[h]:0.0}: it builds on")}; "
                + $"{(twin ? $"the {TwinsLog.Name(twin)} answers {(clean ? "with its rune" : "at once")}" : "no living twin of it to answer")}. Now: {Describe(player, true)}.");
            if (twin)
            {
                twin.m_nview.InvokeRPC(CallerBoss.PunishRpc, player.GetZDOID(), clean);
            }
        }

        // The hit of a twin's rune answering a clean staff (CallerBoss.RPC_Punish): SwapDamage of its element to
        // whoever is on the rune as it strikes; a roll through it dodges it, a block doesn't stop it, and it doesn't
        // stagger (as the storm's rain: TwinsStorm)
        internal static HitData AnswerHit(Element element, Character attacker)
        {
            HitData hit = new HitData();
            if (element == Element.Storm)
            {
                hit.m_damage.m_lightning = SwapDamage;
            }
            else
            {
                hit.m_damage.m_fire = SwapDamage;
            }
            hit.m_blockable = false;
            hit.m_dodgeable = true;
            hit.m_ranged = true;
            hit.m_staggerMultiplier = 0f;
            hit.m_hitType = HitData.HitType.EnemyHit;
            hit.SetAttacker(attacker);
            return hit;
        }

        private static string StaffName(Element element)
        {
            return element == Element.Storm ? "Staff of Storms" : "Wrath of Muspelheim";
        }

        private static string ChargeName(Element element)
        {
            return element == Element.Storm ? "Static" : "Scorch";
        }

        internal static Element Other(Element element)
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

        // The drawn staff's charge reaches `stacks` (Build)
        private static void Stack(Player player, Element element, int stacks)
        {
            Show(player, element);
            SetStacks(player, stacks);
            LogStack(player, element, stacks);
            if (stacks < MaxStacks)
            {
                return;
            }
            // Full, and every stack after: the element's twin smites them, harder each time without a swap, and leaves
            // them weaker to it for a while (after the hit: this smite hits as the last one left them)
            CallerBoss.NearestLiving(player.transform.position, element, out CallerBoss twin);
            float smite = OverloadDamage * Mathf.Pow(OverloadGrowth, s_overloads);
            TwinsLog.Event($"{TwinsLog.Name(player)} overloads with {ChargeName(element)} at {stacks} ({s_overloads + 1} since the last swap): a smite of {smite:0} from the {(twin ? TwinsLog.Name(twin) : "elements")}.");
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
        private static void LogStack(Player player, Element element, int stacks)
        {
            s_mostStacks[(int)element] = Mathf.Max(s_mostStacks[(int)element], stacks);
            SEMan seman = player.GetSEMan();
            float eitr = 1f;
            seman.ModifyEitrRegen(ref eitr);
            float stamina = 1f;
            seman.ModifyStaminaRegen(ref stamina);
            TwinsLog.Event($"{TwinsLog.Name(player)}'s {ChargeName(element)} reaches {stacks}. "
                + $"Now: {Describe(player, true)}; regen with every effect: eitr x{eitr:0.00}, stamina x{stamina:0.00}.");
        }

        // "Static 6.2 in hand (eitr regen x0.00), Scorch 2.4 draining, clean in 12 s (no effect), Stormstruck 2 (48 s
        // left)": what the fight has put on the local player, for the log (TwinsLog's hit lines too, without the regen)
        internal static string Describe(Player player, bool regen)
        {
            if (!player)
            {
                return "nothing";
            }
            List<string> parts = new List<string>();
            foreach (Element element in s_elements)
            {
                int e = (int)element;
                float charge = s_charge[e];
                if (charge <= 0f)
                {
                    continue;
                }
                bool drawn = element == s_drawn;
                string effect = !regen ? ""
                    : drawn ? $" ({(element == Element.Storm ? "eitr" : "stamina")} regen x{Mathf.Max(0f, 1f - RegenCut * Mathf.FloorToInt(charge)):0.00})"
                    : " (no effect)";
                parts.Add($"{ChargeName(element)} {charge:0.0} {(drawn ? "in hand" : $"draining, clean in {(s_drain[e] > 0f ? charge / s_drain[e] : 0f):0} s")}{effect}");
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
            TwinsLog.Event($"Fight summary for {name}: {s_swaps} swaps, {s_takenBack} of them to a staff not yet clean (answered at once); "
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
            s_silent = Element.None;
            System.Array.Clear(s_charge, 0, s_charge.Length);
            System.Array.Clear(s_drain, 0, s_drain.Length);
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
