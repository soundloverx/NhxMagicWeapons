using Jotunn.Entities;
using Jotunn.Managers;

namespace NhxMagicWeapons
{
    // A cheat for testing (it needs devcommands): "nhx_itemlevel <level>" sets the level of the weapon in your hands. The
    // game's spawn command stops at level 4, and the Forge of Potential takes idols and luck
    internal class ItemLevelCommand : ConsoleCommand
    {
        public override string Name => "nhx_itemlevel";
        public override string Help => "[level] - set the level of the weapon in your hands";
        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            Player player = Player.m_localPlayer;
            ItemDrop.ItemData item = player ? player.GetCurrentWeapon() : null;
            // Bare hands are a weapon too, but not one of the inventory's items
            if (item == null || !item.m_equipped)
            {
                context?.AddString("Hold the weapon to change first.");
                return;
            }
            string name = LocalizationManager.Instance.TryTranslate(item.m_shared.m_name);
            // The game keeps it in 16 bits in places (HitData.m_itemLevel)
            if (args.Length < 1 || !int.TryParse(args[0], out int level) || level < 1 || level > short.MaxValue)
            {
                context?.AddString($"Usage: {Name} <level>, from 1. {name} is level {item.m_quality}.");
                return;
            }
            item.m_quality = level;
            // Equipped again, so that its model is made anew at its level (the game only does it when equipment changes)
            player.UnequipItem(item, false);
            player.EquipItem(item, false);
            context?.AddString($"{name} is now level {level}.");
        }
    }
}
