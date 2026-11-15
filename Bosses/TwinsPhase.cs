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
    // - Its coming: the twin stops for TransitionTime, untouchable (TwinsRules), as the storm strikes it or Muspelheim's
    //   blast bursts from it. A raised twin comes back at half health (TwinsBond), so in its second phase at once
    // - The Stormcaller: its shards grow and race around it (Armor\ArmorLegendFx asks IsEnraged); every StormInterval it
    //   calls the storm over the player it isn't hunting (TwinsStorm); and while it lives, players closer than LinkRange
    //   to each other are joined by arcs of lightning that hurt both (UpdatePlayer)
    // - The Firecaller: its wings open and stay (ChannelWings); every CometInterval it marks the player it isn't hunting
    //   with a rune and flames in its hands (CometWindup, standing), then comets through where they stood, CometPast
    //   beyond, hitting whoever is on the way, and the way burns for TrailTime, hurting whoever stands in it
    // - Alone with one player, the twin that watches (TwinsAI) casts its phase's spell at them in place of its own; the
    //   hunter, with no one else, has no one to cast it at. No faster: their speeds and other attacks are as before.
    // - Its twin fallen (the fight's redesign, agreed with the user 2026-11-15, its change 7): its second phase comes at
    //   once, whatever its health (TwinFell), and it goes on casting its spell while it hunts alone: at another player
    //   in the fight, or with no other, at its own quarry (OtherPlayer)
    // Whether it's come, and when, are in its ZDO, for every game's looks; its owner's game decides and casts
    internal class TwinsPhase : MonoBehaviour
    {
        internal const float Threshold = 0.5f;
        private const float TransitionTime = 2f;
        // Its first spell comes this long after the transition; with no one to cast it at, it looks again this often
        private const float FirstAfter = 3f;
        private const float RetryAfter = 1f;
        private const float StormInterval = 20f;
        private const float CometInterval = 14f;
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
        private const float CometPush = 30f;
        private const float TrailTime = 5f;
        private const float TrailWidth = 1.5f;
        // The Firecaller's burst as its phase comes (meters around, camera shake), and how fast its wings' flare dies
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
        // A Stormcaller in its second phase this near joins the players
        private const float LinkFightRadius = 80f;

        private static readonly int s_enragedKey = "nhx_twin_enraged".GetStableHashCode();
        private static readonly int s_enragedAtKey = "nhx_twin_enraged_at".GetStableHashCode();
        private static readonly Dictionary<Character, TwinsPhase> s_phases = new Dictionary<Character, TwinsPhase>();
        private static readonly List<Player> s_partners = new List<Player>();

        private static long Now => ZNet.instance ? ZNet.instance.GetTime().Ticks : 0L;

        private CallerBoss m_boss;
        private ZNetView m_nview;
        private Humanoid m_humanoid;
        // On every game
        private bool m_shown;
        private float m_flare;
        // On the owner's game
        private float m_nextSpell;
        private float m_windup = -1f;
        private Vector3 m_aim;
        private float m_flameTimer;

        internal bool Enraged => m_nview && m_nview.IsValid() && m_nview.GetZDO().GetBool(s_enragedKey);

        // Its phase coming: untouchable, standing
        internal bool Transforming => Enraged && Now - m_nview.GetZDO().GetLong(s_enragedAtKey) < System.TimeSpan.FromSeconds(TransitionTime).Ticks;

        // A twin in its second phase (its shards: Armor\ArmorLegendFx)
        internal static bool IsEnraged(Character character)
        {
            return character && s_phases.TryGetValue(character, out TwinsPhase phase) && phase && phase.Enraged;
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
            if (m_humanoid)
            {
                s_phases[m_humanoid] = this;
            }
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
            if (enraged && !dead && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                Looks(dt);
            }
            if (!m_nview.IsOwner() || dead)
            {
                m_windup = -1f;
                return;
            }
            if (!enraged)
            {
                if (m_humanoid.GetHealthPercentage() <= Threshold)
                {
                    Enrage(null);
                }
                return;
            }
            if (Transforming)
            {
                return;
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

        // On every game: its phase's coming (once, if just now) and its wings
        private void Looks(float dt)
        {
            if (!m_shown)
            {
                m_shown = true;
                long at = m_nview.GetZDO().GetLong(s_enragedAtKey);
                if (Now - at < System.TimeSpan.FromSeconds(TransitionTime + 1f).Ticks)
                {
                    m_flare = 1f;
                    if (m_boss.Element == Element.Fire && Wrath.Beam)
                    {
                        Wrath.Beam.ShowBlastOfRadius(transform.position, FireBurstRadius, FireBurstShake);
                    }
                }
            }
            if (m_boss.Element == Element.Fire)
            {
                ChannelWings.Hold(m_humanoid, true, m_flare);
            }
            m_flare = Mathf.MoveTowards(m_flare, 0f, dt * FlareFade);
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
            // Its line (the user's recordings: TwinsVoices), queued behind its twin's if both come at once
            TwinsVoices.Say(m_boss.Element == Element.Storm ? TwinsVoices.BlueP2 : TwinsVoices.RedP2, transform.position,
                $"the {TwinsLog.Name(m_boss)} enters its second phase");
            m_nextSpell = Time.time + TransitionTime + FirstAfter;
            if (m_boss.Element == Element.Storm)
            {
                // The storm strikes it (networked); its shards grow on every game
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
            m_nextSpell = Time.time + StormInterval;
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
            if (Time.time < m_nextSpell || m_humanoid.InAttack())
            {
                return;
            }
            Player other = OtherPlayer();
            if (!other)
            {
                m_nextSpell = Time.time + RetryAfter;
                return;
            }
            m_nextSpell = Time.time + CometInterval;
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
            Burn(from, to, dir);
        }

        // The players on the way take the comet's hit
        private void Burn(Vector3 from, Vector3 to, Vector3 dir)
        {
            Vector3 path = to - from;
            float length = path.magnitude;
            Vector3 along = length > 0.01f ? path / length : dir;
            foreach (Player player in Player.GetAllPlayers())
            {
                if (!player || player.IsDead())
                {
                    continue;
                }
                Vector3 center = player.GetCenterPoint();
                float t = Mathf.Clamp(Vector3.Dot(center - from, along), 0f, length);
                if ((from + along * t - center).sqrMagnitude > CometWidth * CometWidth)
                {
                    continue;
                }
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
            return player && !player.IsDead() && !Judged(player) && TwinsAI.InReach(m_boss, player);
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

        // The Stormcaller's arcs between players too close to each other
        private static void UpdateLink(Player player, float dt)
        {
            CallerBoss.NearestLiving(player.transform.position, Element.Storm, out CallerBoss storm);
            TwinsPhase phase = storm ? storm.m_phase : null;
            s_partners.Clear();
            if (phase && phase.Enraged && InStormFight(storm, player))
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

        // A player the Stormcaller's arcs reach: within LinkFightRadius of it, or anywhere in its arena's ring (the ring is
        // wider than that from edge to edge; the user, 2026-11-15: "entering the ring during a fight should place the
        // player in combat, being then affected by anything that entails")
        private static bool InStormFight(CallerBoss storm, Player player)
        {
            return Vector3.Distance(player.transform.position, storm.transform.position) <= LinkFightRadius || TwinsAI.InReach(storm, player);
        }

        // `share` of the player's health as the element, lessened by their resistance to it; no stagger, no push
        private static void Hurt(Player player, Element element, float share)
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
