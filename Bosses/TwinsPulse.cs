using NhxMagicWeapons.Staves;
using UnityEngine;
using Storms = NhxMagicWeapons.Staves.StaffOfStorms.Staff;
using Wrath = NhxMagicWeapons.Staves.WrathOfMuspelheim.Staff;

namespace NhxMagicWeapons.Bosses
{
    // Each twin's pulse, its kick for a player who stays close. Claude found neither twin any threat up close (no melee
    // attack, and the Firecaller's flames easiest to step out of at arm's length), and the user (2026-11-20): "can we add
    // some form of melee threat to the bosses? maybe a simple pulsing aoe around them, to discourage players from staying
    // still and just avoiding or running in a tight circle around each boss"; then, of Claude's proposal: "I like it.
    // Let's add a small pushback to it, it will push the player a bit (maybe 2m outside of melee range) and we can keep
    // the damage on a 2s cooldown", with the hunters closing in on their quarry (TwinsAI.Close): "The bosses would hunt
    // the players like football players chase the ball: to kick it a bit".
    // - On its owner's game: once a living player in its fight (not judged) has stayed within Radius of it for Dwell, its
    //   rune lights up at its feet and follows it (ElementRune.Follow) for Warning, then strikes; the next can't begin
    //   until Cooldown after that. Not while it stands for its second phase's coming or its comet's warning
    //   (TwinsPhase.Busy); in any role otherwise, so a watcher too, when its player comes to it
    // - On each player's own game, as the rune strikes there (Kick, from ElementRune): within it and not rolling, Share of
    //   their health as its element, lessened by their resistance to it (TwinsPhase.Hurt: armor would all but cancel a
    //   flat hit this size), no stagger, and pushed straight away from it to PushBeyond past its edge
    // It grows with the twin in its second phase (TwinsPhase.Grown). Its numbers are first guesses, for the co-op playtest.
    // Share was 0.12 until the user (2026-11-22, after test fights: "It just tickles and nudges"; then "The resistances you
    // saw on my player earlier are probably the max we can get to each element. Let's increase the pulse damage to the
    // player takes between 20% and 25% of their max health from it, after resistances and armor"): armor doesn't touch
    // it, and the resistance meads make a player Resistant (half), so 0.45 takes 22.5% from them, 45% from one without
    internal class TwinsPulse : MonoBehaviour
    {
        private const float Radius = 5f;
        private const float Dwell = 1f;
        private const float Warning = 1f;
        private const float Cooldown = 2f;
        private const float Share = 0.45f;
        private const float PushBeyond = 2f;
        // A player this much past its edge as it strikes is a near miss, for the log (meters)
        private const float NearMiss = 1.5f;
        // The Firecaller's blast as it strikes shakes the camera this much (a rune's: 1): it comes every few seconds
        private const float BlastShake = 0.3f;
        private const string StormRuneName = "NhxStormPulseRune";
        private const string FireRuneName = "NhxFirePulseRune";

        private static GameObject s_stormRune;
        private static GameObject s_fireRune;

        private CallerBoss m_boss;
        private ZNetView m_nview;
        private Humanoid m_humanoid;
        // On its owner's game: how long a player has stayed close, when its rune strikes (0: none out), and the earliest
        // the next may begin
        private float m_dwell;
        private float m_strikeAt;
        private float m_next;

        // Its runes, networked: the storm's strikes with the storm's lighter bolt (TwinsStorm's rain), as it comes every few
        // seconds; the fire's erupts in Muspelheim's blast, shaking less
        internal static void AddPrefabs()
        {
            if (s_stormRune)
            {
                return;
            }
            s_stormRune = BossAttacks.AddRune(StormRuneName, false, Warning, Radius, Storms.RainBolt);
            s_fireRune = BossAttacks.AddRune(FireRuneName, true, Warning, Radius);
            foreach (GameObject prefab in new[] { s_stormRune, s_fireRune })
            {
                ElementRune rune = prefab.GetComponent<ElementRune>();
                rune.m_kick = true;
                rune.m_blastShake = BlastShake;
            }
        }

        private void Awake()
        {
            m_boss = GetComponent<CallerBoss>();
            m_nview = GetComponent<ZNetView>();
            m_humanoid = GetComponent<Humanoid>();
        }

        private void Update()
        {
            if (!m_nview || !m_nview.IsValid() || !m_nview.IsOwner() || !m_boss || m_boss.m_abandoned || !m_boss.Running
                || !m_humanoid || m_humanoid.IsDead())
            {
                m_dwell = 0f;
                m_strikeAt = 0f;
                return;
            }
            float now = Time.time;
            if (m_strikeAt > 0f)
            {
                if (now < m_strikeAt)
                {
                    return;
                }
                m_strikeAt = 0f;
                m_next = now + Cooldown;
            }
            if (now < m_next || (m_boss.m_phase && m_boss.m_phase.Busy(out _)))
            {
                m_dwell = 0f;
                return;
            }
            float radius = Radius * (m_boss.m_phase ? m_boss.m_phase.Grown : 1f);
            Player close = Closest(radius);
            if (!close)
            {
                m_dwell = 0f;
                return;
            }
            m_dwell += Time.deltaTime;
            if (m_dwell >= Dwell)
            {
                m_dwell = 0f;
                Begin(radius, close);
            }
        }

        // The nearest living player in its fight within `radius` of it (on the ground), not judged; null for none
        private Player Closest(float radius)
        {
            Player closest = null;
            float best = radius;
            Vector3 position = transform.position;
            foreach (Player player in Player.GetAllPlayers())
            {
                if (!player || player.IsDead() || Judged(player) || !TwinsRing.Inside(m_boss, player.transform.position))
                {
                    continue;
                }
                float distance = Utils.DistanceXZ(player.transform.position, position);
                if (distance <= best)
                {
                    best = distance;
                    closest = player;
                }
            }
            return closest;
        }

        // Its rune at its feet, following it, and its element flaring at its middle (networked)
        private void Begin(float radius, Player close)
        {
            bool storm = m_boss.Element == Element.Storm;
            GameObject prefab = storm ? s_stormRune : s_fireRune;
            ElementRune rune = prefab ? ElementRune.Create(prefab, transform.position, Warning, radius) : null;
            // Without its rune, nothing to strike: it tries again after its cooldown, not every frame
            m_next = Time.time + Cooldown;
            if (!rune)
            {
                return;
            }
            rune.Follow(m_humanoid);
            m_strikeAt = Time.time + Warning;
            GameObject flare = storm ? Storms.Crackle : Wrath.Shot;
            if (flare)
            {
                StaffEffects.Create(new[] { flare }, m_humanoid.GetCenterPoint(), transform.rotation);
            }
            TwinsLog.Event($"{TwinsLog.Name(m_boss)} pulses: {TwinsLog.Name(close)} stayed within {radius:0.#} m of it for {Dwell:0.#} s; "
                + $"it strikes in {Warning:0.#} s.");
        }

        // On every game, as a pulse's rune strikes there (ElementRune.Strike): this game's player, if they're within
        // `radius` of `point` (where its twin stands here) and not rolling, hurt and pushed away
        internal static void Kick(Character caster, Vector3 point, float radius)
        {
            Player player = Player.m_localPlayer;
            CallerBoss boss = CallerBoss.Of(caster);
            if (!player || player.IsDead() || !boss || caster.IsDead() || Judged(player)
                || !TwinsRing.Inside(boss, player.transform.position))
            {
                return;
            }
            Collider body = player.GetCollider();
            if (!body)
            {
                return;
            }
            float distance = Vector3.Distance(body.ClosestPoint(point), point);
            string pulse = $"the {TwinsLog.Name(caster)}'s pulse ({radius:0.#} m)";
            if (distance > radius)
            {
                if (distance <= radius + NearMiss)
                {
                    TwinsLog.Event($"{TwinsLog.Name(player)} is clear of {pulse} by {distance - radius:0.0} m as it strikes.");
                }
                return;
            }
            // Rolling through it (StaffHits.IsValidTarget asks IsDodgeInvincible), or anything else that spares them
            if (!StaffHits.IsValidTarget(caster, player))
            {
                TwinsLog.Event($"{TwinsLog.Name(player)} {(player.IsDodgeInvincible() ? "rolls through" : "is spared by")} {pulse}, {radius - distance:0.0} m inside its edge.");
                return;
            }
            TwinsPhase.Hurt(player, boss.Element, Share);
            Vector3 flat = player.transform.position - point;
            flat.y = 0f;
            float from = flat.magnitude;
            Vector3 away = from > 0.1f ? flat / from : -player.transform.forward;
            float reach = Mathf.Max(0f, radius + PushBeyond - from);
            Vector3 push = away * (reach * TwinsPhase.PushWane / TwinsPhase.PushSpeed);
            if (player.m_pushForce.sqrMagnitude < push.sqrMagnitude)
            {
                player.m_pushForce = push;
            }
            TwinsLog.Event($"{TwinsLog.Name(player)} is kicked by {pulse}: {Share * 100f:0}% of their health before resistance, {from:0.#} m from it, pushed about {reach:0.#} m.");
        }

        private static bool Judged(Player player)
        {
            ZDO zdo = player.m_nview && player.m_nview.IsValid() ? player.m_nview.GetZDO() : null;
            return zdo != null && zdo.GetBool(TwinsJudgment.JudgedKey);
        }
    }
}
