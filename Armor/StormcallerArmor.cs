using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace NhxMagicWeapons.Armor
{
    // Two sets made from Magic Supremacy's Stormcaller set (an Ashlands set: hood, robe, greaves, cloak): Stormcaller's
    // Raiment as made, and the Firecaller's Regalia in red (FirecallerColors). The user (2026-11-10): both "craftable at
    // Galdr Table lvl 4, with one upgrade at lvl 5 and the rest at the Forge of Potential. That way the armor can still be
    // repaired at home" (the game repairs at a station's level 4 at most).
    // Magic Supremacy's set bonus (90% less fall damage, and an air dash in its code) is gone. The Firecaller's Regalia's,
    // the user's (2026-11-10): "+100% eitr regen and the text 'Unleashes the full power of Muspelheim'", and the Wrath of
    // Muspelheim's beam channelled (FireChannel, which checks for this status effect). Stormcaller's Raiment's (the user's
    // picks, 2026-11-10): with the Staff of Storms, its secondary attack becomes the Eye of the Storm (StormEye) and a dodge
    // Lightning Step (LightningStep), and the robe's crystal crackles (ArmorCrackle); and lightning damage by its pieces'
    // levels, as the Firecaller's fire (LevelSetBonus).
    // The recipes are the user's (2026-11-11). The stats are Magic Supremacy's, kept as they were and written out here
    // (Stats) so they can be read without the game.
    // This builds and registers both sets and their recipes; all their numbers are here.
    internal static class StormcallerArmor
    {
        // From Magic Supremacy, and what each piece is called here
        private static readonly string[] Prefabs =
        {
            "ArmorStormcallerHelmet_DO", "ArmorStormcallerChest_DO", "ArmorStormcallerLegs_DO", "ArmorStormcallerCape_DO",
        };
        private static readonly string[] Pieces = { "Hood", "Robe", "Greaves", "Cloak" };

        private const string StormPrefix = "NhxStormcaller";   // NhxStormcallerHood..., $item_nhx_stormcaller_hood...
        private const string FirePrefix = "NhxFirecaller";
        private const string StormSet = "NhxStormcallersRaiment";
        // The Firecaller's Regalia's name, whose level the channel's sigil and wings go by (FireChannel)
        internal const string FireSet = "NhxFirecallersRegalia";

        // The Firecaller's Regalia's set bonus: its status effect's name, which the Wrath of Muspelheim looks for. Eitr
        // regen +100%: the game adds it to the pieces' own (+152% now), for +252% in all; and like any eitr regen, it
        // stands still while attacking, so a channelled beam drains
        internal const string FireSetBonus = "SE_NhxFirecallersRegalia";
        private const float FireSetEitrRegen = 2f;
        // And fire damage +1% for each level of the set's least upgraded piece (the user's, 2026-11-10): LevelSetBonus
        private const float FireSetFirePerLevel = 0.01f;
        // Stormcaller's Raiment's set bonus: its status effect's name, which the Staff of Storms looks for; eitr regen +100%
        // and lightning damage +1% for each level of the set's least upgraded piece, as the Firecaller's fire (the user's,
        // 2026-11-10)
        internal const string StormSetBonus = "SE_NhxStormcallersRaiment";
        private const float StormSetEitrRegen = 2f;
        private const float StormSetLightningPerLevel = 0.01f;

        // Both sets: with the staff in hand, each hit leaves what it hits one more step weaker to the element (the user,
        // 2026-11-11); the staff alone gives none since 2026-11-14 (Staves\ElementWeakness, through LevelSetBonus)
        private const string FireWeakness = "SE_NhxFireWeaknessStacking";
        private const string StormWeakness = "SE_NhxLightningWeaknessStacking";

        // Each set's glow (crystals, eyes, the shoulder's rim) is this much of its full glow unless its wearer has the
        // whole set on and holds the staff it goes with, fading from one to the other in this time (ArmorGlow; both
        // picked here)
        private const float MinGlow = 0.2f;
        private const float GlowFade = 0.5f;

        // Crafted at the Galdr Table with three of its four extensions; its fourth (the Standing Loom) allows the one
        // upgrade there, as the game asks one more level for each (Recipe.GetRequiredStationLevel). Then the Forge of
        // Potential, which has no maximum and takes a flametal protection idol (Ashlands) for each refinement
        // (NhxIdolProgression moves on to bloodgold idols from level 5)
        private const int StationLevel = 4;
        private const int TableMaxLevel = 2;
        private const string Idol = "Upgrader6Armor";

        // The user's (2026-11-11), the same for both sets: each requirement's amount for crafting, then for the upgrade at
        // the table (the game asks the second for each level above 1; a 0 isn't asked or shown). Only the jute differs:
        // blue for Stormcaller's Raiment, red for the Firecaller's Regalia
        private static RequirementConfig[] Requirements(string piece, string jute)
        {
            switch (piece)
            {
                case "Hood":
                    return new[]
                    {
                        new RequirementConfig("MoltenCore", 2, 0),
                        new RequirementConfig("AsksvinCarrionSkull", 1, 0),
                        new RequirementConfig("AskHide", 10, 10),
                        new RequirementConfig("CelestialFeather", 0, 2),
                        new RequirementConfig(jute, 4, 0),
                        new RequirementConfig(Idol, 1, 0, false),
                    };
                case "Robe":
                    return new[]
                    {
                        new RequirementConfig("MoltenCore", 2, 0),
                        new RequirementConfig("AsksvinCarrionRibcage", 1, 0),
                        new RequirementConfig("FlametalNew", 10, 10),
                        new RequirementConfig("CelestialFeather", 0, 2),
                        new RequirementConfig(jute, 10, 0),
                        new RequirementConfig(Idol, 1, 0, false),
                    };
                case "Greaves":
                    return new[]
                    {
                        new RequirementConfig("MoltenCore", 2, 0),
                        new RequirementConfig("AsksvinCarrionPelvic", 1, 0),
                        new RequirementConfig("FlametalNew", 10, 10),
                        new RequirementConfig("CelestialFeather", 0, 2),
                        new RequirementConfig(jute, 6, 0),
                        new RequirementConfig(Idol, 1, 0, false),
                    };
                default: // Cloak
                    return new[]
                    {
                        new RequirementConfig("AskHide", 10, 0),
                        new RequirementConfig("FlametalNew", 10, 10),
                        new RequirementConfig("CelestialFeather", 4, 2),
                        new RequirementConfig(jute, 10, 0),
                        new RequirementConfig(Idol, 1, 0, false),
                    };
            }
        }

        // Each piece's stats: Magic Supremacy's, as its bundle has them (read 2026-11-11), written over the cloned
        // prefab's so they're seen here. The same for both sets, but for the cloak's resistance: the set's element
        // (lightning for Stormcaller's Raiment, fire for the Firecaller's Regalia). Armor and durability rise by their
        // per-level amount for each level above 1; eitr regen and movement are fractions (0.32 is +32%)
        private struct PieceStats
        {
            internal float Armor, ArmorPerLevel, EitrRegen, Durability, DurabilityPerLevel, Weight, Movement;
            internal HitData.DamageModPair[] Resistances;
        }

        private static PieceStats Stats(string piece, HitData.DamageType element)
        {
            switch (piece)
            {
                case "Hood":
                    return new PieceStats
                    {
                        Armor = 22, ArmorPerLevel = 2, EitrRegen = 0.32f, Durability = 1000, DurabilityPerLevel = 200,
                        Weight = 1, Movement = 0,
                        Resistances = new HitData.DamageModPair[0],
                    };
                case "Robe":
                    return new PieceStats
                    {
                        Armor = 22, ArmorPerLevel = 2, EitrRegen = 0.54f, Durability = 1000, DurabilityPerLevel = 200,
                        Weight = 5, Movement = 0,
                        Resistances = new HitData.DamageModPair[0],
                    };
                case "Greaves":
                    return new PieceStats
                    {
                        Armor = 22, ArmorPerLevel = 2, EitrRegen = 0.54f, Durability = 1000, DurabilityPerLevel = 200,
                        Weight = 5, Movement = 0,
                        // The user's own change (2026-11-13: "those were my manual changes"): Magic Supremacy's greaves
                        // are slightly resistant
                        Resistances = new[] { Resists(HitData.DamageType.Frost, HitData.DamageModifier.Resistant) },
                    };
                default: // Cloak
                    return new PieceStats
                    {
                        Armor = 3, ArmorPerLevel = 1, EitrRegen = 0.12f, Durability = 1500, DurabilityPerLevel = 50,
                        Weight = 4, Movement = 0.09f,
                        Resistances = new[] { Resists(element, HitData.DamageModifier.Resistant) },
                    };
            }
        }

        private static HitData.DamageModPair Resists(HitData.DamageType type, HitData.DamageModifier modifier)
        {
            return new HitData.DamageModPair { m_type = type, m_modifier = modifier };
        }

        internal static void Add()
        {
            FirecallerColors colors = FirecallerColors.Load();
            StatusEffect fireBonus = AddFireSetBonus(colors);
            StatusEffect stormBonus = AddStormSetBonus();
            for (int i = 0; i < Prefabs.Length; i++)
            {
                GameObject model = Plugin.Assets.LoadAsset<GameObject>(Prefabs[i]);
                if (!model)
                {
                    Plugin.Log.LogError($"No {Pieces[i].ToLower()} for Stormcaller's Raiment and the Firecaller's Regalia: {Prefabs[i]} isn't in the asset bundle.");
                    continue;
                }
                GameObject storm = AddPiece(model, StormPrefix, Pieces[i], StormSet, stormBonus, null, Staves.StaffOfStorms.Staff.ItemName);
                GameObject fire = AddPiece(model, FirePrefix, Pieces[i], FireSet, fireBonus, colors, Staves.WrathOfMuspelheim.Staff.ItemName);
                if (Pieces[i] != "Cloak")
                {
                    // The legendary looks' runes (ArmorLegend), on the Firecaller's Regalia only (the user, 2026-11-13: "Can we
                    // keep runes for the Firecaller set only?")
                    AddLegendRunes(fire, FirePrefix, FireSet, Staves.WrathOfMuspelheim.Staff.ItemName, false);
                }
                if (Pieces[i] == "Cloak")
                {
                    // The legendary looks' patterned cloak (ArmorLegend), for each set
                    AddLegendCloak(storm, StormPrefix, StormSet, Staves.StaffOfStorms.Staff.ItemName, true);
                    AddLegendCloak(fire, FirePrefix, FireSet, Staves.WrathOfMuspelheim.Staff.ItemName, false);
                }
                if (Pieces[i] != "Robe")
                {
                    continue;
                }
                // The robe's crystal crackles, with the whole set on and the staff in hand
                Transform skin = storm.transform.Find("attach_skin");
                if (skin)
                {
                    ArmorCrackle crackle = skin.gameObject.AddComponent<ArmorCrackle>();
                    crackle.m_set = System.Array.ConvertAll(Pieces, p => (StormPrefix + p).GetStableHashCode());
                    crackle.m_staff = Staves.StaffOfStorms.Staff.ItemName.GetStableHashCode();
                    crackle.m_look = Staves.StaffOfStorms.Staff.Look;
                    crackle.m_setName = StormSet;
                }
                // The legendary looks at high levels: particles and signatures (ArmorLegend), for each set
                AddLegendFx(storm, StormPrefix, StormSet, Staves.StaffOfStorms.Staff.ItemName, true);
                AddLegendFx(fire, FirePrefix, FireSet, Staves.WrathOfMuspelheim.Staff.ItemName, false);
            }
        }

        // Shown with the robe's icon
        private static StatusEffect AddStormSetBonus()
        {
            LevelSetBonus bonus = ScriptableObject.CreateInstance<LevelSetBonus>();
            bonus.name = StormSetBonus;
            bonus.m_name = "$se_nhx_stormcaller";
            bonus.m_tooltip = "$se_nhx_stormcaller_tooltip";
            bonus.m_eitrRegenMultiplier = StormSetEitrRegen;
            bonus.m_setName = StormSet;
            bonus.m_element = HitData.DamageType.Lightning;
            bonus.m_perLevel = StormSetLightningPerLevel;
            GameObject robe = Plugin.Assets.LoadAsset<GameObject>(Prefabs[1]);
            Sprite[] icons = robe ? robe.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons : null;
            bonus.m_icon = icons != null && icons.Length > 0 ? icons[0] : null;
            bonus.m_staff = Staves.StaffOfStorms.Staff.NameKey;
            bonus.m_weakness = Staves.ElementWeakness.Add(StormWeakness, "$se_nhx_lightningweakness", "$se_nhx_stormcaller_weakness",
                HitData.DamageType.Lightning, bonus.m_icon);
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(bonus, false));
            return bonus;
        }

        // Shown with the robe's icon, in red
        private static StatusEffect AddFireSetBonus(FirecallerColors colors)
        {
            LevelSetBonus bonus = ScriptableObject.CreateInstance<LevelSetBonus>();
            bonus.name = FireSetBonus;
            bonus.m_name = "$se_nhx_firecaller";
            bonus.m_tooltip = "$se_nhx_firecaller_tooltip";
            bonus.m_eitrRegenMultiplier = FireSetEitrRegen;
            bonus.m_setName = FireSet;
            bonus.m_element = HitData.DamageType.Fire;
            bonus.m_perLevel = FireSetFirePerLevel;
            GameObject robe = Plugin.Assets.LoadAsset<GameObject>(Prefabs[1]);
            Sprite[] icons = robe ? robe.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons : null;
            Sprite icon = icons != null && icons.Length > 0 ? icons[0] : null;
            bonus.m_icon = colors != null ? colors.Icon(icon) : icon;
            bonus.m_staff = Staves.WrathOfMuspelheim.Staff.NameKey;
            bonus.m_weakness = Staves.ElementWeakness.Add(FireWeakness, "$se_nhx_fireweakness", "$se_nhx_firecaller_weakness",
                HitData.DamageType.Fire, bonus.m_icon);
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(bonus, false));
            return bonus;
        }

        private static void AddLegendFx(GameObject robe, string prefix, string set, string staff, bool storm)
        {
            Transform skin = robe.transform.Find("attach_skin");
            if (!skin)
            {
                return;
            }
            ArmorLegendFx fx = skin.gameObject.AddComponent<ArmorLegendFx>();
            fx.m_set = System.Array.ConvertAll(Pieces, p => (prefix + p).GetStableHashCode());
            fx.m_staff = staff.GetStableHashCode();
            fx.m_setName = set;
            fx.m_storm = storm;
            fx.m_look = Staves.StaffOfStorms.Staff.Look;
        }

        private static void AddLegendRunes(GameObject piece, string prefix, string set, string staff, bool storm)
        {
            Transform skin = piece.transform.Find("attach_skin");
            if (!skin)
            {
                return;
            }
            ArmorLegendRunes runes = skin.gameObject.AddComponent<ArmorLegendRunes>();
            runes.m_set = System.Array.ConvertAll(Pieces, p => (prefix + p).GetStableHashCode());
            runes.m_staff = staff.GetStableHashCode();
            runes.m_setName = set;
            runes.m_storm = storm;
        }

        private static void AddLegendCloak(GameObject cloak, string prefix, string set, string staff, bool storm)
        {
            Transform skin = cloak.transform.Find("attach_skin");
            if (!skin)
            {
                return;
            }
            ArmorLegendCloak look = skin.gameObject.AddComponent<ArmorLegendCloak>();
            look.m_set = System.Array.ConvertAll(Pieces, p => (prefix + p).GetStableHashCode());
            look.m_staff = staff.GetStableHashCode();
            look.m_setName = set;
            look.m_storm = storm;
        }

        private static GameObject AddPiece(GameObject model, string prefix, string piece, string set, StatusEffect bonus, FirecallerColors colors, string staff)
        {
            GameObject armor = PrefabManager.Instance.CreateClonedPrefab(prefix + piece, model);
            Transform skin = armor.transform.Find("attach_skin");
            if (skin)
            {
                ArmorGlow glow = skin.gameObject.AddComponent<ArmorGlow>();
                glow.m_set = System.Array.ConvertAll(Pieces, p => (prefix + p).GetStableHashCode());
                glow.m_staff = staff.GetStableHashCode();
                glow.m_minGlow = MinGlow;
                glow.m_fadeTime = GlowFade;
            }
            ItemDrop.ItemData.SharedData shared = armor.GetComponent<ItemDrop>().m_itemData.m_shared;
            string key = $"$item_nhx_{prefix.Substring(3).ToLower()}_{piece.ToLower()}";
            shared.m_name = key;
            shared.m_description = key + "_description";
            // Each its own set of four, with its own bonus
            shared.m_setName = set;
            shared.m_setStatusEffect = bonus;
            shared.m_maxQuality = TableMaxLevel;
            PieceStats stats = Stats(piece, prefix == FirePrefix ? HitData.DamageType.Fire : HitData.DamageType.Lightning);
            shared.m_armor = stats.Armor;
            shared.m_armorPerLevel = stats.ArmorPerLevel;
            shared.m_eitrRegenModifier = stats.EitrRegen;
            shared.m_maxDurability = stats.Durability;
            shared.m_durabilityPerLevel = stats.DurabilityPerLevel;
            shared.m_weight = stats.Weight;
            shared.m_movementModifier = stats.Movement;
            shared.m_damageModifiers = new List<HitData.DamageModPair>(stats.Resistances);
            if (prefix == FirePrefix)
            {
                colors?.Apply(armor, shared);
            }

            CustomItem item = new CustomItem(armor, false, new ItemConfig
            {
                CraftingStation = CraftingStations.GaldrTable,
                RepairStation = CraftingStations.GaldrTable,
                MinStationLevel = StationLevel,
                Requirements = Requirements(piece, prefix == FirePrefix ? "JuteRed" : "JuteBlue"),
            });
            // As the game's recipes from the Mistlands on: crafting and the table's upgrade don't take the idol (at a
            // crafting station, upgrader requirements don't count), but it's what lists the piece at the Forge of
            // Potential, which takes one for each refinement. Jotunn has no setting for it
            foreach (Piece.Requirement requirement in item.Recipe.Recipe.m_resources)
            {
                if (requirement.m_resItem && requirement.m_resItem.name.EndsWith(Idol))
                {
                    requirement.m_upgraderResource = true;
                }
            }
            ItemManager.Instance.AddItem(item);
            return armor;
        }
    }
}
