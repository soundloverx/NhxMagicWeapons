using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace NhxMagicWeapons
{
    // Magic Supremacy 3.1.7's Lightcaller and Bloodcaller sets and its Tempest Eye and Blood Requiem staves, to try out
    // before they become Deep North sets (the user, 2026-11-28; the Bloodcaller set already is one in Magic Supremacy).
    // They're as its prefabs make them, the user's pick (2026-11-28, "Prefabs only"): their stats, attacks, projectiles,
    // set bonuses and auras, without anything Magic Supremacy's own code adds to them. So, as of 3.1.7:
    // - Tempest Eye's secondary attack leaves a lightning field for 30 s. Magic Supremacy makes it a mine that blows once
    //   (its AoeOneHit), 6 at most per player (AoeCount)
    // - the sets' auras have no owner, which Magic Supremacy gives them (PrefabOwnerBinder). The Lightcaller's heals
    //   everyone within 8 m, enemies too, and the Bloodcaller's marks everyone within 30 m, its wearer too
    // - the Bloodcaller's mark does nothing. Magic Supremacy heals the wearer when a marked enemy dies (DeathTriggerSpawn)
    // - Blood Requiem's pellets heal 1 health each (the game's healthReturn), without Magic Supremacy's 1.5 more
    //   (ProjectileLifeSteal). Its secondary attack's zone asks for a status effect SE_BloodDebuff_DO that isn't in the
    //   bundle (it has SE_BloodcallerDebuff_DO), so it only damages, as in Magic Supremacy
    // - the pieces' max health, max eitr and skill levels, which come from Magic Supremacy's code (ExtraStats,
    //   ItemDropExtendedStats), aren't there
    // - each attack costs the primary attack's eitr, as the game charges it (Magic Supremacy's AttackEitrFix charges each
    //   its own, as Staves\AttackEitr does ours): before the skill discount, Tempest Eye's secondary attack 35 rather than
    //   40, and Blood Requiem's 40 eitr on top of its 100 health
    // What this does: the items under our names, crafted at the Galdr Table from Magic Supremacy's recipes (the user's
    // pick, 2026-11-28; Recipes); the prefabs their attacks and auras create, registered so that every game has them; the
    // status effects the auras ask for by name; the sounds through the game's mixer (AudioRouting). All from their own
    // bundle, Plugin.Assets317 (Tools\slim_bundle.py).
    internal static class PrefabItems
    {
        // Magic Supremacy's prefab, and the item made from it here: its name, and its localization key's last part
        // ($item_nhx_...; the names are Magic Supremacy's English ones). For a set's pieces, its set bonus's key's last
        // part ($se_nhx_...)
        private struct Item
        {
            internal string Prefab, Name, Key, Set;
            internal RequirementConfig[] Requirements;
        }

        // The status effects the sets' auras ask for by name (Aoe.m_statusEffect), and their localization keys' last part
        // ($se_nhx_..., and ..._tooltip)
        private static readonly string[][] NamedEffects =
        {
            new[] { "SE_LightcallerAura_DO", "lightcaller_aura" },
            new[] { "SE_BloodcallerDeathTrigger_DO", "bloodcaller_mark" },
        };

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
            // Tempest Eye: an orb of lightning that bursts where it lands (Elemental Magic), and its secondary attack's
            // lightning field
            new Item
            {
                Prefab = "StaffStormcallerSphere_DO", Name = "NhxTempestEye", Key = "tempesteye",
                Requirements = new[]
                {
                    new RequirementConfig("GemstoneBlue", 1, 1),
                    new RequirementConfig("Eitr", 16, 8),
                    new RequirementConfig("FlametalNew", 10, 0),
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
        };

        private static readonly HashSet<StatusEffect> s_setBonuses = new HashSet<StatusEffect>();

        internal static void Add()
        {
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
            AudioRouting.Add(prefab);

            ItemManager.Instance.AddItem(new CustomItem(prefab, false, new ItemConfig
            {
                CraftingStation = CraftingStations.GaldrTable,
                RepairStation = CraftingStations.GaldrTable,
                MinStationLevel = StationLevel,
                Requirements = item.Requirements,
            }));
        }
    }
}
