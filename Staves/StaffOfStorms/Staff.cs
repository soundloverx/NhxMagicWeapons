using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using NhxMagicWeapons.Staves.StaffOfStorms.Effects;
using UnityEngine;

namespace NhxMagicWeapons.Staves.StaffOfStorms
{
    // Staff of Storms: Magic Supremacy's Thundersurge (its Stormcaller staff) with attacks of its own. The attack casts a
    // lightning bolt that hits at once; the secondary attack (middle mouse) calls lightning down on the spot aimed at.
    // With the whole Stormcaller's Raiment on, the secondary attack is the Eye of the Storm (StormEye) and a dodge with the
    // staff in hand is Lightning Step (LightningStep).
    // This builds and registers the staff, its prefabs and its recipe; all its numbers are here.
    internal static class Staff
    {
        internal const string ItemName = "NhxStaffStorms";
        internal const string NameKey = "$item_nhx_staffstorms";
        private const string WeaknessName = "SE_NhxLightningWeakness";
        private const string BoltName = "NhxStormBolt";
        private const string CallName = "NhxStormCall";
        private const string StrikeName = "NhxStormStrike";
        private const string CrackleName = "NhxStormCrackle";
        private const string ImpactName = "NhxStormImpact";
        private const string BuzzName = "NhxStormBuzz";
        private const string LookName = "NhxStormLook";
        private const string EyeName = "NhxStormEye";
        private const string EyeTickName = "NhxStormEyeTick";
        private const string EyeStrikeName = "NhxStormEyeStrike";
        private const string RainBoltName = "NhxStormRainBolt";

        // From Magic Supremacy. Of their effects we keep the sounds and lights: their particles were too much on top of the
        // bolt (a shockwave sphere and sparks flying every way at the staff's tip, and the same at twice the size where it
        // hits), and Thundersurge's sparks looked like extra bolts that didn't come from the staff's head
        private const string ModelPrefab = "StaffStormcallerShocker_DO";                 // Thundersurge
        private const string SparksPrefab = "fx_stormcaller_staff_shock_DO";             // Thundersurge's sparks and buzz as it fires; its sparks' material draws the bolt
        private const string CracklePrefab = "fx_stormcaller_staff_sphere_trap_land_DO"; // an electric crackle (Tempest Eye's trap landing)
        private const string ImpactPrefab = "fx_stormcaller_staff_sphere_hit_DO";        // Tempest Eye's orb hitting: a bang and a flash
        private const string HeldPrefab = "StaffFirecallerBlaster_DO";                   // the Incinerator: its held attack, for the storm
        private const string CloudEffect = "fx_frostcaller_staff_shard_hit_DO";          // its smoke's material (a soft grey puff), for the storm's cloud
        private const string CloudChild = "smoke";

        // From the game
        private const string BaseStaff = "StaffFireball"; // Staff of Embers
        private const string StrikeBase = "lightningAOE"; // the Obliterator's lightning
        private const string Idol = "Upgrader5Weapon";    // black marble battle idol, for refining at the Forge of Potential

        // Between the prongs of Thundersurge's head, in its "attach" model: the model runs from about -1.4 to 1.1 m along z
        // (its mesh's bounds), and Magic Supremacy lights its head at 0.79
        private static readonly Vector3 TipPosition = new Vector3(0.005f, -0.009f, 0.8f);

        // The bolt's damage, a side-grade to RtDMythics' staves (our friends') until late in the Deep North. Theirs reach
        // level 4, mostly blunt, with an orb hitting 1.5 m around: Mistwalker 163-181 (Mistlands, 28 eitr), Freyr T3
        // 195-213 (Ashlands, 38 eitr), Arcane T3 279-303 (Deep North, 38 eitr). With NhxIdolProgression's idols the staff
        // reaches level 5 in the Mistlands, 9 in the Ashlands. Lightning: 135, 5 more each level, 11 from level 10. Spirit
        // from level 5 and fire from level 10, as the model changes: 10, and 1 more each level after. In all: 165 at 5, 189
        // at 9, 276 at 15, 341 at 20. That's 10% under what matched theirs (184 at 5, 212 at 9, 305 at 15), as the bolt
        // never misses: it hits at once, where the crosshair is. The Charred (Ashlands) are weak to spirit, which most
        // other creatures ignore; most of the Deep North's creatures are weak to fire.
        // Since v1.0.1 all of it is 15% higher (DamageBoost), at every level: 155 at 1, 190 at 5, 217 at 9, 243 at 10, 317
        // at 15, 392 at 20. The strike, the chain's jumps and the tooltip follow, as they come from the staff's damage
        private const float DamageBoost = 1.15f;
        private const float Lightning = 135f * DamageBoost;
        private const float LightningPerLevel = 5f * DamageBoost;
        private const int NorthLevel = 10;
        private const float NorthLightningPerLevel = 11f * DamageBoost;
        private const int SpiritLevel = 5;
        private const float Spirit = 10f * DamageBoost;
        private const float SpiritPerLevel = 1f * DamageBoost;
        private const int FireLevel = 10;
        private const float Fire = 10f * DamageBoost;
        private const float FirePerLevel = 1f * DamageBoost;
        private const float BoltEitr = 30f;
        private const float BoltRange = 50f;
        // The lightning's look by the staff's level, up to level 20 (higher levels look the same). It's drawn as thin bolts
        // side by side. They're thicker at each level, from 35% of a full bolt's width up by 7.5% a level, and thin again
        // when one more comes. One glow around them and the light where they hit grow steadily: 37% at level 1, 4.5% more
        // a level, 100% at 15. Only the first bolt forks, once per 10 levels on average (0.1 forks at level 1, 2 at 20)
        private const int MaxLookLevel = 20;
        private const float StrandSize = 0.35f;
        private const float StrandSizePerLevel = 0.075f;
        private const float GlowSize = 0.37f;
        private const float GlowSizePerLevel = 0.045f;
        private const float BranchesPerLevel = 0.1f;
        // The bolt has one more at levels 5, 10, 15 and 20: 1 at levels 1-4, 5 at 20. The chain's jumps are one bolt, at
        // 70% of its size
        private const int LevelsPerStrand = 5;
        private const int MaxStrands = 5;
        // The staff's model by its level: Thundersurge's look at level 15. Below level 5 it has no crystals, butt spikes or
        // prongs. Prongs: one at 5, the opposite one at 10, the other two at 20. The crystals and spikes come at 5, the
        // crystals barely glowing and the spikes at a quarter of their length, both reaching their full look at 15. The
        // inlays along the shaft glow at 30% at level 1, fully at 15. Their pulse stands still below level 5, runs at half
        // speed at 5 and as made at 15. At 20 the crystals, inlays and head light glow 50% more, and the pulse runs 15%
        // faster. The head's idle sparks (6 a second as made) come at 10, half as many, as many at 15, twice at 20
        private static readonly int[] ProngLevels = { 5, 10, 20, 20 };
        private const int ModelFirstLevel = 5;
        private const int ModelFullLevel = 15;
        private const float SpikeStartSize = 0.25f;
        private const float CrystalStartGlow = 0.05f;
        private const float InlayStartGlow = 0.3f;
        private const float MaxGlow = 1.5f;
        private const float PulseStartSpeed = 0.5f;
        private const float MaxPulseSpeed = 1.15f;
        private const int IdleSparksLevel = 10;
        private const float IdleSparksStart = 0.5f;
        private const float MaxIdleSparks = 2f;
        // When the bolt hits an enemy, the chance that it chains to others nearby: 5% per level of the staff, 100% at 20.
        // It jumps once more every 2 levels: once at levels 1-2, twice at 3-4, up to 10 times from level 19
        private const float ChainChancePerLevel = 0.05f;
        private const int ChainLevelsPerJump = 2;
        private const int ChainMaxJumps = 10;
        // It jumps to the nearest enemy within this range that it hasn't hit, or if there's none, back to one that has
        // taken fewer than ChainMaxHitsPerEnemy of its jumps; else it stops (a lone enemy only takes the bolt)
        private const float ChainRange = 8f;
        private const int ChainMaxHitsPerEnemy = 2;
        // The first jump deals half the bolt's damage, and each next one 85% of the one before
        private const float ChainDamage = 0.5f;
        private const float ChainFalloff = 0.85f;
        // The secondary attack, calling lightning from the sky, comes at level 5: below, middle mouse does nothing
        private const int StrikeLevel = 5;
        // Its lightning falls from 80 m, 3 times as wide as the bolt's (it's seen from farther away), and more jagged. It
        // starts as one bolt at level 5, with one more every 3 levels: 2 at 8, 3 at 11, 4 at 14, 5 from 17 (as many lines
        // as the Obliterator's lightning it replaces)
        private const int StrikeLevelsPerStrand = 3;
        private const int StrikeMaxStrands = 5;
        private const float StrikeScale = 3f;
        private const float StrikeHeight = 80f;
        // The strike deals the bolt's damage times this, to everything within its radius: about twice the bolt's damage
        // before it went 10% down
        private const float StrikeDamageMultiplier = 2.2f;
        private const float StrikeEitr = 50f;
        private const float StrikeRange = 60f;
        private const float StrikeRadius = 3.5f;
        private const float AttackForce = 40f;
        // The Staff of Embers' durability (its base and per level) times this
        private const float DurabilityBoost = 2f;
        // With the whole Stormcaller's Raiment on (the user's picks, 2026-11-10; the numbers picked here):
        // Eye of the Storm, the secondary attack held (the Incinerator's looping attack): a cloud 16 m over the spot aimed
        // at, drifting after the crosshair at up to 12 m/s, within the strike's 60 m; every second, for 30 eitr (as the
        // Wrath of Muspelheim's channelled beam), a rain of bolts from it across its 7 m (below), each striking everything
        // within the strike's 3.5 m for the bolt's damage (the strike alone: 2.2 times it, for 50 eitr), and chaining from
        // the first enemy struck by the bolt's rules. Each second's first bolt shakes the camera half as hard as the
        // strike, 20 m around
        private const float StormEitr = 30f;
        private const float StormInterval = 1f;
        private const float StormDamage = 1f;
        private const float StormRadius = 7f;
        private const float StormSpeed = 12f;
        private const float StormHeight = 16f;
        private const float StormShake = 0.5f;
        private const float StormShakeRange = 20f;
        // Its rain (the user, 2026-11-11: "rain lightning across a larger area under the cloud"; the numbers picked here):
        // 3 bolts a second at level 5, one more every 3 levels (as the strike's bolts side by side), 7 from level 17, one
        // bolt each, spread over the second and across the cloud; each thing struck once a second at most
        private const int StormFirstBolts = 3;
        private const int StormLevelsPerBolt = 3;
        private const int StormMaxBolts = 7;
        // Lightning Step, a dodge with the staff in hand: 12 m (the user's), for 20 eitr instead of the roll's stamina (as
        // Magic Supremacy's Stormcaller air dash), the enemies within 1.5 m of the way taking half the bolt's hit
        private const float StepDistance = 12f;
        private const float StepEitr = 20f;
        private const float StepDamage = 0.5f;
        private const float StepWidth = 1.5f;

        // How the staff's lightning is drawn, for the Stormcaller's Raiment's crackle (ArmorCrackle)
        internal static LightningLook Look { get; private set; }

        internal static void Add()
        {
            GameObject embers = PrefabManager.Cache.GetPrefab<GameObject>(BaseStaff);
            GameObject model = Plugin.Assets.LoadAsset<GameObject>(ModelPrefab);
            if (!embers || !model)
            {
                Plugin.Log.LogError($"No Staff of Storms: {(embers ? ModelPrefab + " isn't in the asset bundle" : "the game has no " + BaseStaff)}.");
                return;
            }

            Transform container = StaffEffects.Container;
            LightningLook look = CreateLook(container);
            Look = look;

            GameObject buzz = StaffEffects.AddWithoutParticles(BuzzName, SparksPrefab);
            GameObject crackle = StaffEffects.AddWithoutParticles(CrackleName, CracklePrefab);
            GameObject impact = StaffEffects.AddWithoutParticles(ImpactName, ImpactPrefab);
            GameObject strike = AddStrike(look, StrikeName, StrikeHeight, 1f, 30f);
            GameObject eyeStrike = AddStrike(look, EyeStrikeName, StormHeight, StormShake, StormShakeRange);
            GameObject rainBolt = AddRainBolt(look);
            // The storm's first bolt each second is one bolt too: the rain spreads them out
            StrikeLightning eyeLightning = eyeStrike ? eyeStrike.GetComponent<StrikeLightning>() : null;
            if (eyeLightning)
            {
                eyeLightning.m_oneStrand = true;
            }

            GameObject bolt = AddBolt(container, look, buzz, crackle, impact);
            GameObject call = CreateCall(container, crackle, strike);
            GameObject eyeTick = CreateEyeTick(container, AddEye(container, eyeStrike, rainBolt, bolt));

            GameObject staff = PrefabManager.Instance.CreateClonedPrefab(ItemName, model);
            GameObject tip = new GameObject(StaffHits.TipName);
            tip.transform.SetParent(staff.transform.Find("attach"), false);
            tip.transform.localPosition = TipPosition;
            SetUpModel(staff);

            ItemDrop.ItemData.SharedData shared = staff.GetComponent<ItemDrop>().m_itemData.m_shared;
            ItemDrop.ItemData.SharedData baseShared = embers.GetComponent<ItemDrop>().m_itemData.m_shared;
            shared.m_name = NameKey;
            shared.m_description = "$item_nhx_staffstorms_description   ";
            shared.m_damages = new HitData.DamageTypes { m_lightning = Lightning };
            shared.m_damagesPerLevel = new HitData.DamageTypes { m_lightning = LightningPerLevel };
            float northLightning = NorthLightningPerLevel - LightningPerLevel;
            LevelDamage.Set(shared,
                new LevelDamage.Step(NorthLevel, new HitData.DamageTypes { m_lightning = northLightning }, new HitData.DamageTypes { m_lightning = northLightning }),
                new LevelDamage.Step(SpiritLevel, new HitData.DamageTypes { m_spirit = Spirit }, new HitData.DamageTypes { m_spirit = SpiritPerLevel }),
                new LevelDamage.Step(FireLevel, new HitData.DamageTypes { m_fire = Fire }, new HitData.DamageTypes { m_fire = FirePerLevel }));
            shared.m_attackForce = AttackForce;
            // Its hits leave what they hit a step weaker to lightning for a while (ElementWeakness)
            Sprite icon = shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null;
            shared.m_attackStatusEffect = ElementWeakness.Add(WeaknessName, "$se_nhx_lightningweakness", "$se_nhx_lightningweakness_tooltip", HitData.DamageType.Lightning, false, icon);
            shared.m_attackStatusEffectChance = 1f;
            // The bolt's chain lightning, for the staff's level
            shared.m_projectileToolTip = true;
            // The rest as the Staff of Embers
            shared.m_maxQuality = baseShared.m_maxQuality;
            shared.m_weight = baseShared.m_weight;
            // Twice its durability, at every level (since v1.0.1)
            shared.m_maxDurability = baseShared.m_maxDurability * DurabilityBoost;
            shared.m_durabilityPerLevel = baseShared.m_durabilityPerLevel * DurabilityBoost;
            shared.m_useDurabilityDrain = baseShared.m_useDurabilityDrain;
            shared.m_blockPower = baseShared.m_blockPower;
            shared.m_blockPowerPerLevel = baseShared.m_blockPowerPerLevel;
            shared.m_deflectionForce = baseShared.m_deflectionForce;
            shared.m_timedBlockBonus = baseShared.m_timedBlockBonus;
            shared.m_movementModifier = baseShared.m_movementModifier;
            shared.m_toolTier = baseShared.m_toolTier;
            // Thundersurge's are Magic Supremacy's copies of these, which aren't registered here
            shared.m_blockEffect = baseShared.m_blockEffect;
            shared.m_trailStartEffect = baseShared.m_trailStartEffect;
            shared.m_startEffect = new EffectList();

            // The Staff of Embers' cast, aimed straight at the crosshair: no spread, and no upward angle (for the fireball's arc)
            Attack boltAttack = baseShared.m_attack.Clone();
            boltAttack.m_attackProjectile = bolt;
            boltAttack.m_attackEitr = BoltEitr;
            boltAttack.m_projectileAccuracy = 0f;
            boltAttack.m_projectileAccuracyMin = 0f;
            boltAttack.m_launchAngle = 0f;
            shared.m_attack = boltAttack;

            // The same with the Staff of Protection's animation, staff raised (one animation, where the fireball alternates two)
            Attack strikeAttack = baseShared.m_attack.Clone();
            strikeAttack.m_attackAnimation = "staff_shield";
            strikeAttack.m_attackChainLevels = 0;
            strikeAttack.m_attackProjectile = call;
            strikeAttack.m_attackEitr = StrikeEitr;
            strikeAttack.m_damageMultiplier = StrikeDamageMultiplier;
            strikeAttack.m_projectileAccuracy = 0f;
            strikeAttack.m_projectileAccuracyMin = 0f;
            strikeAttack.m_launchAngle = 0f;
            shared.m_secondaryAttack = strikeAttack;
            SecondaryUnlock.Add(shared, StrikeLevel);
            // The strike costs its own eitr, not the bolt's (and the storm's shots theirs)
            AttackEitr.Add(shared);
            // A quick click on the storm still calls it once
            FirstShot.Add(shared);

            // With the whole Stormcaller's Raiment on: the storm, and Lightning Step
            GameObject held = Plugin.Assets.LoadAsset<GameObject>(HeldPrefab);
            Attack heldAttack = held ? held.GetComponent<ItemDrop>().m_itemData.m_shared.m_secondaryAttack : null;
            if (heldAttack != null && eyeTick)
            {
                StormEye.SetUp(shared, Armor.StormcallerArmor.StormSetBonus,
                    WrathOfMuspelheim.Staff.HeldAttack(heldAttack, eyeTick, StormEitr, StormInterval, StormDamage));
            }
            else
            {
                Plugin.Log.LogWarning($"{HeldPrefab} isn't in the asset bundle: no Eye of the Storm.");
            }
            LightningStep.s_staff = shared.m_name;
            LightningStep.s_status = Armor.StormcallerArmor.StormSetBonus.GetStableHashCode();
            LightningStep.s_bolt = bolt;
            LightningStep.s_leaveEffects = new[] { crackle };
            LightningStep.s_landEffects = new[] { crackle, impact };
            LightningStep.s_distance = StepDistance;
            LightningStep.s_eitr = StepEitr;
            LightningStep.s_damage = StepDamage;
            LightningStep.s_width = StepWidth;

            CustomItem item = new CustomItem(staff, false, new ItemConfig
            {
                CraftingStation = CraftingStations.GaldrTable,
                RepairStation = CraftingStations.GaldrTable,
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig("YggdrasilWood", 20, 10),
                    new RequirementConfig("Eitr", 16, 8),
                    new RequirementConfig("Thunderstone", 2, 1),
                    new RequirementConfig(Idol, 1, 0, false),
                },
            });
            // As in the game's recipes for the Mistlands staves: crafting doesn't take the idol (at a crafting station,
            // upgrader requirements don't count), but it's what lists the staff at the Forge of Potential, which takes one
            // for each refinement. Jotunn has no setting for it
            foreach (Piece.Requirement requirement in item.Recipe.Recipe.m_resources)
            {
                if (requirement.m_resItem && requirement.m_resItem.name.EndsWith(Idol))
                {
                    requirement.m_upgraderResource = true;
                }
            }
            ItemManager.Instance.AddItem(item);
        }

        // The model in parts that come with the staff's level
        private static void SetUpModel(GameObject staff)
        {
            StaffLevelLook look = StaffModel.Build(staff);
            if (!look)
            {
                return;
            }
            look.m_prongLevels = ProngLevels;
            look.m_firstLevel = ModelFirstLevel;
            look.m_fullLevel = ModelFullLevel;
            look.m_maxLevel = MaxLookLevel;
            look.m_spikeStartSize = SpikeStartSize;
            look.m_crystalStartGlow = CrystalStartGlow;
            look.m_inlayStartGlow = InlayStartGlow;
            look.m_maxGlow = MaxGlow;
            look.m_pulseStartSpeed = PulseStartSpeed;
            look.m_maxPulseSpeed = MaxPulseSpeed;
            look.m_sparksLevel = IdleSparksLevel;
            look.m_sparksStart = IdleSparksStart;
            look.m_maxSparks = MaxIdleSparks;
        }

        // How the bolt and the strike are drawn, its bolts with the material of Thundersurge's sparks
        private static LightningLook CreateLook(Transform container)
        {
            LightningLook look = LightningLook.Create(container, LookName, SparksPrefab);
            look.m_maxLevel = MaxLookLevel;
            look.m_strandSize = StrandSize;
            look.m_strandSizePerLevel = StrandSizePerLevel;
            look.m_glowSize = GlowSize;
            look.m_glowSizePerLevel = GlowSizePerLevel;
            look.m_branchesPerLevel = BranchesPerLevel;
            return look;
        }

        // The Obliterator's lightning without its damage and its own bolts: its two areas hit everything, the caster too, for
        // a fixed 400 at the center and 10 around, where the staff deals its own; its bolts ("Lighting", one going up from
        // 10 m, and "Lighting_rod", four thin ones coming down from there) are replaced with ours (StrikeLightning), which
        // grow with the staff's level. It keeps its ground wave, sparks, lights, thunder and camera shake, though shorter
        // and softer: it lasted 10 s (its lights fade after 4), and shook the camera hard 40 m around. Its lightning falls
        // from `height`, and it shakes the camera with `shake` within `shakeRange`: the strike's, and the storm's (from its
        // cloud, softer, as it strikes every second)
        private static GameObject AddStrike(LightningLook look, string name, float height, float shake, float shakeRange)
        {
            GameObject strike = PrefabManager.Instance.CreateClonedPrefab(name, StrikeBase);
            if (!strike)
            {
                Plugin.Log.LogWarning($"The game has no {StrikeBase}: the Staff of Storms' strikes are invisible.");
                return null;
            }
            foreach (Aoe aoe in strike.GetComponentsInChildren<Aoe>(true))
            {
                if (aoe.m_useCollider)
                {
                    Object.DestroyImmediate(aoe.m_useCollider);
                }
                Object.DestroyImmediate(aoe);
            }
            foreach (string bolts in new[] { "Lighting", "Lighting_rod" })
            {
                Transform child = strike.transform.Find(bolts);
                if (child)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
            StrikeLightning lightning = strike.AddComponent<StrikeLightning>();
            lightning.m_look = look;
            lightning.m_height = height;
            lightning.m_firstLevel = StrikeLevel;
            lightning.m_levelsPerStrand = StrikeLevelsPerStrand;
            lightning.m_maxStrands = StrikeMaxStrands;
            lightning.m_scale = StrikeScale;
            TimedDestruction timed = strike.GetComponent<TimedDestruction>();
            if (timed)
            {
                timed.m_timeout = 5f;
            }
            CamShaker shaker = strike.GetComponent<CamShaker>();
            if (shaker)
            {
                shaker.m_strength = shake;
                shaker.m_range = shakeRange;
            }
            PrefabManager.Instance.AddPrefab(strike);
            return strike;
        }

        // The storm's bolts but the first of each second: its lightning, one bolt as the strike's, with a crackle (the bolt's
        // cast sound) and nothing else: no lights (the cloud flashes for each), ground wave or camera shake
        private static GameObject AddRainBolt(LightningLook look)
        {
            GameObject rain = StaffEffects.AddWithoutParticles(RainBoltName, CracklePrefab);
            if (!rain)
            {
                return null;
            }
            StrikeLightning lightning = rain.AddComponent<StrikeLightning>();
            lightning.m_look = look;
            lightning.m_height = StormHeight;
            lightning.m_firstLevel = StrikeLevel;
            lightning.m_levelsPerStrand = StrikeLevelsPerStrand;
            lightning.m_maxStrands = StrikeMaxStrands;
            lightning.m_scale = StrikeScale;
            lightning.m_oneStrand = true;
            // Long enough for its lightning to show
            TimedDestruction timed = rain.GetComponent<TimedDestruction>();
            if (timed && timed.m_timeout < 1.5f)
            {
                timed.m_timeout = 1.5f;
            }
            return rain;
        }

        // The Eye of the Storm (StormEye): networked, so that every game draws its cloud
        private static GameObject AddEye(Transform container, GameObject strike, GameObject rainBolt, GameObject bolt)
        {
            GameObject go = new GameObject(EyeName);
            go.transform.SetParent(container, false);
            go.AddComponent<ZNetView>();
            StormEye eye = go.AddComponent<StormEye>();
            eye.m_range = StrikeRange;
            eye.m_radius = StormRadius;
            eye.m_strike = strike;
            eye.m_rainBolt = rainBolt;
            eye.m_strikeRadius = StrikeRadius;
            eye.m_firstLevel = StrikeLevel;
            eye.m_firstBolts = StormFirstBolts;
            eye.m_levelsPerBolt = StormLevelsPerBolt;
            eye.m_maxBolts = StormMaxBolts;
            eye.m_interval = StormInterval;
            eye.m_bolt = bolt;
            eye.m_speed = StormSpeed;
            eye.m_height = StormHeight;
            eye.m_tickTimeout = StormInterval * 1.6f;
            eye.m_cloudMaterial = LineMaterials.Copy(CloudEffect, CloudChild);
            PrefabManager.Instance.AddPrefab(go);
            return go;
        }

        // The storm's attack's shots: only on the caster's game, for the moment it takes; not registered
        private static GameObject CreateEyeTick(Transform container, GameObject eye)
        {
            GameObject go = new GameObject(EyeTickName);
            go.transform.SetParent(container, false);
            go.AddComponent<StormEyeTick>().m_eye = eye;
            return go;
        }

        // Networked, so that every game draws it
        private static GameObject AddBolt(Transform container, LightningLook look, GameObject buzz, GameObject crackle, GameObject impact)
        {
            GameObject bolt = new GameObject(BoltName);
            bolt.transform.SetParent(container, false);
            bolt.AddComponent<ZNetView>();
            LightningBolt lightning = bolt.AddComponent<LightningBolt>();
            lightning.m_look = look;
            lightning.m_secondaryLevel = StrikeLevel;
            lightning.m_range = BoltRange;
            lightning.m_levelsPerStrand = LevelsPerStrand;
            lightning.m_maxStrands = MaxStrands;
            lightning.m_chainChancePerLevel = ChainChancePerLevel;
            lightning.m_chainLevelsPerJump = ChainLevelsPerJump;
            lightning.m_chainMaxJumps = ChainMaxJumps;
            lightning.m_chainRange = ChainRange;
            lightning.m_chainMaxHitsPerEnemy = ChainMaxHitsPerEnemy;
            lightning.m_chainDamage = ChainDamage;
            lightning.m_chainFalloff = ChainFalloff;
            lightning.m_castEffects = new[] { buzz, crackle };
            lightning.m_hitEffects = new[] { impact };
            lightning.m_jumpEffects = new[] { crackle };
            PrefabManager.Instance.AddPrefab(bolt);
            return bolt;
        }

        // Only on the caster's game, for the moment it takes to cast: not registered
        private static GameObject CreateCall(Transform container, GameObject crackle, GameObject strike)
        {
            GameObject call = new GameObject(CallName);
            call.transform.SetParent(container, false);
            LightningStrike lightning = call.AddComponent<LightningStrike>();
            lightning.m_range = StrikeRange;
            lightning.m_radius = StrikeRadius;
            lightning.m_strike = strike;
            lightning.m_castEffects = new[] { crackle };
            return call;
        }
    }
}
