using System;
using System.Collections.Generic;
using System.Reflection;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NhxMagicWeapons.Bosses
{
    // The two armor sets' bosses, a first look. The user's idea (2026-11-14): "invisible player bodies wearing each of the
    // armor sets", each with its set's element, casting "similar to what a boss like Yagluth can do when he raises his
    // hands", a pair summoned in the Ashlands (where the sets are made: the Galdr Table at level 4), each dropping "one
    // specific item for each player, which would be part of crafting the armor". Then, to see them first: "Could we have
    // one of each boss so I can spawn them in game and see what they look like? They don't need much AI, they don't even
    // need to attack, just to walk/run around. They should be 2x the size of a player though."
    // So for now: the player's own body and skeleton (the sets are skinned to the player's bones by name) with the body
    // hidden, wearing a set, twice a player's size; a monster's AI with its attacks (BossAttacks); bosses to the game
    // (their bars, their music: TwinsMusic), dropping nothing themselves (the pair's drops: TwinsBond.Won). Summon the pair
    // at an altar (TwinsAltars) or with "nhx_boss summon" (linked: TwinsBond), or
    // one with devcommands' "spawn NhxStormcallerBoss" / "spawn NhxFirecallerBoss"; nhx_boss also makes them follow you,
    // stay, or change their armor's level (BossCommand). What makes them move as a big player is CallerBoss.
    // The fight's core (the user, 2026-11-14: "build the core loop"): the elements building on players, the swaps and the
    // twins' roles (TwinsDuel, TwinsAI); they die together, or the survivor raises the other (TwinsBond). Built since:
    // only the two staves in hand (TwinsJudgment, TwinsRules); the altar, its arena and the lodestone (TwinsAltars,
    // TwinsItems); the drops; the second phase (TwinsPhase)
    internal static class CallerBosses
    {
        internal const string StormName = "NhxStormcallerBoss";
        internal const string FireName = "NhxFirecallerBoss";

        // Twice a player's size (the user's)
        internal const float Size = 2f;
        // Each twin's health: 5000 at first, then half again (the user, 2026-11-15, after a full fight alone: "Next I want to
        // increase their health by 50%"). The game multiplies what they take by its divisor for the players near (CLLC's
        // health per player on the server)
        private const float Health = 7500f;
        // Their two gaits: a monster's AI moves at m_speed and m_runSpeed, and their legs, twice as long, cover twice the
        // ground for each step (CallerBoss gives the animations half the speed). Walking as the player walks (1.6 m/s,
        // 3.2 for them); running at first as the player jogs (4 m/s, 8 for them), which outran the players (the user: "You
        // were right, they are faster than the players"), so now 5 m/s: faster than a player's jog (4), slower than their
        // sprint (7); the animation goes halfway between walking and jogging
        private const float WalkSpeed = 1.6f * Size;
        private const float RunSpeed = 5f;
        // How far they notice players (meters): seen within their view's angle, or heard
        private const float ViewRange = 40f;
        private const float HearRange = 30f;
        // Turning (degrees a second): half the player's 300, as a heavier body
        private const float TurnSpeed = 150f;
        private const float RunTurnSpeed = 150f;
        // Wandering: how far from where they stand (meters), and how often they set off (seconds, up to half again more)
        private const float WanderRange = 10f;
        private const float WanderInterval = 6f;

        // From the game: the body, and monsters whose settings they take (a Fuling's networking, a Ghost's hit and death
        // effects: an empty armor's wisps, not the player's blood and ragdoll, whose body would show)
        private const string BodyPrefab = "Player";
        private const string MonsterPrefab = "Goblin";
        private const string EffectsPrefab = "Ghost";

        internal static void Add()
        {
            GameObject body = PrefabManager.Instance.GetPrefab(BodyPrefab);
            if (!body || !body.GetComponent<Player>())
            {
                Plugin.Log.LogError($"No {BodyPrefab} prefab in the game: no bosses.");
                return;
            }
            // The fight's stacks and runes (TwinsDuel), its music, then each twin with its element and attacks
            TwinsDuel.Add();
            TwinsMusic.Prepare();
            BossAttacks.AddRunes();
            TwinsStorm.AddPrefabs();
            AddBoss(StormName, "$enemy_nhx_stormcaller", body, Armor.StormcallerArmor.StormPieces, Element.Storm, BossAttacks.AddStormCall(), BossAttacks.AddStormBolt());
            AddBoss(FireName, "$enemy_nhx_firecaller", body, Armor.StormcallerArmor.FirePieces, Element.Fire, BossAttacks.AddFireFlames(), BossAttacks.AddFireSigil());
        }

        private static void AddBoss(string name, string displayName, GameObject body, List<GameObject> pieces, Element element, params GameObject[] attacks)
        {
            if (pieces.Count != 4 || pieces.Contains(null))
            {
                Plugin.Log.LogError($"{name} has no armor to wear: no boss.");
                return;
            }
            GameObject boss = PrefabManager.Instance.CreateClonedPrefab(name, body);
            boss.transform.localScale = Vector3.one * Size;

            // The player's scripts out, a humanoid with the player's settings in (its eyes, effects, the body's
            // equipment...). None of them has run: the clone waits in Jotunn's inactive container
            Player player = boss.GetComponent<Player>();
            Humanoid humanoid = boss.AddComponent<Humanoid>();
            CopySettings(typeof(Character), player, humanoid);
            CopySettings(typeof(Humanoid), player, humanoid);
            Object.DestroyImmediate(boss.GetComponent<PlayerController>());
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(boss.GetComponent<Talker>());
            Object.DestroyImmediate(boss.GetComponent<Skills>());

            humanoid.m_name = displayName;
            // Only fire and lightning hurt them, the staves' elements (the user, 2026-11-15: "let's make the bosses immune
            // to any other type of damage other than fire and lightning (like spirit)"): the staves' spirit and anything a
            // gem adds (frost, poison...) do nothing. But the Wrath of Muspelheim's pierce counts as its fire (the user, told
            // it would lose about 41%: "is it possible to count the Wrath's pierce as fire against the twins?"): it hits as
            // it always has (at once, where fire burns over seconds), and pierce from anything else is taken off the hit
            // (TwinsRules.StavesOnly). Players' immunities don't show in their hits' numbers: a hit of only those shows
            // no damage at all
            humanoid.m_damageModifiers = new HitData.DamageModifiers
            {
                m_blunt = HitData.DamageModifier.Immune,
                m_slash = HitData.DamageModifier.Immune,
                m_pierce = HitData.DamageModifier.Normal,
                m_chop = HitData.DamageModifier.Immune,
                m_pickaxe = HitData.DamageModifier.Immune,
                m_fire = HitData.DamageModifier.Normal,
                m_frost = HitData.DamageModifier.Immune,
                m_lightning = HitData.DamageModifier.Normal,
                m_poison = HitData.DamageModifier.Immune,
                m_spirit = HitData.DamageModifier.Immune,
            };
            humanoid.m_group = "";
            humanoid.m_faction = Character.Faction.Boss;
            // Bosses (since 2026-11-14, "start on step 1"): their bars at the top of the screen (BossBars stacks them), their
            // music (TwinsMusic). Of CLLC (the user: "We will let CLLC affect them"), only its scaling by the players within
            // 200 m reaches them, on each hit: less taken by them, more dealt by them. It rolls a boss's stars and affix
            // only as an offering bowl or a spawner makes it, and the altar makes the twins itself (TwinsBond.Summon, Raise),
            // so they have neither, and its world level (the server's "Fluid") doesn't touch them. Left so (2026-11-15, CLLC
            // 5.0.7 read): the server's chances give every boss 3 to 9 stars and one affix, which would be rolled for each
            // twin apart, and its Twin affix would add a third caller, linked to neither
            humanoid.m_boss = true;
            humanoid.m_bossEvent = TwinsMusic.EventName;
            humanoid.m_health = Health;
            // Its set, and its attack (BossAttacks), an item with no model, which it holds when it attacks. No fists: the
            // AI falls back on the unarmed weapon (Humanoid.GetCurrentWeapon), the player's
            humanoid.m_unarmedWeapon = null;
            List<GameObject> items = new List<GameObject>(pieces);
            foreach (GameObject attack in attacks)
            {
                if (attack)
                {
                    items.Add(attack);
                }
            }
            humanoid.m_defaultItems = items.ToArray();
            humanoid.m_randomWeapon = new GameObject[0];
            humanoid.m_randomArmor = new GameObject[0];
            humanoid.m_randomShield = new GameObject[0];
            humanoid.m_randomSets = new Humanoid.ItemSet[0];
            humanoid.m_randomItems = new Humanoid.RandomItem[0];
            humanoid.m_walkSpeed = WalkSpeed;
            humanoid.m_speed = WalkSpeed;
            humanoid.m_runSpeed = RunSpeed;
            humanoid.m_turnSpeed = TurnSpeed;
            humanoid.m_runTurnSpeed = RunTurnSpeed;
            // Swimming from as deep as a player, at their size
            humanoid.m_swimDepth *= Size;
            Character effects = PrefabManager.Instance.GetPrefab(EffectsPrefab)?.GetComponent<Character>();
            humanoid.m_hitEffects = effects ? effects.m_hitEffects : new EffectList();
            humanoid.m_critHitEffects = effects ? effects.m_critHitEffects : new EffectList();
            humanoid.m_backstabHitEffects = effects ? effects.m_backstabHitEffects : new EffectList();
            humanoid.m_deathEffects = effects ? effects.m_deathEffects : new EffectList();

            // Saved with the world and sent as monsters are; a player's isn't kept when they leave
            GameObject monster = PrefabManager.Instance.GetPrefab(MonsterPrefab);
            ZNetView nview = boss.GetComponent<ZNetView>();
            ZNetView monsterView = monster ? monster.GetComponent<ZNetView>() : null;
            if (monsterView)
            {
                nview.m_persistent = monsterView.m_persistent;
                nview.m_type = monsterView.m_type;
                nview.m_syncInitialScale = monsterView.m_syncInitialScale;
            }
            else
            {
                nview.m_persistent = true;
            }
            // Loaded as far as the game loads anything (its distant objects: the simulation distance's far zones too), not
            // only within its near zones as monsters are: the ring is 80 m across, and at the smallest simulation
            // distance a twin at one edge could be gone from the game of a player at the other, which would then count
            // that player out of the fight (TwinsRing asks the twins on its game), free of the ring's hold and of its
            // charges. The game that runs it is still one that has it near
            nview.m_distant = true;
            ZSyncTransform sync = boss.GetComponent<ZSyncTransform>();
            ZSyncTransform monsterSync = monster ? monster.GetComponent<ZSyncTransform>() : null;
            if (sync && monsterSync)
            {
                sync.m_syncPosition = monsterSync.m_syncPosition;
                sync.m_syncRotation = monsterSync.m_syncRotation;
                sync.m_syncScale = monsterSync.m_syncScale;
                sync.m_syncBodyVelocity = monsterSync.m_syncBodyVelocity;
                sync.m_characterParentSync = monsterSync.m_characterParentSync;
            }

            // The body hidden: the armor alone, the hood's eyes glowing in it. Not a player's equipment any more, so no
            // hair, beard, skin or male and female models (VisEquipment does those for m_isPlayer only)
            VisEquipment equipment = boss.GetComponent<VisEquipment>();
            equipment.m_isPlayer = false;
            if (equipment.m_bodyModel)
            {
                equipment.m_bodyModel.enabled = false;
            }
            // Animated even so: the player's animator stops moving the bones while its renderers are out of sight
            // (CullUpdateTransforms), and the one it goes by is the body, never seen now (the armor comes later), so
            // they glided about frozen (the user, 2026-11-14: "they don't use their legs to move though, they are just
            // gliding around")
            foreach (Animator animator in boss.GetComponentsInChildren<Animator>(true))
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            MonsterAI ai = boss.AddComponent<MonsterAI>();
            // Noticing players as far as they cast (at first they noticed no one, to be looked at, with no attack)
            ai.m_viewRange = ViewRange;
            ai.m_hearRange = HearRange;
            ai.m_enableHuntPlayer = false;
            ai.m_attackPlayerObjects = false;
            ai.m_alertedEffects = new EffectList();
            ai.m_idleSound = new EffectList();
            ai.m_consumeItems = new List<ItemDrop>();
            // A Fuling berserker's room to walk: about their size (0.8 m around, 3.5 m tall; they're 1 m and 3.7 m)
            ai.m_pathAgentType = Pathfinding.AgentType.GoblinBruteSize;
            ai.m_randomMoveRange = WanderRange;
            ai.m_randomMoveInterval = WanderInterval;
            ai.m_moveMinAngle = 90f;
            ai.m_jumpInterval = 0f;
            ai.m_avoidWater = true;
            ai.m_avoidLava = true;
            ai.m_avoidFire = false;
            // The game's default makes a creature that hasn't attacked for 30 s, and was hurt in the last 20, run from its
            // target: a hunter kited out of its reach while being burnt turned and fled (the user, 2026-11-15: "I saw the
            // firecaller just roam around aimlessly when it should have been chasing me")
            ai.m_fleeIfHurtWhenTargetCantBeReached = false;
            ai.m_afraidOfFire = false;

            boss.AddComponent<CallerBoss>().m_element = (int)element;
            boss.AddComponent<TwinsPhase>();
            PrefabManager.Instance.AddPrefab(boss);
        }

        // The fields Unity keeps in a prefab (public or [SerializeField], and not [NonSerialized]) that `type` declares,
        // from one component to the other
        private static void CopySettings(Type type, Component from, Component to)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsNotSerialized || (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField))))
                {
                    continue;
                }
                field.SetValue(to, field.GetValue(from));
            }
        }
    }
}
