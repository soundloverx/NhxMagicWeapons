using Jotunn.Entities;
using Jotunn.Managers;
using NhxMagicWeapons.Staves;
using NhxMagicWeapons.Staves.StaffOfStorms;
using UnityEngine;
using Storms = NhxMagicWeapons.Staves.StaffOfStorms.Staff;
using Wrath = NhxMagicWeapons.Staves.WrathOfMuspelheim.Staff;

namespace NhxMagicWeapons.Bosses
{
    // The bosses' first attacks (the user, 2026-11-14: "build the first two attacks"; "it would be really cool if they could
    // attack without [weapons]"): the Stormcaller calls lightning on a rune that warns 2 s ahead (BossCall, ElementRune), the
    // Firecaller breathes the Wrath of Muspelheim's flamethrower from its hands after a wind-up (BossFlames).
    // Each is an item the boss holds, as every monster's attacks are (Yagluth's beam and meteors too): a copy of a Fuling
    // shaman's fireball attack with no model, so nothing shows in the hands, with our attack in it. The AI picks it when
    // its target is within range and the interval has passed, turns to it, and plays the attack on the player's
    // animations; the game makes the hit (the item's damage, with the world's modifiers and CLLC's damage for the players
    // near; no stars, which the twins never have: CallerBosses), and our projectile spends it.
    // Their numbers are first guesses, to tune by watching them (the user: "If I turn on god mode, they can attack me and I
    // can observe"). Against armor: a hit under twice the armor is cut to hit² / (4 × armor), so 200 lightning does 100
    // to a player with 100 armor, and 100 fire 25
    internal static class BossAttacks
    {
        internal const string StormCallName = "NhxStormcallerCall";
        internal const string FireFlamesName = "NhxFirecallerFlames";
        internal const string FireSigilName = "NhxFirecallerSigil";
        private const string StormRuneName = "NhxStormRune";
        private const string FireRuneName = "NhxFireRune";
        private const string CastName = "NhxStormcallerCast";
        private const string SigilCastName = "NhxFirecallerCast";
        private const string PuffName = "NhxFirecallerPuff";

        // The telegraphed spells, the only ones a watcher casts (TwinsAI)
        internal static bool IsWatcherSpell(ItemDrop.ItemData item)
        {
            string name = item?.m_shared.m_name;
            return name == StormCallName || name == FireSigilName;
        }

        // Spells a hunter leaves alone: the Firecaller's sigil, so that hunting it closes in with its flames (TwinsAI)
        internal static bool IsWatcherOnly(ItemDrop.ItemData item)
        {
            return item?.m_shared.m_name == FireSigilName;
        }

        // From the game: the item they're made from (a monster's attack, with no model), and the Staff of Embers, whose
        // cast the staff's strike is made from; from Magic Supremacy, the Incinerator, whose held flamethrower attack the
        // Wrath of Muspelheim's is
        private const string BaseItem = "GoblinShaman_attack_fireball";
        private const string BaseStaff = "StaffFireball";
        private const string HeldPrefab = "StaffFirecallerBlaster_DO";

        // Both: how far from its target the boss may be (meters), how far off its facing (degrees), and the push
        private const float MaxAngle = 30f;
        private const float AttackForce = 40f;

        // The Stormcaller's call (its watcher's spell, and a hunter's from afar): within 35 m, every 6 s at most, with the
        // Staff of Protection's raised-staff animation (the staff's strike's), the rune 2 s ahead of the strike, which hits
        // 3.5 m around (the staff's), for 200 lightning
        private const float CallRange = 35f;
        private const float CallInterval = 6f;
        private const string CallAnimation = "staff_shield";
        private const float CallDelay = 2f;
        private const float CallLightning = 200f;

        // The Stormcaller's bolt from its hands (BossBolt), a hunter's (it brings the Stormcaller in after a player): within
        // 20 m, every 5 s at most, on the held flamethrower animation (both hands out): 5 puffs of charge (1 s), then the
        // bolt, reaching 30 m from its hands, for 120 lightning, half the usual stagger (lightning staggers: a full 120 would
        // nearly always), chaining as the staff at level 10 (half the time, up to 5 jumps), looking like the staff's at 15.
        // It walks on as it charges (the user, 2026-11-20: "I would have the Stormcaller move while charging his hunter
        // lightning too", as the hunters close in: TwinsAI.Close), at BoltMove of its speed (3 m/s running: a jogging player
        // gains on it a little), turning freely: where the bolt goes was set as the charge began (BossBolt)
        private const float BoltRange = 20f;
        private const float BoltMove = 0.6f;
        private const float BoltTurn = 1f;
        private const float BoltInterval = 5f;
        private const int BoltCharge = 5;
        private const float BoltReach = 30f;
        private const float BoltLightning = 120f;
        private const float BoltStagger = 0.5f;
        private const int BoltChainLevel = 10;
        private const int BoltLookLevel = 15;
        private const string BoltName = "NhxStormcallerBolt";
        private const string BoltCastName = "NhxStormcallerBoltCast";

        // The Firecaller's sigil, the call's twin (its watcher's spell, and a hunter's from afar): within 35 m, every 6 s at
        // most, with the Staff of Embers' own cast (the staff's bolt's: a thrust of the hands), its rune 2 s ahead of an
        // eruption 3.5 m around, for 180 fire (fire burns on: SE_Burning)
        private const float SigilRange = 35f;
        private const float SigilInterval = 6f;
        private const float SigilDelay = 2f;
        private const float SigilRadius = 3.5f;
        private const float SigilFire = 180f;

        // The Firecaller's flames: within 9 m, every 7 s at most, a puff every 0.2 s (the staff's): 5 of wind-up (1 s), 13
        // of flames (2.6 s) drawn 12 m long, burning 10 m out from the 3rd on, once they've flown there, and every 3rd
        // after (4 burns, 0.6 s apart) for 100 fire each; meanwhile it doesn't move and turns at a quarter of its speed
        // (150 degrees a second: 37.5). A light push: the flames shove, they don't blast (the strike's is the staff's)
        private const float FlamesRange = 9f;
        private const float FlamesInterval = 7f;
        private const float PuffInterval = 0.2f;
        private const int WindupPuffs = 5;
        private const int FlamePuffs = 13;
        private const int FirstBurn = 3;
        private const int BurnEvery = 3;
        private const float FlamesDrawn = 12f;
        private const float FlamesReach = 10f;
        private const float FlamesFire = 100f;
        private const float FlamesTurn = 0.25f;
        private const float FlamesForce = 15f;

        // The runes, networked: the spells' (their delay and size as made here) and the twins' smites' (TwinsDuel)
        internal static void AddRunes()
        {
            if (!TwinsDuel.s_stormRune)
            {
                TwinsDuel.s_stormRune = AddRune(StormRuneName, false, CallDelay, Storms.StrikeArea);
                TwinsDuel.s_fireRune = AddRune(FireRuneName, true, SigilDelay, SigilRadius);
            }
        }

        // A storm rune strikes with the staff's strike, or `strike` (the storm's lighter rain: TwinsStorm)
        internal static GameObject AddRune(string name, bool fire, float delay, float radius, GameObject strike = null)
        {
            GameObject prefab = new GameObject(name);
            prefab.transform.SetParent(StaffEffects.Container, false);
            prefab.AddComponent<ZNetView>();
            ElementRune rune = prefab.AddComponent<ElementRune>();
            rune.m_fire = fire;
            rune.m_delay = delay;
            rune.m_radius = radius;
            rune.m_strike = fire ? null : strike ? strike : Storms.Strike;
            PrefabManager.Instance.AddPrefab(prefab);
            return prefab;
        }

        // A spell's projectile, local: its rune under the target, `effects` in its hands
        private static GameObject AddCast(string name, GameObject rune, GameObject effects)
        {
            GameObject cast = new GameObject(name);
            cast.transform.SetParent(StaffEffects.Container, false);
            BossCall call = cast.AddComponent<BossCall>();
            call.m_rune = rune;
            call.m_castEffects = effects ? new[] { effects } : new GameObject[0];
            return cast;
        }

        internal static GameObject AddStormCall()
        {
            GameObject embers = PrefabManager.Cache.GetPrefab<GameObject>(BaseStaff);
            Attack cast = embers ? embers.GetComponent<ItemDrop>().m_itemData.m_shared.m_attack : null;
            if (cast == null || !Storms.Strike)
            {
                Plugin.Log.LogWarning("The Stormcaller has no call of lightning: " + (cast == null ? $"the game has no {BaseStaff}" : "no Staff of Storms strike") + ".");
                return null;
            }
            AddRunes();
            GameObject castPrefab = AddCast(CastName, TwinsDuel.s_stormRune, Storms.Crackle);

            Attack attack = cast.Clone();
            attack.m_attackAnimation = CallAnimation;
            attack.m_attackChainLevels = 0;
            attack.m_attackRandomAnimations = 0;
            attack.m_attackProjectile = castPrefab;
            attack.m_projectiles = 1;
            attack.m_projectileBursts = 1;
            attack.m_projectileAccuracy = 0f;
            attack.m_projectileAccuracyMin = 0f;
            attack.m_launchAngle = 0f;
            Quiet(attack);
            return AddItem(StormCallName, attack, new HitData.DamageTypes { m_lightning = CallLightning }, CallRange, CallInterval);
        }

        internal static GameObject AddStormBolt()
        {
            GameObject held = Plugin.Assets.LoadAsset<GameObject>(HeldPrefab);
            Attack heldAttack = held ? held.GetComponent<ItemDrop>().m_itemData.m_shared.m_secondaryAttack : null;
            if (heldAttack == null || !LightningStep.s_bolt)
            {
                Plugin.Log.LogWarning("The Stormcaller has no bolt: " + (heldAttack == null ? $"{HeldPrefab} isn't in the asset bundle" : "no Staff of Storms bolt") + ".");
                return null;
            }

            GameObject castPrefab = new GameObject(BoltCastName);
            castPrefab.transform.SetParent(StaffEffects.Container, false);
            BossBolt bolt = castPrefab.AddComponent<BossBolt>();
            bolt.m_charge = BoltCharge;
            bolt.m_range = BoltReach;
            bolt.m_chainLevel = BoltChainLevel;
            bolt.m_lookLevel = BoltLookLevel;
            bolt.m_bolt = LightningStep.s_bolt;
            bolt.m_chargeEffects = Storms.Crackle ? new[] { Storms.Crackle } : new GameObject[0];
            bolt.m_hitEffects = LightningStep.s_landEffects;

            Attack attack = Wrath.HeldAttack(heldAttack, castPrefab, 0f, PuffInterval, 1f, 0f);
            attack.m_projectileBursts = BoltCharge + 1;
            attack.m_perBurstResourceUsage = false;
            attack.m_speedFactor = BoltMove;
            attack.m_speedFactorRotation = BoltTurn;
            Quiet(attack);
            attack.m_staggerMultiplier = BoltStagger;
            return AddItem(BoltName, attack, new HitData.DamageTypes { m_lightning = BoltLightning }, BoltRange, BoltInterval);
        }

        internal static GameObject AddFireSigil()
        {
            GameObject embers = PrefabManager.Cache.GetPrefab<GameObject>(BaseStaff);
            Attack cast = embers ? embers.GetComponent<ItemDrop>().m_itemData.m_shared.m_attack : null;
            if (cast == null)
            {
                Plugin.Log.LogWarning($"The Firecaller has no sigil: the game has no {BaseStaff}.");
                return null;
            }
            AddRunes();
            GameObject castPrefab = AddCast(SigilCastName, TwinsDuel.s_fireRune, Wrath.Shot);

            // The Staff of Embers' cast as it is (its two thrusts, in turn), straight at the target
            Attack attack = cast.Clone();
            attack.m_attackProjectile = castPrefab;
            attack.m_projectiles = 1;
            attack.m_projectileBursts = 1;
            attack.m_projectileAccuracy = 0f;
            attack.m_projectileAccuracyMin = 0f;
            attack.m_launchAngle = 0f;
            Quiet(attack);
            return AddItem(FireSigilName, attack, new HitData.DamageTypes { m_fire = SigilFire }, SigilRange, SigilInterval);
        }

        internal static GameObject AddFireFlames()
        {
            GameObject held = Plugin.Assets.LoadAsset<GameObject>(HeldPrefab);
            Attack heldAttack = held ? held.GetComponent<ItemDrop>().m_itemData.m_shared.m_secondaryAttack : null;
            if (heldAttack == null || !Wrath.Flames)
            {
                Plugin.Log.LogWarning("The Firecaller has no flames: " + (heldAttack == null ? $"{HeldPrefab} isn't in the asset bundle" : "no Wrath of Muspelheim flames") + ".");
                return null;
            }

            GameObject puffPrefab = new GameObject(PuffName);
            puffPrefab.transform.SetParent(StaffEffects.Container, false);
            BossFlames flames = puffPrefab.AddComponent<BossFlames>();
            flames.m_flames = Wrath.Flames;
            flames.m_windup = WindupPuffs;
            flames.m_firstBurn = FirstBurn;
            flames.m_burnEvery = BurnEvery;
            flames.m_flameReach = FlamesDrawn;
            flames.m_reach = FlamesReach;

            Attack attack = Wrath.HeldAttack(heldAttack, puffPrefab, 0f, PuffInterval, 1f, 0f);
            attack.m_projectileBursts = WindupPuffs + FlamePuffs;
            attack.m_perBurstResourceUsage = false;
            // Along its facing, up or down toward its target
            attack.m_useCharacterFacing = true;
            attack.m_useCharacterFacingYAim = true;
            attack.m_speedFactor = 0f;
            attack.m_speedFactorRotation = FlamesTurn;
            Quiet(attack);
            GameObject item = AddItem(FireFlamesName, attack, new HitData.DamageTypes { m_fire = FlamesFire }, FlamesRange, FlamesInterval);
            if (item)
            {
                item.GetComponent<ItemDrop>().m_itemData.m_shared.m_attackForce = FlamesForce;
            }
            return item;
        }

        // A monster's: no cost, and none of the cloned attack's own effects (the Staff of Embers' fireball sounds)
        private static void Quiet(Attack attack)
        {
            attack.m_attackEitr = 0f;
            attack.m_attackStamina = 0f;
            attack.m_attackHealth = 0f;
            attack.m_damageMultiplier = 1f;
            attack.m_raiseSkillAmount = 0f;
            attack.m_startEffect = new EffectList();
            attack.m_triggerEffect = new EffectList();
            attack.m_burstEffect = new EffectList();
            attack.m_trailStartEffect = new EffectList();
            attack.m_hitEffect = new EffectList();
            attack.m_hitTerrainEffect = new EffectList();
        }

        // The item the boss holds for an attack: the base monster attack's, without a model, our attack and the AI's rules
        private static GameObject AddItem(string name, Attack attack, HitData.DamageTypes damages, float range, float interval)
        {
            GameObject item = PrefabManager.Instance.CreateClonedPrefab(name, BaseItem);
            if (!item)
            {
                Plugin.Log.LogWarning($"The game has no {BaseItem}: no {name}.");
                return null;
            }
            foreach (string model in new[] { "attach", "attach_skin", "attach_back" })
            {
                Transform child = item.transform.Find(model);
                if (child)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
            ItemDrop.ItemData.SharedData shared = item.GetComponent<ItemDrop>().m_itemData.m_shared;
            shared.m_name = name;
            shared.m_damages = damages;
            shared.m_damagesPerLevel = new HitData.DamageTypes();
            shared.m_attackForce = AttackForce;
            shared.m_blockable = true;
            shared.m_dodgeable = true;
            shared.m_attackStatusEffect = null;
            shared.m_useDurability = false;
            shared.m_attack = attack;
            shared.m_secondaryAttack = new Attack();
            shared.m_aiAttackRange = range;
            shared.m_aiAttackRangeMin = 0f;
            shared.m_aiAttackInterval = interval;
            shared.m_aiAttackMaxAngle = MaxAngle;
            shared.m_aiTargetType = ItemDrop.ItemData.AiTarget.Enemy;
            shared.m_aiPrioritized = false;
            shared.m_aiPrioritizedIfAngleCheckValid = false;
            shared.m_aiInDungeonOnly = false;
            shared.m_aiInMistOnly = false;
            shared.m_aiMaxHealthPercentage = 1f;
            shared.m_aiMinHealthPercentage = 0f;
            shared.m_aiWhenFlying = false;
            shared.m_aiWhenWalking = true;
            shared.m_aiWhenSwiming = true;
            // In the game's item list, so a boss holding it is shown holding nothing (VisEquipment looks items up there)
            ItemManager.Instance.AddItem(new CustomItem(item, false));
            return item;
        }
    }
}
