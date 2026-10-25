using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;
using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim
{
    // Wrath of Muspelheim: Magic Supremacy's Incinerator (its Firecaller blaster) with attacks of its own. The attack casts
    // a fire beam that hits at once and explodes; the secondary attack (middle mouse, held) is a flamethrower. Built like
    // the Staff of Storms, its numbers mirror that staff's.
    // This builds and registers the staff, its prefabs and its recipe; all its numbers are here.
    internal static class Staff
    {
        private const string ItemName = "NhxStaffMuspelheim";
        private const string BeamName = "NhxMuspelBeam";
        private const string JetName = "NhxMuspelJet";
        private const string FlamesName = "NhxMuspelFlames";
        private const string ShotName = "NhxMuspelShot";
        private const string BlastName = "NhxMuspelBlast";
        private const string LookName = "NhxMuspelLook";

        // From Magic Supremacy
        private const string ModelPrefab = "StaffFirecallerBlaster_DO";                 // the Incinerator
        private const string ShotPrefab = "sfx_firecaller_staff_blaster_shot_DO";        // its shot's sound (Dundr's)
        private const string BlastPrefab = "fx_firecallercaller_staff_blaster_hit_DO";   // its shot's explosion: fire, embers, smoke, a bang
        private const string FlamesPrefab = "fx_firecaller_staff_thrower_DO";            // its flamethrower's flames and roar
        // The material the beams are drawn with: Thundersurge's sparks' (Legacy Shaders/Particles/Alpha Blended)
        private const string BeamMaterialEffect = "fx_stormcaller_staff_shock_DO";
        private const string BeamMaterialChild = "Flash";

        // From the game
        private const string BaseStaff = "StaffFireball"; // Staff of Embers
        private const string Idol = "Upgrader5Weapon";    // black marble battle idol, for refining at the Forge of Potential

        // In the Incinerator's "attach" model the staff runs along z, its head up: its flame crystal from 0.55 to 1.1 m
        // (around 0.011, -0.012), its light at 0.87. The beam leaves from the crystal's upper part; embers rise from all
        // of it
        private static readonly Vector3 TipPosition = new Vector3(0.011f, -0.012f, 0.95f);
        private static readonly Vector3 EmbersCenter = new Vector3(0.011f, -0.012f, 0.82f);
        private static readonly Vector3 EmbersSize = new Vector3(0.07f, 0.07f, 0.45f);

        // The beam's damage: the Staff of Storms' in all. Fire burns over time (the game's burning: its hits add up, and
        // water makes it go out five times as fast), so as the Incinerator's (80 pierce, 120 fire) two fifths of it is
        // pierce, which hits at once: 54 pierce and 81 fire at level 1, 2 and 3 more each level (5, as the Staff of Storms'
        // lightning), 4 and 7 from level 10 (11). Where the Staff of Storms gets 10 fire at level 10 (1 more each level
        // after), this gets as much more fire; and the same spirit from level 5: 10, 1 more each level after. In all: 165
        // at 5 (62 pierce, 93 fire, 10 spirit), 211 at 10, 276 at 15, 341 at 20 (114 pierce, 202 fire, 25 spirit)
        private const float Pierce = 54f;
        private const float PiercePerLevel = 2f;
        private const float Fire = 81f;
        private const float FirePerLevel = 3f;
        private const int NorthLevel = 10;
        private const float NorthPiercePerLevel = 4f;
        private const float NorthFirePerLevel = 7f;
        private const float NorthFire = 10f;
        private const float NorthFireBonusPerLevel = 1f;
        private const int SpiritLevel = 5;
        private const float Spirit = 10f;
        private const float SpiritPerLevel = 1f;
        private const float BeamEitr = 30f;
        private const float BeamRange = 50f;
        // Its attack: the flamethrower's animation, staff held forward (the user, 2026-10-25: "It feels more appropriate to
        // handle the staff like a shotgun instead of swinging it"; it was the Staff of Embers' cast): a beam as the click
        // comes through, and one a second while held
        private const float BeamInterval = 1f;
        // How long each beam shows: wild as it's shot, calming and straightening as it fades (FireBeams). It was 0.35 s
        // before it became a waveform
        private const float BeamDuration = 0.6f;
        private const float AttackForce = 40f;
        // The beam's look by the staff's level, up to level 20 (higher levels look the same): waveforms side by side (see
        // FireBeams), one more at levels 5 and 10 (1 at levels 1-4, 2 at 5-9, 3 from 10; the user asked for 2 fewer at 20
        // than the Staff of Storms' 5), each thicker at each level from 50% of a full beam's width (35% until the user
        // found it too dim) up by 7.5% a level, and thinner again when one more comes; at most 80% (as at levels 9 and
        // 14, so from 14 on). The sheath and glow around them and the light where they hit: 37% at level 1, 4.5% more a
        // level, 100% at 15, 123% at 20
        private const int MaxLookLevel = 20;
        private const float StrandSize = 0.5f;
        private const float StrandSizePerLevel = 0.075f;
        private const float MaxStrandSize = 0.8f;
        private const float GlowSize = 0.37f;
        private const float GlowSizePerLevel = 0.045f;
        private const int LevelsPerStrand = 5;
        private const int MaxStrands = 3;
        // Its blast, as chain lightning for the Staff of Storms: 1 m around where it hits at level 1, 0.15 m more each
        // level (2.35 m at 10, 3.85 m at 20), where everything else takes half the beam's damage. Its effect, the
        // Incinerator's explosion, is about 3.5 m around as made, and made as big as the blast
        private const float BlastRadius = 1f;
        private const float BlastRadiusPerLevel = 0.15f;
        private const float BlastDamage = 0.5f;
        private const float BlastEffectRadius = 3.5f;
        // The secondary attack, a flamethrower held down, comes at level 5: below, middle mouse does nothing. As the
        // Incinerator's, a puff every 0.2 s while held, each taking its eitr (20 a second). Each burns everything in a cone
        // 0.5 m wide at the head, 12 degrees wider on each side farther out, for 15% of the beam's damage (75% a second,
        // fire adding up), reaching 7 m at level 5 and a meter more every 3 levels: 12 m at 20. The Incinerator's flames
        // fly about 20 m as made, and are made as long as the reach
        private const int FlameLevel = 5;
        private const float FlameEitr = 4f;
        private const float FlameInterval = 0.2f;
        private const float FlameDamage = 0.15f;
        private const float FlameReach = 7f;
        private const float FlameReachPerLevel = 1f / 3f;
        private const float FlameWidth = 0.5f;
        private const float FlameAngle = 12f;
        private const float FlamesMadeReach = 20f;
        // The staff's model by its level: the Incinerator's look at level 15. Its black swirls come in pairs: around the
        // head at 5, the lower shaft at 10, the butt at 15; they glow half as much as made at 5 and turn half as fast,
        // fully at 15. The head's flame crystal glows 35% at level 1, the butt's crystal 5%, the collar's shards 30%, all
        // fully at 15; the shards' pulse stands still below 5, as the swirls. At 20 everything glows 50% more, and the
        // swirls and pulse go 15% faster. Embers rise from the head from level 10: 3 a second, 6 at 15, 12 at 20
        private static readonly int[] SwirlLevels = { 5, 5, 10, 10, 15, 15 };
        private const int ModelFullLevel = 15;
        private const float HeadStartGlow = 0.35f;
        private const float ButtStartGlow = 0.05f;
        private const float ShardsStartGlow = 0.3f;
        private const float SwirlStartGlow = 0.5f;
        private const float MaxGlow = 1.5f;
        private const float SpinStart = 0.5f;
        private const float MaxSpin = 1.15f;
        private const int EmbersLevel = 10;
        private const float EmbersRate = 6f;
        private const float EmbersStart = 0.5f;
        private const float MaxEmbers = 2f;

        internal static void Add()
        {
            GameObject embers = PrefabManager.Cache.GetPrefab<GameObject>(BaseStaff);
            GameObject model = Plugin.Assets.LoadAsset<GameObject>(ModelPrefab);
            if (!embers || !model)
            {
                Plugin.Log.LogError($"No Wrath of Muspelheim: {(embers ? ModelPrefab + " isn't in the asset bundle" : "the game has no " + BaseStaff)}.");
                return;
            }

            Transform container = StaffEffects.Container;
            BeamLook look = CreateLook(container);
            GameObject shot = StaffEffects.AddWithoutParticles(ShotName, ShotPrefab);
            GameObject flames = AddFlames();
            FlameJet jet = CreateJet(container, flames);
            GameObject beam = AddBeam(container, look, shot, CreateBlast(), jet);

            GameObject staff = PrefabManager.Instance.CreateClonedPrefab(ItemName, model);
            GameObject tip = new GameObject(StaffHits.TipName);
            tip.transform.SetParent(staff.transform.Find("attach"), false);
            tip.transform.localPosition = TipPosition;
            SetUpModel(staff);

            ItemDrop.ItemData.SharedData shared = staff.GetComponent<ItemDrop>().m_itemData.m_shared;
            ItemDrop.ItemData.SharedData baseShared = embers.GetComponent<ItemDrop>().m_itemData.m_shared;
            shared.m_name = "$item_nhx_staffmuspelheim";
            shared.m_description = "$item_nhx_staffmuspelheim_description";
            shared.m_damages = new HitData.DamageTypes { m_pierce = Pierce, m_fire = Fire };
            shared.m_damagesPerLevel = new HitData.DamageTypes { m_pierce = PiercePerLevel, m_fire = FirePerLevel };
            float northPierce = NorthPiercePerLevel - PiercePerLevel;
            float northFire = NorthFirePerLevel - FirePerLevel;
            LevelDamage.Set(shared,
                new LevelDamage.Step(NorthLevel, new HitData.DamageTypes { m_pierce = northPierce, m_fire = northFire }, new HitData.DamageTypes { m_pierce = northPierce, m_fire = northFire }),
                new LevelDamage.Step(SpiritLevel, new HitData.DamageTypes { m_spirit = Spirit }, new HitData.DamageTypes { m_spirit = SpiritPerLevel }),
                new LevelDamage.Step(NorthLevel, new HitData.DamageTypes { m_fire = NorthFire }, new HitData.DamageTypes { m_fire = NorthFireBonusPerLevel }));
            shared.m_attackForce = AttackForce;
            // The beam's blast radius and the flamethrower's reach, for the staff's level
            shared.m_projectileToolTip = true;
            // The rest as the Staff of Embers
            shared.m_maxQuality = baseShared.m_maxQuality;
            shared.m_weight = baseShared.m_weight;
            shared.m_maxDurability = baseShared.m_maxDurability;
            shared.m_durabilityPerLevel = baseShared.m_durabilityPerLevel;
            shared.m_useDurabilityDrain = baseShared.m_useDurabilityDrain;
            shared.m_blockPower = baseShared.m_blockPower;
            shared.m_blockPowerPerLevel = baseShared.m_blockPowerPerLevel;
            shared.m_deflectionForce = baseShared.m_deflectionForce;
            shared.m_timedBlockBonus = baseShared.m_timedBlockBonus;
            shared.m_movementModifier = baseShared.m_movementModifier;
            shared.m_toolTier = baseShared.m_toolTier;
            shared.m_skillType = baseShared.m_skillType;
            // The Incinerator's are Magic Supremacy's copies of these, which aren't registered here
            shared.m_blockEffect = baseShared.m_blockEffect;
            shared.m_trailStartEffect = baseShared.m_trailStartEffect;
            shared.m_startEffect = new EffectList();

            // Both attacks are the Incinerator's flamethrower (the Staff of Frost's animation, the staff held forward,
            // looping while the button is down): a beam a second, or a puff of flames every 0.2 s
            Attack held = shared.m_secondaryAttack;
            shared.m_attack = HeldAttack(held, beam, BeamEitr, BeamInterval, 1f);
            shared.m_secondaryAttack = HeldAttack(held, jet.gameObject, FlameEitr, FlameInterval, FlameDamage);
            SecondaryUnlock.Add(shared, FlameLevel);
            // Each shot costs its own eitr: a puff not the beam's
            AttackEitr.Add(shared);
            // A quick click still shoots once
            FirstShot.Add(shared);

            CustomItem item = new CustomItem(staff, false, new ItemConfig
            {
                CraftingStation = CraftingStations.GaldrTable,
                RepairStation = CraftingStations.GaldrTable,
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig("YggdrasilWood", 20, 10),
                    new RequirementConfig("Eitr", 16, 8),
                    new RequirementConfig("SurtlingCore", 4, 2),
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

        // The Incinerator's flamethrower attack shooting `projectile` every `interval` seconds while held, each shot taking
        // `eitr` and dealing the staff's damage times `damage`, aimed straight at the crosshair. Its effects are Magic
        // Supremacy's, which aren't registered here: the shots make their own
        private static Attack HeldAttack(Attack held, GameObject projectile, float eitr, float interval, float damage)
        {
            Attack attack = held.Clone();
            attack.m_attackProjectile = projectile;
            attack.m_attackEitr = eitr;
            attack.m_perBurstResourceUsage = true;
            attack.m_burstInterval = interval;
            attack.m_damageMultiplier = damage;
            attack.m_projectileAccuracy = 0f;
            attack.m_projectileAccuracyMin = 0f;
            attack.m_launchAngle = 0f;
            attack.m_burstEffect = new EffectList();
            attack.m_startEffect = new EffectList();
            attack.m_triggerEffect = new EffectList();
            attack.m_trailStartEffect = new EffectList();
            attack.m_hitEffect = new EffectList();
            attack.m_hitTerrainEffect = new EffectList();
            return attack;
        }

        // The swirls in parts that come with the staff's level, and the embers
        private static void SetUpModel(GameObject staff)
        {
            StaffLevelLook look = StaffModel.Build(staff, EmbersCenter, EmbersSize);
            if (!look)
            {
                return;
            }
            look.m_swirlLevels = SwirlLevels;
            look.m_fullLevel = ModelFullLevel;
            look.m_maxLevel = MaxLookLevel;
            look.m_headStartGlow = HeadStartGlow;
            look.m_buttStartGlow = ButtStartGlow;
            look.m_shardsStartGlow = ShardsStartGlow;
            look.m_swirlStartGlow = SwirlStartGlow;
            look.m_maxGlow = MaxGlow;
            look.m_spinStart = SpinStart;
            look.m_maxSpin = MaxSpin;
            look.m_embersLevel = EmbersLevel;
            look.m_embersRate = EmbersRate;
            look.m_embersStart = EmbersStart;
            look.m_maxEmbers = MaxEmbers;
        }

        private static BeamLook CreateLook(Transform container)
        {
            BeamLook look = BeamLook.Create(container, LookName, BeamMaterialEffect, BeamMaterialChild);
            look.m_maxLevel = MaxLookLevel;
            look.m_strandSize = StrandSize;
            look.m_strandSizePerLevel = StrandSizePerLevel;
            look.m_maxStrandSize = MaxStrandSize;
            look.m_glowSize = GlowSize;
            look.m_glowSizePerLevel = GlowSizePerLevel;
            return look;
        }

        // The Incinerator's explosion, made by each game where the beam hits (FireBeam), at the blast's size: no ZNetView,
        // not registered (its TimedDestruction then simply destroys it)
        private static GameObject CreateBlast()
        {
            GameObject source = Plugin.Assets.LoadAsset<GameObject>(BlastPrefab);
            if (!source)
            {
                Plugin.Log.LogWarning($"{BlastPrefab} isn't in the asset bundle: the Wrath of Muspelheim's blasts are invisible.");
                return null;
            }
            GameObject blast = PrefabManager.Instance.CreateClonedPrefab(BlastName, source);
            Object.DestroyImmediate(blast.GetComponent<ZNetView>());
            TimedDestruction timed = blast.GetComponent<TimedDestruction>();
            if (timed)
            {
                timed.m_triggerOnAwake = true;
            }
            AudioRouting.Add(blast);
            return blast;
        }

        // The Incinerator's flames, made as long as the flamethrower's reach on every game (FlameLook)
        private static GameObject AddFlames()
        {
            GameObject source = Plugin.Assets.LoadAsset<GameObject>(FlamesPrefab);
            if (!source)
            {
                Plugin.Log.LogWarning($"{FlamesPrefab} isn't in the asset bundle: the Wrath of Muspelheim's flamethrower is invisible.");
                return null;
            }
            GameObject flames = PrefabManager.Instance.CreateClonedPrefab(FlamesName, source);
            flames.AddComponent<FlameLook>().m_madeReach = FlamesMadeReach;
            PrefabManager.Instance.AddPrefab(flames);
            AudioRouting.Add(flames);
            return flames;
        }

        // Only on the caster's game, for the moment it takes to burn: not registered
        private static FlameJet CreateJet(Transform container, GameObject flames)
        {
            GameObject go = new GameObject(JetName);
            go.transform.SetParent(container, false);
            FlameJet jet = go.AddComponent<FlameJet>();
            jet.m_firstLevel = FlameLevel;
            jet.m_maxLevel = MaxLookLevel;
            jet.m_reach = FlameReach;
            jet.m_reachPerLevel = FlameReachPerLevel;
            jet.m_width = FlameWidth;
            jet.m_angle = FlameAngle;
            jet.m_flames = flames;
            return jet;
        }

        // Networked, so that every game draws it
        private static GameObject AddBeam(Transform container, BeamLook look, GameObject shot, GameObject blast, FlameJet jet)
        {
            GameObject go = new GameObject(BeamName);
            go.transform.SetParent(container, false);
            go.AddComponent<ZNetView>();
            FireBeam beam = go.AddComponent<FireBeam>();
            beam.m_look = look;
            beam.m_range = BeamRange;
            beam.m_duration = BeamDuration;
            beam.m_levelsPerStrand = LevelsPerStrand;
            beam.m_maxStrands = MaxStrands;
            beam.m_blastRadius = BlastRadius;
            beam.m_blastRadiusPerLevel = BlastRadiusPerLevel;
            beam.m_maxBlastLevel = MaxLookLevel;
            beam.m_blastDamage = BlastDamage;
            beam.m_blast = blast;
            beam.m_blastEffectRadius = BlastEffectRadius;
            beam.m_castEffects = new[] { shot };
            beam.m_flames = jet;
            PrefabManager.Instance.AddPrefab(go);
            return go;
        }
    }
}
