using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace NhxMagicWeapons
{
    // Magic Supremacy 3.1.7's Lightcaller and Bloodcaller sets and its Blood Requiem staff, to try out before they become
    // Deep North sets (the user, 2026-11-28; the Bloodcaller set already is one in Magic Supremacy). Then more staves to get
    // a feel of before keeping 2 or 3 (the user, 2026-11-28): Sanguine Concord, the Candle of Vital Light and the
    // Springbloom Parasol. Tempest Eye and Luminaris were tried too, and taken out (the user, 2026-11-28: "we won't be
    // keeping those").
    // They're as its prefabs make them, the user's pick (2026-11-28, "Prefabs only"): their stats, attacks, projectiles,
    // set bonuses and auras, without what Magic Supremacy's own code adds to them. Except two things, for the staves whose
    // main attack wouldn't work without them (the user's pick, 2026-11-28; PrefabItemFixes): Sanguine Concord's pellets
    // heal allies, and the Candle's sigil heals once and goes, 3 at most. Otherwise, as of 3.1.7:
    // - the sets' auras have no owner, which Magic Supremacy gives them (PrefabOwnerBinder). The Lightcaller's heals
    //   everyone within 8 m, enemies too, and the Bloodcaller's marks everyone within 30 m, its wearer too
    // - the Bloodcaller's mark does nothing. Magic Supremacy heals the wearer when a marked enemy dies (DeathTriggerSpawn)
    // - Blood Requiem's pellets heal 1 health each (the game's healthReturn), without Magic Supremacy's 1.5 more
    //   (ProjectileLifeSteal). Its secondary attack's zone asks for a status effect SE_BloodDebuff_DO that isn't in the
    //   bundle (it has SE_BloodcallerDebuff_DO), so it only damages, as in Magic Supremacy
    // - the pieces' max health, max eitr and skill levels, which come from Magic Supremacy's code (ExtraStats,
    //   ItemDropExtendedStats), aren't there, nor Sanguine Concord's buff's 50 max health (ExtraStats)
    // - each attack costs the primary attack's eitr, as the game charges it (Magic Supremacy's AttackEitrFix charges each
    //   its own, as Staves\AttackEitr does ours): before the skill discount, Blood Requiem's secondary attack 40 eitr on top
    //   of its 100 health. Sanguine Concord's main attack costs no eitr, so its secondary attack costs none either (50 in
    //   Magic Supremacy); the Candle's secondary 25 (60). The Parasol's costs its own 80 (ParasolChannel)
    // - the Parasol's and Sanguine Concord's buffs stack (Magic Supremacy keeps 2 at most, and one shield: SeCount)
    // The Parasol is becoming more than its prefab: with the Lightcaller set, a channel instead of its shield, and nothing
    // without the set (ParasolChannel; the user's design, 2026-11-28)
    // What this does: the items under our names, crafted at the Galdr Table from Magic Supremacy's recipes (the user's
    // pick, 2026-11-28; Recipes); the prefabs their attacks and auras create, registered so that every game has them; the
    // status effects asked for by name; the sounds through the game's mixer (AudioRouting). All from their own bundle,
    // Plugin.Assets317 (Tools\slim_bundle.py).
    internal static class PrefabItems
    {
        // Magic Supremacy's prefab, and the item made from it here: its name, and its localization key's last part
        // ($item_nhx_...; the names are Magic Supremacy's English ones). For a set's pieces, its set bonus's key's last
        // part ($se_nhx_...). For an item with a look of its own on the back, the embedded model part file it comes
        // from (AddBackLook)
        private struct Item
        {
            internal string Prefab, Name, Key, Set, BackLook;
            internal RequirementConfig[] Requirements;
        }

        // The status effects asked for by name (Aoe.m_statusEffect, and FriendlyHeal's), which the game finds by name in
        // ObjectDB, and their localization keys' last part ($se_nhx_..., and ..._tooltip; Magic Supremacy's English texts)
        private static readonly string[][] NamedEffects =
        {
            new[] { "SE_LightcallerAura_DO", "lightcaller_aura" },             // the Lightcaller set's aura
            new[] { "SE_BloodcallerDeathTrigger_DO", "bloodcaller_mark" },     // the Bloodcaller set's aura
            new[] { "SE_BloodcallerHeal_DO", "bloodcaller_heal" },             // Sanguine Concord's pellets
            new[] { "SE_BloodcallerBuff_DO", "bloodcaller_buff" },             // and its secondary attack
            new[] { "SE_LightcallerSigil_DO", "lightcaller_heal" },            // the Candle's sigil, named as its heal
            new[] { "SE_LightcallerHeal_DO", "lightcaller_heal" },             // and its secondary attack
            new[] { "SE_WindcallerShield_DO", "windcaller_shield" },           // the Parasol's attack
            new[] { "SE_WindcallerPetal_DO", "windcaller_petal" },             // and its secondary attack
        };

        // The two fixes (PrefabItemFixes): Sanguine Concord's pellets, and the effect they give the first ally they meet
        private const string HealPellet = "projectile_bloodcaller_staff_heal_DO";
        private const string HealPelletEffect = "SE_BloodcallerHeal_DO";
        // The Candle's sigil, its flash as it heals and goes (Magic Supremacy's), and how many each player can have
        private const string Sigil = "aoe_lightcaller_staff_sigil_DO";
        private const string SigilFlash = "fx_lightcaller_staff_sigil_hit_DO";
        private const int MaxSigils = 3;

        // Crafted at the Galdr Table with no extension, upgraded there up to Magic Supremacy's level 4 (one more station
        // level for each: 2, 3 and 4). Magic Supremacy crafts them at its own table (the Magic Weaver, levels 2 to 4)
        private const int StationLevel = 1;

        // Magic Supremacy's recipes (3.1.7), without its own materials (light, storm and blood spirits, bronze fittings):
        // each requirement's amount for crafting, then for each upgrade
        private static readonly Item[] Items =
        {
            // The Lightcaller set (Mistlands in Magic Supremacy), and its set bonus SE_LightcallerArmor_DO
            new Item
            {
                Prefab = "ArmorLightcallerHelmet_DO", Name = "NhxLightcallerHood", Key = "lightcaller_hood", Set = "lightcaller",
                Requirements = new[]
                {
                    new RequirementConfig("Eitr", 6, 3),
                    new RequirementConfig("LinenThread", 16, 0),
                    new RequirementConfig("Bronze", 8, 0),
                },
            },
            new Item
            {
                Prefab = "ArmorLightcallerChest_DO", Name = "NhxLightcallerVestments", Key = "lightcaller_vestments", Set = "lightcaller",
                Requirements = new[]
                {
                    new RequirementConfig("Eitr", 6, 3),
                    new RequirementConfig("LinenThread", 20, 0),
                },
            },
            new Item
            {
                Prefab = "ArmorLightcallerLegs_DO", Name = "NhxLightcallerLegwraps", Key = "lightcaller_legwraps", Set = "lightcaller",
                Requirements = new[]
                {
                    new RequirementConfig("Eitr", 6, 3),
                    new RequirementConfig("LinenThread", 20, 0),
                },
            },
            new Item
            {
                Prefab = "ArmorLightcallerCape_DO", Name = "NhxLightcallerCape", Key = "lightcaller_cape", Set = "lightcaller",
                Requirements = new[]
                {
                    new RequirementConfig("Eitr", 6, 3),
                    new RequirementConfig("LinenThread", 24, 0),
                    new RequirementConfig("BlackCore", 2, 0),
                },
            },
            // The Bloodcaller set (Deep North in Magic Supremacy), and its set bonus SE_BloodcallerArmor_DO
            new Item
            {
                Prefab = "ArmorBloodcallerHelmet_DO", Name = "NhxBloodcallerAdornment", Key = "bloodcaller_adornment", Set = "bloodcaller",
                Requirements = new[]
                {
                    new RequirementConfig("NornThread", 3, 2),
                    new RequirementConfig("GiantBloodSack", 10, 0),
                },
            },
            new Item
            {
                Prefab = "ArmorBloodcallerChest_DO", Name = "NhxBloodcallerVestments", Key = "bloodcaller_vestments", Set = "bloodcaller",
                Requirements = new[]
                {
                    new RequirementConfig("NornThread", 5, 3),
                    new RequirementConfig("MooseHide", 6, 0),
                    new RequirementConfig("Gold", 8, 0),
                },
            },
            new Item
            {
                Prefab = "ArmorBloodcallerLegs_DO", Name = "NhxBloodcallerLegwraps", Key = "bloodcaller_legwraps", Set = "bloodcaller",
                Requirements = new[]
                {
                    new RequirementConfig("NornThread", 5, 3),
                    new RequirementConfig("MooseHide", 6, 0),
                    new RequirementConfig("Gold", 4, 0),
                },
            },
            new Item
            {
                Prefab = "ArmorBloodcallerCape_DO", Name = "NhxBloodcallerVeil", Key = "bloodcaller_veil", Set = "bloodcaller",
                Requirements = new[]
                {
                    new RequirementConfig("NornThread", 6, 3),
                    new RequirementConfig("MooseHide", 6, 0),
                    new RequirementConfig("Gold", 8, 0),
                },
            },
            // Blood Requiem: a spread of 12 blood pellets (Blood Magic), and its secondary attack's zone for 100 health
            new Item
            {
                Prefab = "StaffBloodcallerShotgun_DO", Name = "NhxBloodRequiem", Key = "bloodrequiem",
                Requirements = new[]
                {
                    new RequirementConfig("OrbThunderBlood", 1, 1),
                    new RequirementConfig("NornThread", 5, 2),
                    new RequirementConfig("Gold", 10, 0),
                },
            },
            // Sanguine Concord: a stream of healing pellets for 2 health each (Blood Magic), and its secondary attack's buff
            // on you and allies within 7 m. It has one level in Magic Supremacy, which lists upgrade costs all the same
            new Item
            {
                Prefab = "StaffBloodcallerHeal_DO", Name = "NhxSanguineConcord", Key = "sanguineconcord",
                Requirements = new[]
                {
                    new RequirementConfig("OrbThunderBlood", 1, 1),
                    new RequirementConfig("GiantBloodSack", 6, 2),
                    new RequirementConfig("Gold", 10, 0),
                },
            },
            // The Candle of Vital Light: a healing sigil where it lands (Elemental Magic), and its secondary attack's heal
            // on you and allies within 7 m
            new Item
            {
                Prefab = "StaffLightcallerHeal_DO", Name = "NhxCandleOfVitalLight", Key = "candleofvitallight",
                Requirements = new[]
                {
                    new RequirementConfig("TrophySeeker", 1, 0),
                    new RequirementConfig("Eitr", 16, 8),
                    new RequirementConfig("Bronze", 24, 0),
                },
            },
            // The Springbloom Parasol: a shield on you and allies within 7 m (Elemental Magic), and its secondary attack's
            // buff (carry weight, speed, jump, slower falls). Closed on the back (the user, 2026-11-28: "an umbrella that
            // is always open. Do you see a way of closing it when sheathing the weapon?"; Tools\fold_parasol.py)
            new Item
            {
                Prefab = "StaffWindcallerBuff_DO", Name = "NhxSpringbloomParasol", Key = "springbloomparasol",
                BackLook = "parasol_closed",
                Requirements = new[]
                {
                    new RequirementConfig("TrophyGjall", 1, 0),
                    new RequirementConfig("Eitr", 16, 8),
                    new RequirementConfig("YggdrasilWood", 20, 0),
                },
            },
        };

        private static readonly HashSet<StatusEffect> s_setBonuses = new HashSet<StatusEffect>();

        internal static void Add()
        {
            AddFixes();
            AddPrefabs();
            AddNamedEffects();
            foreach (Item item in Items)
            {
                AddItem(item);
            }
        }

        // What the items' attacks, projectiles and auras create, all listed in the bundle (Tools\slim_bundle.py): those
        // with a ZNetView are networked, so the game needs them registered for the other games to create their copies.
        // Magic Supremacy registers the same by name. The items themselves are registered as ours (AddItem)
        private static void AddPrefabs()
        {
            foreach (GameObject prefab in Plugin.Assets317.LoadAllAssets<GameObject>())
            {
                if (prefab.GetComponent<ItemDrop>())
                {
                    continue;
                }
                if (prefab.GetComponent<ZNetView>())
                {
                    PrefabManager.Instance.AddPrefab(prefab);
                }
                AudioRouting.Add(prefab);
            }
        }

        // On the prefabs, before anything copies them
        private static void AddFixes()
        {
            GameObject pellet = Plugin.Assets317.LoadAsset<GameObject>(HealPellet);
            if (pellet)
            {
                pellet.AddComponent<FriendlyHeal>().m_statusEffect = HealPelletEffect;
            }
            else
            {
                Plugin.Log.LogWarning($"{HealPellet} isn't in the asset bundle: Sanguine Concord heals no one.");
            }
            GameObject sigil = Plugin.Assets317.LoadAsset<GameObject>(Sigil);
            if (sigil)
            {
                OneHitAoe once = sigil.AddComponent<OneHitAoe>();
                once.m_hitEffect = Plugin.Assets317.LoadAsset<GameObject>(SigilFlash);
                once.m_maxPerPlayer = MaxSigils;
            }
            else
            {
                Plugin.Log.LogWarning($"{Sigil} isn't in the asset bundle: the Candle of Vital Light has no sigil.");
            }
        }

        private static void AddNamedEffects()
        {
            foreach (string[] named in NamedEffects)
            {
                StatusEffect effect = Plugin.Assets317.LoadAsset<StatusEffect>(named[0]);
                if (!effect)
                {
                    Plugin.Log.LogWarning($"{named[0]} isn't in the asset bundle: the aura that asks for it does nothing.");
                    continue;
                }
                AddStatusEffect(effect, named[1]);
            }
        }

        // With our localization keys, which carry Magic Supremacy's English texts
        private static void AddStatusEffect(StatusEffect effect, string key)
        {
            effect.m_name = "$se_nhx_" + key;
            effect.m_tooltip = "$se_nhx_" + key + "_tooltip";
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(effect, false));
        }

        private static void AddItem(Item item)
        {
            GameObject model = Plugin.Assets317.LoadAsset<GameObject>(item.Prefab);
            if (!model)
            {
                Plugin.Log.LogError($"No {item.Name}: {item.Prefab} isn't in the asset bundle.");
                return;
            }
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(item.Name, model);
            ItemDrop.ItemData.SharedData shared = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            shared.m_name = "$item_nhx_" + item.Key;
            shared.m_description = "$item_nhx_" + item.Key + "_description";
            // The set bonus is the same object for the set's four pieces: registered once, with the first
            StatusEffect bonus = shared.m_setStatusEffect;
            if (item.Set != null && bonus && s_setBonuses.Add(bonus))
            {
                AddStatusEffect(bonus, item.Set);
            }
            if (item.BackLook != null)
            {
                AddBackLook(prefab, item.BackLook);
            }
            // The parasol's channel with the Lightcaller set, instead of Magic Supremacy's shield
            if (item.Name == ParasolChannel.ItemPrefab)
            {
                ParasolChannel.Add(shared);
            }
            AudioRouting.Add(prefab);

            ItemManager.Instance.AddItem(new CustomItem(prefab, false, new ItemConfig
            {
                CraftingStation = CraftingStations.GaldrTable,
                RepairStation = CraftingStations.GaldrTable,
                MinStationLevel = StationLevel,
                Requirements = item.Requirements,
            }));
        }

        // A look of its own on the back: the game shows an item's child attach_back there, if it has one, instead of its
        // attach (VisEquipment.AttachItem). This one is a copy of its attach, its model's mesh (attach's only one) swapped
        // for the part "closed" of the embedded file `resource` (Staves\ModelParts), which keeps its submeshes' materials
        private static void AddBackLook(GameObject prefab, string resource)
        {
            Transform attach = prefab.transform.Find("attach");
            MeshFilter[] filters = attach ? attach.GetComponentsInChildren<MeshFilter>(true) : new MeshFilter[0];
            Dictionary<string, Staves.ModelParts.Part> parts = Staves.ModelParts.Load(resource, "Nhx" + prefab.name + "_");
            if (filters.Length != 1 || parts == null || !parts.TryGetValue("closed", out Staves.ModelParts.Part part))
            {
                Plugin.Log.LogWarning($"No look on the back for {prefab.name}: {(filters.Length != 1 ? $"its attach has {filters.Length} meshes, not 1" : resource + " isn't in the DLL as expected")}.");
                return;
            }
            GameObject back = Object.Instantiate(attach.gameObject, prefab.transform, false);
            back.name = "attach_back";
            // Hidden on the item as it lies on the ground (the game shows the copy it makes); and first, as the game takes
            // whichever of attach_back and attach it finds first
            back.SetActive(false);
            back.transform.SetSiblingIndex(0);
            MeshFilter model = back.GetComponentInChildren<MeshFilter>(true);
            model.sharedMesh = part.m_mesh;
            PlaceBackLook(model.transform, part.m_mesh);
            // What the back has no use for: the effects when it's held (the game doesn't start them there), and the
            // collider, the open parasol's shape
            Transform equipped = back.transform.Find("equiped");
            if (equipped)
            {
                Object.DestroyImmediate(equipped.gameObject);
            }
            foreach (Collider collider in back.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }
        }

        // Where the closed parasol lies on the back. The game puts an item's attach_back on its back joint as it is, there
        // with its tip over the right shoulder and its handle at the left hip; the user (2026-11-28, from a screenshot):
        // "flip it both horizontal and vertical so that the handle is the one sticking over the right shoulder? And maybe to
        // push it lower down on the back too?". In the joint's frame its shaft runs along z, its tip toward +z. It's turned
        // end over end (around x) and slid back along z so that its handle's end is where its tip was: the same line on the
        // back, its ends swapped. Then BackDown meters further down that line (toward the left hip), and turned BackRoll
        // degrees around its shaft (which side of the paper faces out). The user's picks (2026-11-28, tried in the game with
        // a console command since removed: "I used 0.55 45")
        private const float BackDown = 0.55f;
        private const float BackRoll = 45f;

        private static void PlaceBackLook(Transform model, Mesh mesh)
        {
            // The handle's end (the mesh's lowest z), turned end over end, comes where the tip (its highest) was
            float span = (mesh.bounds.min.z + mesh.bounds.max.z) * model.localScale.z;
            Quaternion turn = Quaternion.AngleAxis(180f, Vector3.right) * Quaternion.AngleAxis(BackRoll, Vector3.forward);
            model.localPosition = turn * model.localPosition + Vector3.forward * (span - BackDown);
            model.localRotation = turn * model.localRotation;
        }
    }
}
