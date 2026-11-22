using System.Collections.Generic;
using HarmonyLib;
using NhxMagicWeapons.Staves;
using NhxMagicWeapons.Staves.StaffOfStorms;
using NhxMagicWeapons.Staves.WrathOfMuspelheim;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // On each boss (CallerBosses), on every game:
    // - Its armor's level, which the legendary looks go by (Armor\ArmorLegend: particles from 12, the cloak from 15, the
    //   signatures at 20...): its pieces' level, set from its ZDO (20 unless nhx_boss says otherwise). Every game gives a
    //   monster its default items itself, at level 1; the owner's are the ones that count, and ArmorLegend shares the
    //   set's level from there
    // - Its set glowing fully and crackling, as with the set's staff in hand (Armor\ArmorGlow.Wielding asks Empowered)
    // - Its legs' pace: the player's walk and jog animations are picked by the speed the game gives them (forward_speed,
    //   sideway_speed, in m/s), and its legs are twice as long, so at twice a player's speed the animations are given
    //   the player's (StrideScale), and its feet stay on the ground
    // And on the game that runs it (its owner): its part in the twins' fight (TwinsAI picks its role and target every
    // RoleInterval), its answer to a player drawing its staff (RPC_Punish: TwinsDuel), its bond with its twin
    // (TwinsBond: raising it if it falls), and its ward against its own element's staff (Ward: TwinsRules)
    internal class CallerBoss : MonoBehaviour
    {
        internal const int DefaultLevel = 20;
        internal static readonly int LevelKey = "nhx_boss_level".GetStableHashCode();
        internal const string PunishRpc = "NhxTwinsPunish";
        private const string WardRpc = "NhxTwinsWard";
        internal const string PresentRpc = "NhxTwinsPresent";

        // Its element (Element: the staff whose drawing it answers)
        public int m_element;

        internal enum Role
        {
            None,
            // After the player who drew its staff last, or alongside its twin
            Hunter,
            // Alone with one player who holds the other staff (or none): keeping back, slow spells only (TwinsAI)
            Watcher,
            // A player the elements have judged: standing, facing them, channelling (TwinsJudgment draws and deals it)
            Judging,
        }

        // It lands this far short of the player it rushes at (meters, from their middle), and its rush shows as the staff's
        // look at this level
        private const float ArriveGap = 4f;
        private const int RushLevel = 20;
        private const float RoleInterval = 0.5f;
        // A hunter whose quarry has stayed out of reach (meters: a little past the Firecaller's flames, the Stormcaller's
        // bolt) for ChaseDelay seconds rushes at it, as at a player who drew its staff, at most every RushCooldown (the
        // user, 2026-11-14: "the bosses might want to blink closer from time to time anyway"): a player can outrun them
        // (sprinting, 7 m/s; they run at 5.5: CallerBosses.RunSpeed), or stand where they can't walk
        private const float FireReach = 14f;
        private const float StormReach = 26f;
        private const float ChaseDelay = 4f;
        private const float RushCooldown = 10f;
        // Where a chasing Stormcaller lands: well within its bolt's reach, not at arm's length (a caster; the Firecaller, and
        // both when punishing a swap, land at ArriveGap)
        private const float StormChaseGap = 12f;
        // A watcher the player keeps hurting flashes behind its hunting twin (Claude's review, 2026-11-17, its #4, built at
        // the user's word: "go ahead with 4% and 8s, both twins"): alone with one player, the twin they can hurt stood still
        // between its spells, and the staves hit at once, so their half of the fight asked nothing of them. Once it has
        // lost FlashShare of its health since its last flash (or since it began to watch), at most every FlashCooldown, and
        // not while it attacks, stands for its second phase or its comet, or answers a swap, it flashes (its rush) to
        // FlashBehind beyond its twin on the line from the player, no further than FlashMaxFromPlayer from them: the twin
        // chasing them can't be hurt by their staff and stops shots aimed through it, so they have to move, or drag it
        // aside, to have the watcher in sight again. A share of its health, so CLLC's scaling changes nothing of it
        // (Evade). No further than 20 m from them since the ring shrank to 30 m (2026-11-22): at 30, in a test fight, the
        // watcher's 21 flashes went 25 to 39 m and often left it at the arena's edge
        private const float FlashShare = 0.04f;
        private const float FlashCooldown = 8f;
        private const float FlashBehind = 8f;
        private const float FlashMaxFromPlayer = 20f;
        // Its ward shows at most this often (seconds), and for a hit through its middle (a blast, Lightning Step, the
        // comet: nearer its axis than WardNear), this far from it toward whoever struck (meters)
        private const float WardEvery = 0.15f;
        private const float WardNear = 0.3f;
        private const float WardReach = 1f;

        // Every boss on this game (BossCommand and the fight look for them here)
        internal static readonly List<CallerBoss> All = new List<CallerBoss>();
        // Its fight was left behind: it goes as it loads (Awake, Start)
        internal bool m_abandoned;
        private static readonly HashSet<VisEquipment> s_equipment = new HashSet<VisEquipment>();
        private static readonly Dictionary<ZSyncAnimation, Transform> s_animations = new Dictionary<ZSyncAnimation, Transform>();
        internal static readonly Dictionary<BaseAI, CallerBoss> ByAI = new Dictionary<BaseAI, CallerBoss>();
        private static readonly Dictionary<Character, CallerBoss> s_twins = new Dictionary<Character, CallerBoss>();

        // Whether this character is one of the twins (TwinsRules)
        internal static bool IsTwin(Character character) => character && s_twins.ContainsKey(character);

        // The twin this character is, or null: without a GetComponent, for what asks every frame or every hit (BossBars,
        // TwinsRules)
        internal static CallerBoss Of(Character character)
        {
            return character && s_twins.TryGetValue(character, out CallerBoss boss) ? boss : null;
        }

        internal ZNetView m_nview;
        internal MonsterAI m_ai;
        internal Humanoid m_humanoid;
        // Its second phase (TwinsPhase)
        internal TwinsPhase m_phase;
        // On its owner's game: its role and whom it goes after (null: as its AI would)
        internal Role m_role;
        internal Character m_target;
        // On its owner's game: until when it holds a taunt for the one it's after (Time.time; 0: none. TwinsVoices)
        internal float m_tauntHeld;
        private VisEquipment m_equipment;
        private ZSyncAnimation m_animation;
        private int m_level;
        private float m_roleTimer;
        private float m_outOfReach;
        private float m_nextRush;
        // A watcher's health as it last flashed or began to watch (a share; -1: not watching), and its next flash's
        // earliest time (Evade)
        private float m_flashFrom = -1f;
        private float m_nextFlash;
        private float m_lastWard = -WardEvery;
        // Its answer to a clean staff's draw, on its way: when its rune strikes, and where (RPC_Punish, Arrive)
        private float m_answerAt;
        private Vector3 m_answerPoint;

        // On its owner's game: when a living player was last known in its fight (Present; TwinsRules.CheckAbandoned).
        // Not yet: this game has only just begun to run it
        private float m_presentAt = -1f;

        // Its timers go on where they were when another game takes it over (Claude's review, 2026-11-17, its #2, built at
        // the user's word: "go ahead with #2"): a player whose game ran it dies and respawns far away, the other's game
        // runs it from then, and until then its timers were on the first game only (Time.time, the attack items' own),
        // so the second found every attack, rush and spell ready, and loosed them at once on the player left. They're
        // kept in its ZDO too, in the network's time every game shares (Save, SetNextRush, TwinsPhase), and a game that
        // begins to run it takes them up from there (Resume). Whether this game has, since it began to run it
        private bool m_running;
        // Until then it starts nothing (TwinsAI's attacks, TwinsPhase's spells): the AI and the phase may run before this
        // in the frame this game takes it over, and would find everything ready
        internal bool Running => m_running;
        // When each attack item was last used, as last written to its ZDO (Save)
        private readonly Dictionary<ItemDrop.ItemData, float> m_savedAttacks = new Dictionary<ItemDrop.ItemData, float>();
        private static readonly int s_nextRushKey = "nhx_twin_next_rush".GetStableHashCode();
        private static readonly Dictionary<string, int> s_attackKeys = new Dictionary<string, int>();

        // The network's time, every game's alike (ticks): `seconds` from now, and how far a moment of it is from now
        internal static long NetworkIn(float seconds) => (ZNet.instance ? ZNet.instance.GetTime().Ticks : 0L) + System.TimeSpan.FromSeconds(seconds).Ticks;

        internal static float SecondsUntil(long at) => (float)System.TimeSpan.FromTicks(at - (ZNet.instance ? ZNet.instance.GetTime().Ticks : 0L)).TotalSeconds;

        // A living player is in its fight: their own game says so (PresentRpc, from TwinsDuel), or this one sees them
        internal void Present() => m_presentAt = Time.time;

        // How long since one was (seconds), counted from now if this game hasn't known of any yet
        internal float SincePresent()
        {
            if (m_presentAt < 0f)
            {
                m_presentAt = Time.time;
            }
            return Time.time - m_presentAt;
        }

        // Its rune is out for a clean staff's draw: it starts no attack until it has struck (TwinsAI), so it's free to
        // land on it
        internal bool Answering => m_answerAt > 0f;

        internal Element Element => (Element)m_element;

        // A boss's equipment: its set glows as with its staff in hand
        internal static bool Empowered(VisEquipment equipment) => s_equipment.Contains(equipment);

        // How far the nearest living boss (of `element`, or any for None) is from `point`, and which: infinity for none
        internal static float NearestLiving(Vector3 point, Element element, out CallerBoss nearest)
        {
            nearest = null;
            float best = float.PositiveInfinity;
            foreach (CallerBoss boss in All)
            {
                if (!boss || !boss.m_humanoid || boss.m_humanoid.IsDead() || !boss.m_nview || !boss.m_nview.IsValid()
                    || (element != Element.None && boss.Element != element))
                {
                    continue;
                }
                float distance = Vector3.Distance(point, boss.transform.position);
                if (distance < best)
                {
                    best = distance;
                    nearest = boss;
                }
            }
            return best;
        }

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            m_ai = GetComponent<MonsterAI>();
            m_humanoid = GetComponent<Humanoid>();
            m_equipment = GetComponent<VisEquipment>();
            m_animation = GetComponent<ZSyncAnimation>();
            m_phase = GetComponent<TwinsPhase>();
            // A fight left behind ends as it loads again, before anything of it happens (TwinsRules.LeftBehind): kept out
            // of the twins the fight goes by (All: holds, the players' side, the spells), not shown as a boss (its bar
            // would bring the fight's music), its AI held (TwinsAI) and nothing of its own run (Update, TwinsPhase), then
            // gone in Start
            m_abandoned = m_nview && m_nview.GetZDO() != null && TwinsRules.LeftBehind(m_nview.GetZDO());
            if (m_abandoned)
            {
                if (m_humanoid)
                {
                    m_humanoid.m_boss = false;
                }
            }
            else
            {
                All.Add(this);
            }
            if (m_humanoid)
            {
                s_twins[m_humanoid] = this;
            }
            if (m_equipment)
            {
                s_equipment.Add(m_equipment);
            }
            if (m_animation)
            {
                s_animations[m_animation] = transform;
            }
            if (m_ai)
            {
                ByAI[m_ai] = this;
            }
            if (m_nview)
            {
                m_nview.Register<ZDOID, bool>(PunishRpc, RPC_Punish);
                m_nview.Register<Vector3>(WardRpc, RPC_Ward);
                m_nview.Register(PresentRpc, sender => Present());
                TwinsBond.Register(this);
                TwinsVoices.Register(this);
            }
        }

        // After every component's Awake (its health is set up): a twin just raised comes back hurt (TwinsBond)
        private void Start()
        {
            if (m_abandoned)
            {
                // Whichever game loads it first sends it away (only a ZDO's owner can destroy it)
                if (m_nview && m_nview.IsValid())
                {
                    if (!m_nview.IsOwner())
                    {
                        m_nview.ClaimOwnership();
                    }
                    TwinsRules.Vanish(this, "its fight was left (nobody in it before it unloaded)");
                }
                return;
            }
            if (m_nview && m_nview.IsValid() && m_nview.IsOwner() && m_humanoid)
            {
                TwinsBond.OnStart(this);
            }
        }

        private void OnDestroy()
        {
            All.Remove(this);
            if (m_humanoid)
            {
                s_twins.Remove(m_humanoid);
            }
            if (m_equipment)
            {
                s_equipment.Remove(m_equipment);
            }
            if (m_animation)
            {
                s_animations.Remove(m_animation);
            }
            if (m_ai)
            {
                ByAI.Remove(m_ai);
            }
        }

        private void Update()
        {
            if (m_abandoned || !m_nview || !m_nview.IsValid() || !m_humanoid)
            {
                return;
            }
            UpdateLevel();
            if (!m_nview.IsOwner() || m_humanoid.IsDead())
            {
                m_role = Role.None;
                m_target = null;
                m_tauntHeld = 0f;
                m_answerAt = 0f;
                m_presentAt = -1f;
                m_running = false;
                return;
            }
            if (!m_running)
            {
                Resume();
            }
            if (m_answerAt > 0f && Time.time >= m_answerAt)
            {
                m_answerAt = 0f;
                Arrive();
            }
            m_roleTimer -= Time.deltaTime;
            if (m_roleTimer <= 0f)
            {
                m_roleTimer = RoleInterval;
                if (TwinsRules.CheckAbandoned(this))
                {
                    return;
                }
                Save();
                TwinsAI.UpdateRole(this);
                TwinsBond.Update(this);
                Chase();
                Evade();
            }
        }

        // A hunter rushes at a quarry that stays out of reach (every RoleInterval)
        private void Chase()
        {
            Character target = m_target;
            if (m_role != Role.Hunter || !target || target.IsDead() || m_humanoid.InAttack())
            {
                m_outOfReach = 0f;
                return;
            }
            // Standing for its second phase's coming or its comet's warning (TwinsPhase.Busy), it can't rush: the time
            // isn't counted and no rush is tried, so none is spent (with its RushCooldown) on a rush that can't happen.
            // Claude's audit, 2026-11-16, applied at the user's word ("Apply fixes for #1 and #3"): once the coming
            // took 5 s, a quarry kept out of reach through it cost the twin its chase for up to 10 s after
            if (m_phase && m_phase.Busy(out _))
            {
                return;
            }
            float reach = Element == Element.Fire ? FireReach : StormReach;
            if (Vector3.Distance(target.transform.position, transform.position) <= reach)
            {
                m_outOfReach = 0f;
                return;
            }
            m_outOfReach += RoleInterval;
            if (m_outOfReach >= ChaseDelay && Time.time >= m_nextRush)
            {
                m_outOfReach = 0f;
                SetNextRush(RushCooldown);
                TwinsLog.Event($"{TwinsLog.Name(this)} has been out of reach of {TwinsLog.Name(target)} for {ChaseDelay:0} s.");
                Rush(target.transform.position, Element == Element.Storm ? StormChaseGap : ArriveGap, TwinsLog.Name(target));
            }
        }

        // A watcher hurt by FlashShare of its health since its last flash flashes behind its twin (every RoleInterval)
        private void Evade()
        {
            if (m_role != Role.Watcher || !(m_target is Player player) || player.IsDead())
            {
                m_flashFrom = -1f;
                return;
            }
            float health = m_humanoid.GetHealthPercentage();
            // Just begun to watch (or healed): counted from here
            if (m_flashFrom < health)
            {
                m_flashFrom = health;
            }
            if (m_flashFrom - health < FlashShare || Time.time < m_nextFlash || m_humanoid.InAttack() || Answering
                || (m_phase && m_phase.Busy(out _)))
            {
                return;
            }
            NearestLiving(transform.position, TwinsDuel.Other(Element), out CallerBoss twin);
            if (!twin)
            {
                return;
            }
            // Beyond its twin on the line from the player (or from the player through itself, its twin at their feet)
            Vector3 from = player.transform.position;
            Vector3 away = twin.transform.position - from;
            away.y = 0f;
            if (away.sqrMagnitude < 1f)
            {
                away = transform.position - from;
                away.y = 0f;
            }
            if (away.sqrMagnitude < 0.01f)
            {
                return;
            }
            Vector3 spot = from + away.normalized * Mathf.Min(away.magnitude + FlashBehind, FlashMaxFromPlayer);
            TwinsLog.Event($"{TwinsLog.Name(this)} has lost {100f * (m_flashFrom - health):0.#}% of its health since it last flashed or began to watch: "
                + $"it flashes behind the {TwinsLog.Name(twin)}.");
            Rush(spot, 0f, "a spot behind its twin");
            m_nextFlash = Time.time + FlashCooldown;
            m_flashFrom = health;
        }

        // Its next chase can't come within `seconds`: here, and in its ZDO for a game that takes it over
        private void SetNextRush(float seconds)
        {
            m_nextRush = Time.time + seconds;
            m_nview.GetZDO().Set(s_nextRushKey, NetworkIn(seconds));
        }

        // Its ZDO key for when an attack item was last used
        private static int AttackKey(ItemDrop.ItemData item)
        {
            string name = item.m_shared.m_name;
            if (!s_attackKeys.TryGetValue(name, out int key))
            {
                key = ("nhx_twin_attack_" + name).GetStableHashCode();
                s_attackKeys[name] = key;
            }
            return key;
        }

        // Its attacks (the items its AI picks them by: BossAttacks), with what their use is timed by
        private IEnumerable<ItemDrop.ItemData> Attacks()
        {
            Inventory inventory = m_humanoid.GetInventory();
            if (inventory == null)
            {
                yield break;
            }
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item != null && item.m_shared.m_aiAttackInterval > 0f)
                {
                    yield return item;
                }
            }
        }

        // On its owner's game, every RoleInterval: when its attacks were last used, those that were since it last looked,
        // in its ZDO (the game times them by the item's m_lastAttackTime: BaseAI.CanUseAttack, MonsterAI)
        private void Save()
        {
            ZDO zdo = m_nview.GetZDO();
            foreach (ItemDrop.ItemData item in Attacks())
            {
                if (item.m_lastAttackTime <= 0f || (m_savedAttacks.TryGetValue(item, out float saved) && saved == item.m_lastAttackTime))
                {
                    continue;
                }
                m_savedAttacks[item] = item.m_lastAttackTime;
                zdo.Set(AttackKey(item), NetworkIn(item.m_lastAttackTime - Time.time));
            }
        }

        // As this game begins to run it (from its first moment, or taking it over): its timers from its ZDO, where the
        // game that ran it before left them. Its attacks' wait until its items are there (Humanoid.Start)
        private void Resume()
        {
            int items = 0;
            List<string> resumed = new List<string>();
            ZDO zdo = m_nview.GetZDO();
            foreach (ItemDrop.ItemData item in Attacks())
            {
                items++;
                long at = zdo.GetLong(AttackKey(item));
                if (at != 0L)
                {
                    item.m_lastAttackTime = Time.time + SecondsUntil(at);
                    m_savedAttacks[item] = item.m_lastAttackTime;
                    float wait = item.m_shared.m_aiAttackInterval - (Time.time - item.m_lastAttackTime);
                    resumed.Add($"{item.m_shared.m_name.Replace("NhxStormcaller", "").Replace("NhxFirecaller", "").ToLowerInvariant()} "
                        + (wait > 0f ? $"in {wait:0.#} s" : "ready"));
                }
            }
            if (items == 0)
            {
                return;
            }
            m_running = true;
            long rush = zdo.GetLong(s_nextRushKey);
            if (rush != 0L)
            {
                m_nextRush = Time.time + SecondsUntil(rush);
                float wait = m_nextRush - Time.time;
                resumed.Add("rush " + (wait > 0f ? $"in {wait:0.#} s" : "ready"));
            }
            string spells = m_phase ? m_phase.Resume() : null;
            if (spells != null)
            {
                resumed.Add(spells);
            }
            if (resumed.Count > 0)
            {
                TwinsLog.Event($"{TwinsLog.Name(this)} is run by this game now: its timers go on where they were ({string.Join(", ", resumed)}).");
            }
        }

        private void UpdateLevel()
        {
            int level = Mathf.Max(1, m_nview.GetZDO().GetInt(LevelKey, DefaultLevel));
            if (level == m_level)
            {
                return;
            }
            // Its items come in Humanoid.Start; until then, nothing to set
            ItemDrop.ItemData[] pieces = { m_humanoid.m_helmetItem, m_humanoid.m_chestItem, m_humanoid.m_legItem, m_humanoid.m_shoulderItem };
            if (System.Array.IndexOf(pieces, null) >= 0)
            {
                return;
            }
            foreach (ItemDrop.ItemData piece in pieces)
            {
                piece.m_quality = level;
            }
            m_level = level;
        }

        // A player drew its staff after the other (TwinsDuel.Draw), and it answers (the fight's redesign, agreed with the
        // user 2026-11-15, its change 3). Its targeting follows from the draw (TwinsAI). On its owner's game:
        // - A staff still charged: smitten at once, unavoidable, then the boss is on them in a flash, a blink for the
        //   Stormcaller (Lightning Step's), a comet for the Firecaller (the Firecaller's Regalia's); until the redesign,
        //   every swap's answer
        // - A clean staff: its rune under them (ElementRune, the size and warning TwinsDuel gives), which strikes whoever
        //   is still on it; as it strikes, the twin lands on it, if it's free (Arrive), and hunts from there
        private void RPC_Punish(long sender, ZDOID playerID, bool clean)
        {
            if (!m_nview.IsOwner() || !m_humanoid || m_humanoid.IsDead())
            {
                return;
            }
            GameObject found = ZNetScene.instance ? ZNetScene.instance.FindInstance(playerID) : null;
            Player player = found ? found.GetComponent<Player>() : null;
            if (!player || player.IsDead())
            {
                return;
            }
            m_roleTimer = 0f;
            // A taunt it held for someone else goes unsaid: this player has its staff now, and it turns to them as soon
            // as their draw reaches this game, a moment after this call does (TwinsVoices)
            if (player != m_target)
            {
                m_tauntHeld = 0f;
            }
            if (!clean)
            {
                TwinsLog.Event($"{TwinsLog.Name(this)} punishes {TwinsLog.Name(player)}'s swap to a staff still charged: a smite of {TwinsDuel.SwapDamage:0}, then a rush.");
                TwinsVoices.Impatient(this, player);
                TwinsDuel.Smite(Element, player, m_humanoid, TwinsDuel.SwapDamage, TwinsDuel.SmiteRadius);
                Rush(player.transform.position, ArriveGap, TwinsLog.Name(player));
                SetNextRush(RushCooldown);
                return;
            }
            Vector3 point = LightningStrike.Ground(player.transform.position);
            GameObject prefab = Element == Element.Storm ? TwinsDuel.s_stormRune : TwinsDuel.s_fireRune;
            ElementRune rune = prefab ? ElementRune.Create(prefab, point, TwinsDuel.AnswerDelay, TwinsDuel.AnswerRadius) : null;
            if (rune)
            {
                rune.SetHit(m_humanoid, TwinsDuel.AnswerHit(Element, m_humanoid));
            }
            m_answerAt = Time.time + TwinsDuel.AnswerDelay;
            m_answerPoint = point;
            TwinsLog.Event($"{TwinsLog.Name(this)} answers {TwinsLog.Name(player)}'s swap to a clean staff: its rune at {TwinsLog.At(point)}"
                + $"{(rune ? "" : " (missing)")}, striking for {TwinsDuel.AnswerDamage:0} in {TwinsDuel.AnswerDelay:0.0} s.");
        }

        // Its rune strikes (RPC_Punish), and it lands on it: unless it's in the middle of an attack, or stands for its
        // second phase's coming or its comet's warning (Rush), when the rune strikes without it and it hunts once it's done
        private void Arrive()
        {
            if (m_humanoid.InAttack())
            {
                TwinsLog.Event($"{TwinsLog.Name(this)}'s rune strikes without it: in the middle of an attack.");
                return;
            }
            Rush(m_answerPoint, AnswerGap, "its rune");
            SetNextRush(RushCooldown);
        }

        // It lands this far short of its rune's middle (meters): on the rune, not on a player who stayed there
        private const float AnswerGap = 1.5f;

        // Its own element's staff struck it at `point`, to nothing, or any staff as its second phase comes
        // (TwinsRules.StavesOnly, on its owner's game): its ward flashes there on every game (WardFlash), so whoever struck
        // sees why no number came
        internal void Ward(Vector3 point, Character attacker)
        {
            if (Time.time - m_lastWard < WardEvery || !m_nview || !m_nview.IsValid() || !m_humanoid)
            {
                return;
            }
            m_lastWard = Time.time;
            Vector3 center = m_humanoid.GetCenterPoint();
            Vector3 flat = point - center;
            flat.y = 0f;
            if (flat.sqrMagnitude < WardNear * WardNear && attacker)
            {
                flat = attacker.GetCenterPoint() - center;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f)
                {
                    point = center + flat.normalized * WardReach;
                }
            }
            m_nview.InvokeRPC(ZNetView.Everybody, WardRpc, point);
        }

        private void RPC_Ward(long sender, Vector3 point)
        {
            WardFlash.Show(this, point);
        }

        // In a flash to `gap` short of `point`, along the ground (LightningStep.Walk), drawn as its element's; `toward`
        // for the log
        private void Rush(Vector3 point, float gap, string toward)
        {
            // Not while it stands for its second phase's coming or its comet's warning (TwinsPhase)
            if (m_phase && m_phase.Busy(out _))
            {
                TwinsLog.Event($"{TwinsLog.Name(this)}'s rush: busy with its second phase, none.");
                return;
            }
            Vector3 start = transform.position;
            Vector3 flat = point - start;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance <= gap + 1f)
            {
                return;
            }
            Vector3 dir = flat / distance;
            if (!Flash(dir, distance - gap, out Vector3 from, out Vector3 to))
            {
                TwinsLog.Event($"{TwinsLog.Name(this)}'s rush at {toward}: nowhere to land, none.");
                return;
            }
            TwinsLog.Event($"{TwinsLog.Name(this)} rushes at {toward}: {Vector3.Distance(from, to):0} m.");
            Quaternion rotation = Quaternion.LookRotation(dir);
            if (Element == Element.Storm)
            {
                if (LightningStep.s_bolt)
                {
                    LightningBolt.Arc(LightningStep.s_bolt, m_humanoid, from, to, RushLevel);
                }
                StaffEffects.Create(LightningStep.s_leaveEffects, from, rotation);
                StaffEffects.Create(LightningStep.s_landEffects, to, rotation);
            }
            else
            {
                if (CometDash.s_comet)
                {
                    FireComet.Create(CometDash.s_comet, from, to, RushLevel);
                }
                StaffEffects.Create(CometDash.s_leaveEffects, from, rotation);
            }
        }

        // The twin moved `reach` along `dir` at once, facing it, and where its middle was and is (for the look): along the
        // ground, as Lightning Step goes; but where something's in the way (rocks, a tree), it doesn't stop there: it
        // flashes over it to a free spot near where it meant to land (the user, 2026-11-15: debris "prevented the bosses
        // from teleporting next to me"). False: nowhere to go. The rush's, and the Firecaller's comet in its second phase
        // (TwinsPhase). An arena's twin lands within its ring (TwinsRing: the walk ends at the edge, and so does the aim)
        internal bool Flash(Vector3 dir, float reach, out Vector3 from, out Vector3 to)
        {
            Vector3 start = transform.position;
            from = to = m_humanoid.GetCenterPoint();
            Vector3 aim = TwinsRing.Clamp(m_humanoid, start + dir * reach);
            bool walked = LightningStep.Walk(m_humanoid, start, dir, reach, out Vector3 end);
            if (!walked || Utils.DistanceXZ(end, aim) > ShortOfAim)
            {
                if (FindStand(aim, out Vector3 stand))
                {
                    end = stand;
                }
                else if (!walked)
                {
                    return false;
                }
            }
            to = end + (from - start);
            Quaternion rotation = Quaternion.LookRotation(dir);
            transform.SetPositionAndRotation(end, rotation);
            Rigidbody body = GetComponent<Rigidbody>();
            if (body)
            {
                body.position = end;
                body.rotation = rotation;
            }
            return true;
        }

        // A rush that walks this much short of where it aimed looks for a free spot there instead (meters)
        private const float ShortOfAim = 2f;
        // Rings of spots about the aim tried for one (meters apart, how many rings, how many spots on each)
        private const float StandStep = 1.5f;
        private const int StandRings = 4;
        private const int StandSpots = 8;

        // A spot near `aim` on the ground, out of deep water, with room for the twin to stand (its capsule at its size)
        private bool FindStand(Vector3 aim, out Vector3 stand)
        {
            stand = aim;
            CapsuleCollider capsule = m_humanoid.GetCollider() as CapsuleCollider;
            float scale = transform.localScale.y;
            float radius = (capsule ? capsule.radius : 0.5f) * scale * 0.9f;
            float height = (capsule ? capsule.height : 1.85f) * scale;
            float water = ZoneSystem.instance ? ZoneSystem.instance.m_waterLevel : float.MinValue;
            for (int ring = 0; ring <= StandRings; ring++)
            {
                int spots = ring == 0 ? 1 : StandSpots;
                for (int i = 0; i < spots; i++)
                {
                    Vector3 point = aim + Quaternion.AngleAxis(360f * i / spots, Vector3.up) * Vector3.forward * (StandStep * ring);
                    if (!ZoneSystem.instance || !ZoneSystem.instance.GetGroundHeight(point, out float ground) || ground < water - 0.5f
                        || TwinsRing.Stops(m_humanoid, point))
                    {
                        continue;
                    }
                    point.y = ground;
                    Vector3 bottom = point + Vector3.up * (radius + 0.3f);
                    Vector3 top = point + Vector3.up * Mathf.Max(radius + 0.3f, height - radius);
                    if (!Physics.CheckCapsule(bottom, top, radius, StaffHits.SightMask, QueryTriggerInteraction.Ignore))
                    {
                        stand = point;
                        return true;
                    }
                }
            }
            return false;
        }

        // The movement speeds the animations are given, divided by the boss's size (its scale, which the world's
        // modifiers can change a little: Character.Awake). The owner's game sets them, and others take them from its ZDO
        [HarmonyPatch(typeof(ZSyncAnimation), nameof(ZSyncAnimation.SetFloat), typeof(int), typeof(float))]
        private static class StrideScale
        {
            private static readonly int s_forward = ZSyncAnimation.GetHash("forward_speed");
            private static readonly int s_sideway = ZSyncAnimation.GetHash("sideway_speed");

            private static void Prefix(ZSyncAnimation __instance, int hash, ref float value)
            {
                if ((hash == s_forward || hash == s_sideway) && s_animations.TryGetValue(__instance, out Transform boss) && boss)
                {
                    value /= Mathf.Max(0.01f, boss.localScale.y);
                }
            }
        }
    }
}
