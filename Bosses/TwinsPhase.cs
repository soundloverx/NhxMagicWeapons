using System.Collections.Generic;
using NhxMagicWeapons.Staves;
using NhxMagicWeapons.Staves.StaffOfStorms;
using NhxMagicWeapons.Staves.StaffOfStorms.Effects;
using NhxMagicWeapons.Staves.WrathOfMuspelheim;
using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;
using UnityEngine;
using UnityEngine.Rendering;
using Storms = NhxMagicWeapons.Staves.StaffOfStorms.Staff;
using Wrath = NhxMagicWeapons.Staves.WrathOfMuspelheim.Staff;

namespace NhxMagicWeapons.Bosses
{
    // Each twin's second phase, from half its health (the user, 2026-11-15: "Can we add a 'phase 2' to each boss below
    // 50%? [...] Maybe something that affects the player who the boss isn't targeting?"; then of Claude's options: "go with
    // your pick, no speed increase. But I do like the idea of keeping the players separated, so just arc lightning between
    // them whenever they are closer than 12m. It shouldn't be deadly, but enough for players to want to avoid that. There
    // is no need for a message on the screen, the visuals are enough"):
    // - Its coming (the user, 2026-11-16: "Can we make the transition into phase 2 a bit more flashy? The boss would stop
    //   doing whatever he's doing for 5 seconds, grow by an additional 25% in size in that time [...] During those 5
    //   seconds, I imagine runes floating around their body, the same runes that appear when hit by the staff they are
    //   immune to"; then of Claude's take on it, the burst at the end and three extras: "I like absolutely everything you
    //   described [...] I want all the extras"): the twin stands for TransitionTime, untouchable (TwinsRules: its ward
    //   shows where a hit lands, its burning waits), and grows by Growth on every game (Grow). Its ward's runes circle it, closing in and
    //   brightening (PhaseRunes); the Stormcaller's shards spread and grow, the Firecaller's wings open partway. As it
    //   ends it raises its arms (its call's animation), the runes fall into it and burst out along the ground, the storm
    //   strikes it or Muspelheim's blast bursts from it, its shards race or its wings burst fully open, and the players
    //   near are pushed back (Push, no harm; a roll through it dodges it). A raised twin comes back at half health
    //   (TwinsBond), so in its second phase at once, and comes into it as above, its line going ahead of any waiting
    //   (TwinsVoices; the user: "as long as he plays his voice line again (the voice line gaining priority over anything
    //   else when the boss resurrects, but without stopping voice lines already in progress)")
    // - The Stormcaller: its shards grow and race around it (Armor\ArmorLegendFx asks ProgressOf); every StormInterval it
    //   calls the storm over the player it isn't hunting (TwinsStorm); and while it lives, from its burst on, players
    //   closer than LinkRange to each other are joined by arcs of lightning that hurt both (UpdatePlayer)
    // - The Firecaller: its wings open and stay (ChannelWings); every CometInterval it marks the player it isn't hunting
    //   with a rune and flames in its hands (CometWindup, standing), then comets through where they stood, CometPast
    //   beyond, hitting whoever is on the way, and the way burns for TrailTime, hurting whoever stands in it
    // - Alone with one player, the twin that watches (TwinsAI) casts its phase's spell at them in place of its own; the
    //   hunter, with no one else, has no one to cast it at. A watching Firecaller comets every WatcherCometInterval (the
    //   user, 2026-11-16: "When firecaller is watching, let it use the comet more often, with just a 7 seconds
    //   cooldown"): its role as the comet comes due decides, so a watcher that starts hunting waits out CometInterval
    //   from its last. No faster: their speeds and other attacks are as before.
    // - Its twin fallen (the fight's redesign, agreed with the user 2026-11-15, its change 7): its second phase comes at
    //   once, whatever its health (TwinFell), and it goes on casting its spell while it hunts alone: at another player
    //   in the fight, or with no other, at its own quarry (OtherPlayer)
    // Whether it's come, and when, are in its ZDO, for every game's looks; its owner's game decides and casts
    internal class TwinsPhase : MonoBehaviour
    {
        internal const float Threshold = 0.5f;
        // Its coming: how long (2 s until the user's 5, 2026-11-16), and how much bigger it is by the end (a quarter again)
        private const float TransitionTime = 5f;
        private const float Growth = 0.25f;
        // Its arms raised (its call's animation: BossAttacks) this long before the end, to be up as the runes fall in
        private const string ArmsAnimation = "staff_shield";
        private const float ArmsBefore = 0.4f;
        // As it comes, the players within PushRadius (meters from where it stands) are pushed back, PushNear meters for one
        // at its feet down to PushFar at the edge. The game moves a pushed character at PushSpeed (m/s) while its push
        // lasts, the push waning by PushWane a second (Character.UpdatePushback), so a push of distance * PushWane /
        // PushSpeed carries them that far on open ground
        internal const float PushRadius = 6f;
        private const float PushNear = 5f;
        private const float PushFar = 2f;
        // (the pulse's kick too: TwinsPulse)
        internal const float PushSpeed = 20f;
        internal const float PushWane = 100f;
        // The Firecaller's wings open this far (0 to 1) as it comes, the rest of the way at once as it has come
        private const float WingsWhileComing = 0.6f;
        // The Stormcaller's shake as it has come (meters around, strength; Muspelheim's blast shakes for the Firecaller)
        private const float StormShakeRange = 30f;
        private const float StormShake = 0.6f;
        // Its first spell comes this long after the transition; with no one to cast it at, it looks again this often
        private const float FirstAfter = 3f;
        private const float RetryAfter = 1f;
        private const float StormInterval = 20f;
        private const float CometInterval = 14f;
        private const float WatcherCometInterval = 7f;
        // The comet: the warning (standing, flames in its hands toward the rune, every WindupFlamesEvery), how far past the
        // mark it lands, who it hits on the way (within CometWidth of its line: fire, a roll dodges it, no block, a push),
        // and its burning way (TrailWidth either side, TrailTime)
        private const float CometWindup = 1.2f;
        private const float WindupFlamesEvery = 0.2f;
        private const float WindupFlameReach = 1.5f;
        private const float CometRuneRadius = 2.5f;
        private const float CometPast = 5f;
        private const float CometWidth = 2f;
        private const float CometDamage = 150f;
        // Its way, sent to every game to judge its own player (Burn)
        private const string CometRpc = "NhxTwinsComet";
        private const float CometPush = 30f;
        private const float TrailTime = 5f;
        private const float TrailWidth = 1.5f;
        // The Firecaller's burst as its phase has come (meters around, camera shake), and how fast its wings' flare dies
        private const float FireBurstRadius = 5f;
        private const float FireBurstShake = 1f;
        private const float FlareFade = 1.5f;
        private const int LookLevel = 20;

        // The players' side, on each player's own game (UpdatePlayer): the burning way takes TrailShare of their health
        // each TrailTick, the arcs LinkShare for each player within LinkRange each LinkTick (after LinkGrace), both lessened
        // by their resistance to the element (meads); neither staggers. Arcs drawn every ArcInterval, by one game of the
        // two, at this look's level
        private const float TrailTick = 0.5f;
        private const float TrailShare = 0.06f;
        private const float LinkRange = 12f;
        private const float LinkTick = 1f;
        private const float LinkGrace = 0.5f;
        private const float LinkShare = 0.05f;
        private const float ArcInterval = 0.25f;
        private const int ArcLevel = 12;

        private static readonly int s_enragedKey = "nhx_twin_enraged".GetStableHashCode();
        private static readonly int s_enragedAtKey = "nhx_twin_enraged_at".GetStableHashCode();
        // Its next spell's time and its last comet's, in the network's time, for a game that takes it over (Resume;
        // CallerBoss: its timers go on where they were)
        private static readonly int s_nextSpellKey = "nhx_twin_next_spell".GetStableHashCode();
        private static readonly int s_lastCometKey = "nhx_twin_last_comet".GetStableHashCode();
        private static readonly Dictionary<Character, TwinsPhase> s_phases = new Dictionary<Character, TwinsPhase>();
        private static readonly List<Player> s_partners = new List<Player>();

        private static long Now => ZNet.instance ? ZNet.instance.GetTime().Ticks : 0L;

        private CallerBoss m_boss;
        private ZNetView m_nview;
        private Humanoid m_humanoid;
        private ZSyncAnimation m_animation;
        // On every game: its size as it loaded (Grow), whether this game has looked at its phase yet, and whether it
        // saw it coming (for its end: Come)
        private Vector3 m_size = Vector3.one;
        private bool m_shown;
        private bool m_watched;
        private float m_flare;
        // On the owner's game: it was raised (TwinsBond.OnStart: its line comes first), its coming is under way, its
        // arms are up
        private bool m_raised;
        private bool m_coming;
        private bool m_armsUp;
        private float m_nextSpell;
        // When its last comet was marked: a hunter's next waits CometInterval from it, a watcher's WatcherCometInterval
        private float m_lastComet = float.NegativeInfinity;
        private float m_windup = -1f;
        private Vector3 m_aim;
        private float m_flameTimer;

        internal bool Enraged => m_nview && m_nview.IsValid() && m_nview.GetZDO().GetBool(s_enragedKey);

        // Its phase coming: untouchable, standing
        internal bool Transforming => Enraged && Now - m_nview.GetZDO().GetLong(s_enragedAtKey) < System.TimeSpan.FromSeconds(TransitionTime).Ticks;

        // How far its phase has come: 0 before it, 1 once it has come, between as it comes (by the time every game shares)
        internal float Progress
        {
            get
            {
                if (!Enraged)
                {
                    return 0f;
                }
                float since = (float)System.TimeSpan.FromTicks(Now - m_nview.GetZDO().GetLong(s_enragedAtKey)).TotalSeconds;
                return Mathf.Clamp01(since / TransitionTime);
            }
        }

        // How much bigger it is than it was before its phase (eased in and out as it comes): its size (Grow), its pulse's
        // (TwinsPulse)
        internal float Grown => 1f + Growth * Mathf.SmoothStep(0f, 1f, Progress);

        // How far a twin's second phase has come, 0 for anyone else (its shards: Armor\ArmorLegendFx)
        internal static float ProgressOf(Character character)
        {
            return character && s_phases.TryGetValue(character, out TwinsPhase phase) && phase ? phase.Progress : 0f;
        }

        // It was just raised (TwinsBond.OnStart, on its owner's game, before its first Update): its phase's line, as it
        // comes back in it, goes ahead of those waiting
        internal void Raised()
        {
            m_raised = true;
        }

        // Standing still, facing `look`: as its phase comes, or winding up its comet (TwinsAI holds its AI)
        internal bool Busy(out Vector3 look)
        {
            look = transform.forward;
            if (m_windup >= 0f)
            {
                look = m_aim - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude < 0.01f)
                {
                    look = transform.forward;
                }
                return true;
            }
            return Transforming;
        }

        private void Awake()
        {
            m_boss = GetComponent<CallerBoss>();
            m_nview = GetComponent<ZNetView>();
            m_humanoid = GetComponent<Humanoid>();
            m_animation = GetComponent<ZSyncAnimation>();
            if (m_nview)
            {
                m_nview.Register<Vector3, Vector3>(CometRpc, (sender, from, to) => Burn(from, to));
            }
            if (m_humanoid)
            {
                s_phases[m_humanoid] = this;
            }
        }

        // After every Awake: its size as the game made it (CallerBosses.Size, which the world's modifiers change a little in
        // Character.Awake), the same on every game. Not sent (CallerBosses: ZSyncTransform), so never a grown one
        private void Start()
        {
            m_size = transform.localScale;
        }

        private void OnDestroy()
        {
            if (m_humanoid && s_phases.TryGetValue(m_humanoid, out TwinsPhase phase) && phase == this)
            {
                s_phases.Remove(m_humanoid);
            }
        }

        private void Update()
        {
            if (!m_nview || !m_nview.IsValid() || !m_humanoid || !m_boss || m_boss.m_abandoned)
            {
                return;
            }
            float dt = Time.deltaTime;
            bool dead = m_humanoid.IsDead();
            bool enraged = Enraged;
            if (enraged && !dead)
            {
                Grow();
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                {
                    Looks(dt);
                }
            }
            if (!m_nview.IsOwner() || dead)
            {
                m_windup = -1f;
                m_coming = false;
                m_armsUp = false;
                return;
            }
            // Not before this game has taken up its timers (CallerBoss.Resume)
            if (!m_boss.Running)
            {
                return;
            }
            if (!enraged)
            {
                if (m_humanoid.GetHealthPercentage() <= Threshold)
                {
                    Enrage(m_raised ? "raised" : null);
                }
                return;
            }
            if (Transforming)
            {
                m_coming = true;
                RaiseArms();
                return;
            }
            if (m_coming)
            {
                m_coming = false;
                Come();
            }
            if (m_boss.Element == Element.Storm)
            {
                UpdateStorm();
            }
            else
            {
                UpdateComet(dt);
            }
        }

        // On every game: its size, from how far its phase has come (eased in and out), set only as it changes
        private void Grow()
        {
            Vector3 size = m_size * Grown;
            if ((transform.localScale - size).sqrMagnitude > 1e-8f)
            {
                transform.localScale = size;
            }
        }

        // On every game: its phase's coming (its runes, if this game sees it come; the burst as it has come) and its wings
        private void Looks(float dt)
        {
            bool transforming = Transforming;
            if (!m_shown)
            {
                m_shown = true;
                // A game that finds it already come (joining, or loading it) sees it as it is now
                if (transforming)
                {
                    m_watched = true;
                    PhaseRunes.Show(m_boss, m_nview.GetZDO().GetLong(s_enragedAtKey), TransitionTime, PushRadius);
                }
            }
            if (m_watched && !transforming)
            {
                m_watched = false;
                Climax();
            }
            if (m_boss.Element == Element.Fire)
            {
                ChannelWings.Hold(m_humanoid, true, m_flare, transforming ? WingsWhileComing * Mathf.SmoothStep(0f, 1f, Progress) : 1f);
            }
            m_flare = Mathf.MoveTowards(m_flare, 0f, dt * FlareFade);
        }

        // On every game, as its phase has come: Muspelheim's blast from the Firecaller (its wings burst open: Looks), a
        // shake for the Stormcaller (the storm strikes it: Come); this game's player pushed back
        private void Climax()
        {
            m_flare = 1f;
            if (m_boss.Element == Element.Fire)
            {
                if (Wrath.Beam)
                {
                    Wrath.Beam.ShowBlastOfRadius(transform.position, FireBurstRadius, FireBurstShake);
                }
            }
            else if (GameCamera.instance)
            {
                GameCamera.instance.AddShake(transform.position, StormShakeRange, StormShake, false);
            }
            Push(Player.m_localPlayer);
        }

        // This game's player, if they're in its fight within PushRadius, pushed straight away from it (on their own game,
        // where their body moves): no harm, no stagger; one rolling just then isn't touched
        private void Push(Player player)
        {
            if (!player || player.IsDead() || Judged(player) || !TwinsRing.Inside(m_boss, player.transform.position))
            {
                return;
            }
            Vector3 flat = player.transform.position - transform.position;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance > PushRadius)
            {
                return;
            }
            if (player.IsDodgeInvincible())
            {
                TwinsLog.Event($"{TwinsLog.Name(player)} rolls through the {TwinsLog.Name(m_boss)}'s burst ({distance:0.#} m from it).");
                return;
            }
            Vector3 away = distance > 0.1f ? flat / distance : -player.transform.forward;
            float reach = Mathf.Lerp(PushNear, PushFar, distance / PushRadius);
            Vector3 push = away * (reach * PushWane / PushSpeed);
            if (player.m_pushForce.sqrMagnitude < push.sqrMagnitude)
            {
                player.m_pushForce = push;
            }
            TwinsLog.Event($"{TwinsLog.Name(player)} is pushed back by the {TwinsLog.Name(m_boss)}'s burst: {distance:0.#} m from it, about {reach:0.#} m.");
        }

        // On the owner's game, as its phase comes: its arms go up ArmsBefore its end (the animation is sent to every game)
        private void RaiseArms()
        {
            if (m_armsUp || Progress < 1f - ArmsBefore / TransitionTime)
            {
                return;
            }
            m_armsUp = true;
            if (m_animation)
            {
                m_animation.SetTrigger(ArmsAnimation);
            }
        }

        // On the owner's game, as its phase has come: the storm strikes the Stormcaller (networked)
        private void Come()
        {
            m_armsUp = false;
            TwinsLog.Event($"{TwinsLog.Name(m_boss)}'s second phase has come.");
            if (m_boss.Element == Element.Storm)
            {
                if (Storms.Strike)
                {
                    StrikeLightning.SetLevel(Instantiate(Storms.Strike, transform.position, Quaternion.identity), LookLevel);
                }
                if (Storms.Crackle)
                {
                    StaffEffects.Create(new[] { Storms.Crackle }, m_humanoid.GetCenterPoint(), Quaternion.identity);
                }
            }
        }

        // Its next spell no sooner than `seconds` from now: here, and in its ZDO for a game that takes it over
        private void SetNextSpell(float seconds)
        {
            m_nextSpell = Time.time + seconds;
            m_nview.GetZDO().Set(s_nextSpellKey, CallerBoss.NetworkIn(seconds));
        }

        // As a game begins to run it (CallerBoss.Resume): its spells' times from its ZDO, where the game that ran it
        // before left them. What it took up, for the log, or null
        internal string Resume()
        {
            if (!m_nview || !m_nview.IsValid())
            {
                return null;
            }
            ZDO zdo = m_nview.GetZDO();
            List<string> resumed = new List<string>();
            long next = zdo.GetLong(s_nextSpellKey);
            if (next != 0L)
            {
                m_nextSpell = Time.time + CallerBoss.SecondsUntil(next);
                float wait = m_nextSpell - Time.time;
                resumed.Add((m_boss && m_boss.Element == Element.Storm ? "storm " : "phase spell ") + (wait > 0f ? $"in {wait:0.#} s" : "ready"));
            }
            long comet = zdo.GetLong(s_lastCometKey);
            if (comet != 0L)
            {
                m_lastComet = Time.time + CallerBoss.SecondsUntil(comet);
                resumed.Add($"last comet {Time.time - m_lastComet:0.#} s ago");
            }
            return resumed.Count > 0 ? string.Join(", ", resumed) : null;
        }

        // Its twin fell (TwinsBond.Fell, on its owner's game): alone, its second phase comes now, whatever its health,
        // with its transition and its line, as at half its health
        internal void TwinFell()
        {
            if (m_nview && m_nview.IsValid() && m_nview.IsOwner() && m_humanoid && !m_humanoid.IsDead() && !Enraged)
            {
                Enrage("its twin fell");
            }
        }

        private void Enrage(string why)
        {
            ZDO zdo = m_nview.GetZDO();
            zdo.Set(s_enragedKey, true);
            zdo.Set(s_enragedAtKey, Now);
            TwinsLog.Event($"{TwinsLog.Name(m_boss)} enters its second phase at {100f * m_humanoid.GetHealthPercentage():0}% health{(why != null ? $" ({why})" : "")}.");
            // Its line (the user's recordings: TwinsVoices), queued behind its twin's if both come at once; a raised twin's
            // goes ahead of those waiting, after the one playing
            bool raised = m_raised;
            m_raised = false;
            TwinsVoices.Say(m_boss.Element == Element.Storm ? TwinsVoices.BlueP2 : TwinsVoices.RedP2, transform.position,
                $"the {TwinsLog.Name(m_boss)} enters its second phase{(raised ? ", raised" : "")}", raised);
            SetNextSpell(TransitionTime + FirstAfter);
            m_coming = true;
            m_armsUp = false;
        }

        private void UpdateStorm()
        {
            if (Time.time < m_nextSpell)
            {
                return;
            }
            Player other = OtherPlayer();
            if (!other)
            {
                m_nextSpell = Time.time + RetryAfter;
                return;
            }
            SetNextSpell(StormInterval);
            TwinsLog.Event($"Stormcaller calls its storm over {TwinsLog.Name(other)}.");
            TwinsStorm.Create(m_humanoid, other);
        }

        private void UpdateComet(float dt)
        {
            if (m_windup >= 0f)
            {
                m_windup -= dt;
                m_flameTimer -= dt;
                if (m_flameTimer <= 0f)
                {
                    m_flameTimer = WindupFlamesEvery;
                    WindupFlames();
                }
                if (m_windup <= 0f)
                {
                    m_windup = -1f;
                    Comet();
                }
                return;
            }
            float interval = m_boss.m_role == CallerBoss.Role.Watcher ? WatcherCometInterval : CometInterval;
            if (Time.time < m_nextSpell || Time.time < m_lastComet + interval || m_humanoid.InAttack())
            {
                return;
            }
            Player other = OtherPlayer();
            if (!other)
            {
                m_nextSpell = Time.time + RetryAfter;
                return;
            }
            m_lastComet = Time.time;
            m_nview.GetZDO().Set(s_lastCometKey, CallerBoss.NetworkIn(0f));
            m_aim = LightningStrike.Ground(other.transform.position);
            m_windup = CometWindup;
            m_flameTimer = 0f;
            // The mark: a rune where they stood, erupting as the comet comes (its look only: the comet hits)
            if (TwinsDuel.s_fireRune)
            {
                ElementRune.Create(TwinsDuel.s_fireRune, m_aim, CometWindup, CometRuneRadius);
            }
        }

        // Flames gathering in its hands, toward the mark
        private void WindupFlames()
        {
            if (!Wrath.Flames)
            {
                return;
            }
            Vector3 hands = BossHands.Between(m_humanoid, m_humanoid.GetCenterPoint());
            Vector3 toward = m_aim - hands;
            if (toward.sqrMagnitude > 0.01f)
            {
                FlameLook.SetReach(Instantiate(Wrath.Flames, hands, Quaternion.LookRotation(toward)), WindupFlameReach * transform.localScale.y);
            }
        }

        // Through where its mark stood, and on past it
        private void Comet()
        {
            Vector3 start = transform.position;
            Vector3 flat = m_aim - start;
            flat.y = 0f;
            float distance = flat.magnitude;
            Vector3 dir = distance > 0.1f ? flat / distance : transform.forward;
            if (!m_boss.Flash(dir, distance + CometPast, out Vector3 from, out Vector3 to))
            {
                TwinsLog.Event("Firecaller's comet: nowhere to land, none.");
                return;
            }
            TwinsLog.Event($"Firecaller's comet from {TwinsLog.At(from)} to {TwinsLog.At(to)} ({Vector3.Distance(from, to):0} m).");
            if (CometDash.s_comet)
            {
                FireComet.Create(CometDash.s_comet, from, to, LookLevel, TrailTime, TrailWidth, true);
            }
            StaffEffects.Create(CometDash.s_leaveEffects, from, Quaternion.LookRotation(dir));
            m_flare = 1f;
            // Each game judges whether its own player was on the way (Burn)
            m_nview.InvokeRPC(ZNetView.Everybody, CometRpc, from, to);
        }

        // On every game, as the comet goes from `from` to `to` (CometRpc): this game's player takes its hit if they're on
        // the way. Each game judges its own (Claude's review, 2026-11-17, its #1, as ElementRune's strikes): the game that
        // runs the twin judged every player where it saw them, 0.2 s or more behind where they were. A roll through it
        // still dodges it, as the game judges it, on the player's own game (Character.RPC_Damage)
        private void Burn(Vector3 from, Vector3 to)
        {
            Player player = Player.m_localPlayer;
            // Those in its fight: who watches from outside the ring isn't touched (TwinsRing)
            if (!player || player.IsDead() || !m_humanoid || !TwinsRing.Inside(m_boss, player.transform.position))
            {
                return;
            }
            Vector3 path = to - from;
            float length = path.magnitude;
            Vector3 along = length > 0.01f ? path / length : transform.forward;
            Vector3 center = player.GetCenterPoint();
            float t = Mathf.Clamp(Vector3.Dot(center - from, along), 0f, length);
            float off = Vector3.Distance(from + along * t, center);
            if (off > CometWidth)
            {
                if (off <= CometWidth + 1.5f)
                {
                    TwinsLog.Event($"{TwinsLog.Name(player)} is clear of the {TwinsLog.Name(m_boss)}'s comet by {off - CometWidth:0.0} m.");
                }
                return;
            }
            TwinsLog.Event($"{TwinsLog.Name(player)} is on the {TwinsLog.Name(m_boss)}'s comet's way, {CometWidth - off:0.0} m inside its edge{(player.IsDodgeInvincible() ? ", rolling" : "")}.");
            HitData hit = new HitData();
            hit.m_damage.m_fire = CometDamage;
            hit.m_point = center;
            hit.m_dir = along;
            hit.m_pushForce = CometPush;
            hit.m_blockable = false;
            hit.m_dodgeable = true;
            hit.m_staggerMultiplier = 0f;
            hit.m_hitType = HitData.HitType.EnemyHit;
            hit.SetAttacker(m_humanoid);
            player.Damage(hit);
        }

        // Whom its phase's spell goes to: watching, its one player; hunting, its twin's quarry if that isn't its own, or
        // the nearest other player in the fight, or, its twin fallen and no other player there, its own quarry (until the
        // redesign a lone hunter facing one player never cast it); none, nobody
        private Player OtherPlayer()
        {
            Character mine = m_boss.m_target;
            if (m_boss.m_role == CallerBoss.Role.Watcher)
            {
                return mine is Player watched && Fighter(watched) ? watched : null;
            }
            if (m_boss.m_role != CallerBoss.Role.Hunter)
            {
                return null;
            }
            CallerBoss.NearestLiving(transform.position, m_boss.Element == Element.Storm ? Element.Fire : Element.Storm, out CallerBoss twin);
            if (twin && twin.m_target is Player theirs && theirs != mine && Fighter(theirs))
            {
                return theirs;
            }
            Player nearest = null;
            float best = float.PositiveInfinity;
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == mine || !Fighter(player))
                {
                    continue;
                }
                float distance = Vector3.Distance(player.transform.position, transform.position);
                if (distance < best)
                {
                    best = distance;
                    nearest = player;
                }
            }
            if (!nearest && mine is Player quarry && Fighter(quarry) && TwinsBond.Alone(m_boss))
            {
                return quarry;
            }
            return nearest;
        }

        // A living player in this fight the elements haven't judged
        private bool Fighter(Player player)
        {
            return player && !player.IsDead() && !Judged(player) && TwinsRing.Inside(m_boss, player.transform.position);
        }

        private static bool Judged(Player player)
        {
            ZDO zdo = player.m_nview && player.m_nview.IsValid() ? player.m_nview.GetZDO() : null;
            return zdo != null && zdo.GetBool(TwinsJudgment.JudgedKey);
        }

        // ---- The players' side ----

        private static float s_trailTimer;
        private static float s_linkTimer = LinkGrace;
        private static float s_arcTimer;

        // Every frame of the fight, for the local player (TwinsDuel.Update, unless they're judged)
        internal static void UpdatePlayer(Player player, float dt)
        {
            s_trailTimer -= dt;
            if (s_trailTimer <= 0f)
            {
                s_trailTimer = TrailTick;
                if (FireComet.Burning(player.transform.position))
                {
                    Hurt(player, Element.Fire, TrailShare);
                }
            }
            UpdateLink(player, dt);
        }

        // The Stormcaller's arcs between players too close to each other, from its burst on: not while its phase comes, when
        // it does nothing (the user, 2026-11-16: "yes, start it at the burst"; until then they began with its coming)
        private static void UpdateLink(Player player, float dt)
        {
            CallerBoss.NearestLiving(player.transform.position, Element.Storm, out CallerBoss storm);
            TwinsPhase phase = storm ? storm.m_phase : null;
            s_partners.Clear();
            if (phase && phase.Enraged && !phase.Transforming && InStormFight(storm, player))
            {
                Vector3 center = player.GetCenterPoint();
                foreach (Player other in Player.GetAllPlayers())
                {
                    if (other && other != player && !other.IsDead() && !Judged(other)
                        && Vector3.Distance(other.GetCenterPoint(), center) <= LinkRange
                        && InStormFight(storm, other))
                    {
                        s_partners.Add(other);
                    }
                }
            }
            if (s_partners.Count == 0)
            {
                s_linkTimer = LinkGrace;
                s_arcTimer = 0f;
                return;
            }
            // Each pair's arc is drawn by one of its two games (networked: both see it), the one whose player's id is lower
            s_arcTimer -= dt;
            if (s_arcTimer <= 0f)
            {
                s_arcTimer = ArcInterval;
                ZDOID mine = player.GetZDOID();
                foreach (Player other in s_partners)
                {
                    if (LightningStep.s_bolt && mine.CompareTo(other.GetZDOID()) < 0)
                    {
                        LightningBolt.Arc(LightningStep.s_bolt, player, player.GetCenterPoint(), other.GetCenterPoint(), ArcLevel);
                    }
                }
            }
            s_linkTimer -= dt;
            if (s_linkTimer <= 0f)
            {
                s_linkTimer = LinkTick;
                Hurt(player, Element.Storm, LinkShare * s_partners.Count);
            }
            s_partners.Clear();
        }

        // A player the Stormcaller's arcs reach: one in its fight (TwinsRing: anywhere in its arena's ring; the user,
        // 2026-11-15: "entering the ring during a fight should place the player in combat, being then affected by
        // anything that entails")
        private static bool InStormFight(CallerBoss storm, Player player)
        {
            return TwinsRing.Inside(storm, player.transform.position);
        }

        // `share` of the player's health as the element, lessened by their resistance to it; no stagger, no push (the
        // pulse's too: TwinsPulse)
        internal static void Hurt(Player player, Element element, float share)
        {
            HitData hit = new HitData();
            float damage = player.GetMaxHealth() * share;
            if (element == Element.Storm)
            {
                hit.m_damage.m_lightning = damage;
            }
            else
            {
                hit.m_damage.m_fire = damage;
            }
            hit.m_point = player.GetCenterPoint();
            hit.m_dir = Vector3.down;
            hit.m_staggerMultiplier = 0f;
            hit.m_hitType = HitData.HitType.EnemyHit;
            hit.ApplyResistance(player.GetDamageModifiers(), out HitData.DamageModifier modifier);
            player.ApplyDamage(hit, true, true, modifier);
        }
    }
}
