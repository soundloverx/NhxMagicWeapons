using System.Collections.Generic;
using Jotunn.Entities;
using NhxMagicWeapons.Armor;

namespace NhxMagicWeapons
{
    // Cheats for trying the armor sets' legendary looks (Armor\ArmorLegend; they need devcommands).
    // "nhx_armorlook" shows the looks at a level and picks which show, on this game:
    //   nhx_armorlook                  what's shown now
    //   nhx_armorlook 20               your set shown at level 20 (0: at its own level); others see it too
    //   nhx_armorlook particles        only these (particles, signature, cloak, runes, or all / none)
    //   nhx_armorlook staff            only with the set's staff in hand ("always": without it too)
    // which can be put together: "nhx_armorlook 20 signature staff"
    internal class ArmorLookCommand : ConsoleCommand
    {
        public override string Name => "nhx_armorlook";
        public override string Help => "[level] [particles|signature|cloak|runes|all|none]... [staff|always] - try the armor sets' legendary looks";
        public override bool IsCheat => true;

        private static readonly Dictionary<string, ArmorLegend.Ideas> s_names = new Dictionary<string, ArmorLegend.Ideas>
        {
            { "particles", ArmorLegend.Ideas.Particles },
            { "signature", ArmorLegend.Ideas.Signature },
            { "cloak", ArmorLegend.Ideas.Cloak },
            { "runes", ArmorLegend.Ideas.Runes },
            { "all", ArmorLegend.Ideas.All },
            { "none", ArmorLegend.Ideas.None },
        };

        public override void Run(string[] args, Terminal context)
        {
            ArmorLegend.Ideas? ideas = null;
            foreach (string arg in args)
            {
                string word = arg.ToLowerInvariant();
                if (int.TryParse(word, out int level) && level >= 0 && level <= short.MaxValue)
                {
                    ArmorLegend.s_previewLevel = level;
                }
                else if (s_names.TryGetValue(word, out ArmorLegend.Ideas idea))
                {
                    ideas = (ideas ?? ArmorLegend.Ideas.None) | idea;
                }
                else if (word == "staff")
                {
                    ArmorLegend.s_staffOnly = true;
                }
                else if (word == "always")
                {
                    ArmorLegend.s_staffOnly = false;
                }
                else
                {
                    context?.AddString($"Unknown: {arg}. Usage: {Name} {Help}");
                    return;
                }
            }
            if (ideas.HasValue)
            {
                ArmorLegend.s_ideas = ideas.Value;
            }
            string shown = ArmorLegend.s_ideas == ArmorLegend.Ideas.None ? "none" : ArmorLegend.s_ideas.ToString().ToLowerInvariant();
            string at = ArmorLegend.s_previewLevel > 0 ? $"your set shown at level {ArmorLegend.s_previewLevel}" : "your set at its own level";
            string staff = ArmorLegend.s_staffOnly ? "only with its staff in hand" : "with or without its staff";
            context?.AddString($"Legendary looks: {shown}; {at}, {staff}. Particles from level {ArmorLegend.ParticlesFrom}, signature at {ArmorLegend.SignatureAt}, cloak from {ArmorLegendCloak.PatternFrom} (glowing from {ArmorLegendCloak.GlowFrom}), runes from 12 (shoulders) to 18 (hood).");
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
