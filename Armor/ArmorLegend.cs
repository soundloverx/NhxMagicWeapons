using System;
using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Armor
{
    // Legendary looks for both sets at high levels (the user, 2026-11-13: the sets are crafted in the Ashlands but "will
    // be used in the endgame, while being upgraded with idols [...] can we make them look legendary without blinding the
    // players by just increasing the glow intensity?"). The ideas are in the user's document "Legendary armor looks: ideas
    // past level 10". Four were built to be tried in game; the user kept two ("I like the particles and the signatures. I
    // am not a big fan of the breathing lights. And I feel like the whole armor rim detracts from the right shoulder/arm
    // which makes the armor look special"), both in ArmorLegendFx:
    // - Particles: embers (Firecaller) or static sparks and a crackle without the staff (Stormcaller), from level 12
    // - Signature: at 20, flames trailing from the hood's eyes and embers at each step (Firecaller), or crystal shards
    //   circling the shoulders with arcs between them (Stormcaller)
    // Then, after mock-ups of runes and patterned cloth, the patterns on the cloak only (ArmorLegendCloak): from 15, from 20
    // glowing with a faint pulse; and the runes on the other pieces (ArmorLegendRunes), from 12 to 18
    // Both only with the whole set on, like the glow, and at the set's level: its least upgraded piece, as the set's
    // damage bonus (LevelSetBonus). The game tells other games only the cloak's level, so the wearer's game shares the set's
    // level in its ZDO. An armor stand has no level for it: none of these show there
    internal static class ArmorLegend
    {
        [Flags]
        internal enum Ideas
        {
            None = 0,
            Particles = 1,
            Signature = 2,
            Cloak = 4,
            Runes = 8,
            // The Firecaller's Regalia's showpieces (the user, 2026-11-13: "there's nothing that really makes me say 'wow,
            // that's so cool'", then "I want to try all 3 of them on", and having tried them: "I like the visuals"): a
            // sigil on the ground and wings of flame while the Wrath of Muspelheim's beam is channelled (ChannelSigil,
            // ChannelWings, held up by FireChannel), from SigilFrom and WingsFrom; and a dodge as a comet (CometDash), at
            // any level, as Stormcaller's Raiment's Lightning Step
            Wings = 16,
            Sigil = 32,
            Comet = 64,
            All = Particles | Signature | Cloak | Runes | Wings | Sigil | Comet,
        }

        // What this game shows, whether only with the set's staff in hand (not decided yet), and the level shown for the
        // local player's set instead of its own (0: its own). Set with nhx_armorlook, for testing
        internal static Ideas s_ideas = Ideas.All;
        internal static bool s_staffOnly;
        internal static int s_previewLevel;
        // The local player's wings and sigil held up without a channel, to look at them (nhx_armorlook show)
        internal static bool s_showpieces;

        // From which level each shows, and the level where they're at their fullest (higher levels look the same)
        internal const int ParticlesFrom = 12;
        internal const int SignatureAt = 20;
        internal const int FullLevel = 20;
        // The Firecaller's sigil and wings (the user, 2026-11-13: "The blink can stay from level 1, like the
        // Stormcaller's. I think the runes on the ground can appear at level 10 and the wings at level 20")
        internal const int SigilFrom = 10;
        internal const int WingsFrom = 20;

        private static readonly Dictionary<string, int> s_levelKeys = new Dictionary<string, int>();

        internal static bool On(Ideas idea) => (s_ideas & idea) != 0;

        // 0 below `from`, then from `start` at `from` to 1 at FullLevel
        internal static float Ramp(int level, int from, float start)
        {
            return level < from ? 0f : Mathf.Lerp(start, 1f, Mathf.InverseLerp(from, FullLevel, level));
        }

        // The whole set worn, and its staff in hand if the looks need it
        internal static bool Shows(VisEquipment equipment, int[] set, int staff)
        {
            return ArmorGlow.WholeSet(equipment, set) && (!s_staffOnly || ArmorGlow.Wielding(equipment, set, staff));
        }

        // The set's level as `wearer` shows it (call it only with the whole set on): on the wearer's own game from its items
        // (or the preview's, for the local player), which it writes to its ZDO for the others; elsewhere from there. 0 for
        // no one (an armor stand)
        internal static int Level(Character wearer, string setName)
        {
            if (!(wearer is Humanoid humanoid) || !humanoid.m_nview || !humanoid.m_nview.IsValid())
            {
                return 0;
            }
            if (!s_levelKeys.TryGetValue(setName, out int key))
            {
                key = ("nhx_armor_level_" + setName).GetStableHashCode();
                s_levelKeys[setName] = key;
            }
            ZDO zdo = humanoid.m_nview.GetZDO();
            if (!humanoid.m_nview.IsOwner())
            {
                return zdo.GetInt(key);
            }
            int level = wearer == Player.m_localPlayer && s_previewLevel > 0 ? s_previewLevel : LevelSetBonus.LowestLevel(humanoid, setName);
            if (zdo.GetInt(key) != level)
            {
                zdo.Set(key, level);
            }
            return level;
        }
    }
}
