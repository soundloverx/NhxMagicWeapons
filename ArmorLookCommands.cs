using System.Collections.Generic;
using Jotunn.Entities;
using NhxMagicWeapons.Armor;
using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;

namespace NhxMagicWeapons
{
    // Cheats for trying the armor sets' legendary looks and the Firecaller's Regalia's showpieces (Armor\ArmorLegend; they
    // need devcommands). "nhx_armorlook" shows the looks at a level and picks which show, on this game:
    //   nhx_armorlook                  what's shown now
    //   nhx_armorlook 20               your set shown at level 20 (0: at its own level); others see it too
    //   nhx_armorlook particles        only these (particles, signature, cloak, runes, wings, sigil, comet, or all / none)
    //   nhx_armorlook -wings +sigil    what's shown now, without the wings and with the sigil
    //   nhx_armorlook staff            only with the set's staff in hand ("always": without it too)
    //   nhx_armorlook show             your wings and sigil held up without channelling, to look at them ("hide": not)
    // which can be put together: "nhx_armorlook 20 signature staff"
    internal class ArmorLookCommand : ConsoleCommand
    {
        public override string Name => "nhx_armorlook";
        public override string Help => "[level] [[+|-]particles|signature|cloak|runes|wings|sigil|comet|all|none]... [staff|always] [show|hide] - try the armor sets' legendary looks and the Firecaller's showpieces";
        public override bool IsCheat => true;

        private static readonly Dictionary<string, ArmorLegend.Ideas> s_names = new Dictionary<string, ArmorLegend.Ideas>
        {
            { "particles", ArmorLegend.Ideas.Particles },
            { "signature", ArmorLegend.Ideas.Signature },
            { "cloak", ArmorLegend.Ideas.Cloak },
            { "runes", ArmorLegend.Ideas.Runes },
            { "wings", ArmorLegend.Ideas.Wings },
            { "sigil", ArmorLegend.Ideas.Sigil },
            { "comet", ArmorLegend.Ideas.Comet },
            { "all", ArmorLegend.Ideas.All },
            { "none", ArmorLegend.Ideas.None },
        };

        public override void Run(string[] args, Terminal context)
        {
            // Named plainly, they replace what's shown; with + or -, they're added to it or taken from it
            ArmorLegend.Ideas? only = null;
            ArmorLegend.Ideas added = ArmorLegend.Ideas.None;
            ArmorLegend.Ideas removed = ArmorLegend.Ideas.None;
            foreach (string arg in args)
            {
                string word = arg.ToLowerInvariant();
                if (word.Length > 1 && (word[0] == '+' || word[0] == '-') && s_names.TryGetValue(word.Substring(1), out ArmorLegend.Ideas changed))
                {
                    if (word[0] == '+')
                    {
                        added |= changed;
                        removed &= ~changed;
                    }
                    else
                    {
                        removed |= changed;
                        added &= ~changed;
                    }
                }
                else if (int.TryParse(word, out int level) && level >= 0 && level <= short.MaxValue)
                {
                    ArmorLegend.s_previewLevel = level;
                }
                else if (s_names.TryGetValue(word, out ArmorLegend.Ideas idea))
                {
                    only = (only ?? ArmorLegend.Ideas.None) | idea;
                }
                else if (word == "staff")
                {
                    ArmorLegend.s_staffOnly = true;
                }
                else if (word == "always")
                {
                    ArmorLegend.s_staffOnly = false;
                }
                else if (word == "show")
                {
                    ArmorLegend.s_showpieces = true;
                }
                else if (word == "hide")
                {
                    ArmorLegend.s_showpieces = false;
                }
                else
                {
                    context?.AddString($"Unknown: {arg}. Usage: {Name} {Help}");
                    return;
                }
            }
            ArmorLegend.s_ideas = ((only ?? ArmorLegend.s_ideas) | added) & ~removed;
            string shown = ArmorLegend.s_ideas == ArmorLegend.Ideas.None ? "none" : ArmorLegend.s_ideas.ToString().ToLowerInvariant();
            string at = ArmorLegend.s_previewLevel > 0 ? $"your set shown at level {ArmorLegend.s_previewLevel}" : "your set at its own level";
            string staff = ArmorLegend.s_staffOnly ? "only with its staff in hand" : "with or without its staff";
            context?.AddString($"Legendary looks: {shown}; {at}, {staff}. Particles from level {ArmorLegend.ParticlesFrom}, signature at {ArmorLegend.SignatureAt}, cloak from {ArmorLegendCloak.PatternFrom} (glowing from {ArmorLegendCloak.GlowFrom}), runes from 12 (shoulders) to 18 (hood).");
            context?.AddString($"The Firecaller's showpieces: the comet on a dodge with the staff in hand at any level; as the beam is channelled, the sigil from level {ArmorLegend.SigilFrom} and the wings from level {ArmorLegend.WingsFrom}, from its hit {ChannelWings.OpenAtTick} on. Your wings and sigil are {(ArmorLegend.s_showpieces ? "held up without channelling, at those levels (hide: not)" : "shown only while channelling (show: held up to look at)")}.");
        }
    }

    // "nhx_armorlevel <level>" sets the level of the armor you wear (helmet, chest, legs, cape), as nhx_itemlevel for the
    // weapon: the Galdr Table stops at 2, and the Forge of Potential takes idols and luck
    internal class ArmorLevelCommand : ConsoleCommand
    {
        public override string Name => "nhx_armorlevel";
        public override string Help => "[level] - set the level of the armor you wear";
        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            Player player = Player.m_localPlayer;
            if (!player)
            {
                return;
            }
            if (args.Length < 1 || !int.TryParse(args[0], out int level) || level < 1 || level > short.MaxValue)
            {
                context?.AddString($"Usage: {Name} <level>, from 1.");
                return;
            }
            int count = 0;
            foreach (ItemDrop.ItemData item in new[] { player.m_helmetItem, player.m_chestItem, player.m_legItem, player.m_shoulderItem })
            {
                if (item == null)
                {
                    continue;
                }
                item.m_quality = level;
                // Equipped again, so that the set's bonus and the cape's look take the new level
                player.UnequipItem(item, false);
                player.EquipItem(item, false);
                count++;
            }
            context?.AddString(count > 0 ? $"{count} pieces of armor are now level {level}." : "Wear the armor to change first.");
        }
    }
}
