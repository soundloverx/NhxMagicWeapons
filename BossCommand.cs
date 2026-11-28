using Jotunn.Entities;
using NhxMagicWeapons.Bosses;
using UnityEngine;

namespace NhxMagicWeapons
{
    // A cheat for the sets' bosses, the twins (Bosses\CallerBosses; needs devcommands), on the bosses near you:
    //   nhx_boss               how many there are
    //   nhx_boss level <n>     their armor's level, for the legendary looks (20 at first; 1 for the plain sets)
    //   nhx_boss health <%>    their health, as a share of the most (51 then a hit, or 50, to see their second phase:
    //                          Bosses\TwinsPhase; one twin low, to see it fall and its twin raise it: TwinsBond)
    //   nhx_boss drain         a stand-in partner on the staff in your hand, or none again: the altar's drain for sharing
    //                          a staff, seen alone (Bosses\TwinsDrain; in god mode the game keeps you at 1 health)
    //   nhx_boss damage [<%>]  the share of their damage the twins deal, for the whole world and every player in it, kept
    //                          with the world (Bosses\BossDamage; 100 as built); without a share, what it is
    // On the game that runs them: this one, which takes them over. They're called at an altar (TwinsAltars). Its summon
    // (linked twins with no arena), follow and stay went on the user's word, 2026-11-17 ("I think you can just remove
    // nhx_boss"; health and level kept, as they work on an altar's twins too): the fight is tested at an altar
    internal class BossCommand : ConsoleCommand
    {
        public override string Name => "nhx_boss";
        public override string Help => "[level <n>|health <%>|drain|damage [<%>]] - the armor sets' twin bosses near you; damage: the share of their damage they deal, for the whole world";
        public override bool IsCheat => true;

        // How near (meters)
        private const float Reach = 60f;

        public override void Run(string[] args, Terminal context)
        {
            Player player = Player.m_localPlayer;
            if (!player)
            {
                return;
            }
            string what = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (what == "drain")
            {
                context?.AddString(TwinsDrain.StandIn(player));
                return;
            }
            if (what == "damage")
            {
                Damage(args, context);
                return;
            }
            int level = 0;
            if (what == "level" && (args.Length < 2 || !int.TryParse(args[1], out level) || level < 1 || level > short.MaxValue))
            {
                context?.AddString($"Usage: {Name} level <level>, from 1.");
                return;
            }
            float percent = 0f;
            if (what == "health" && (args.Length < 2 || !float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out percent)
                || percent <= 0f || percent > 100f))
            {
                context?.AddString($"Usage: {Name} health <percent>, above 0, up to 100.");
                return;
            }
            if (what != "" && what != "level" && what != "health")
            {
                context?.AddString($"Unknown: {args[0]}. Usage: {Name} {Help}");
                return;
            }

            int count = 0;
            foreach (CallerBoss boss in CallerBoss.All)
            {
                if (!boss || !boss.m_nview || !boss.m_nview.IsValid()
                    || Vector3.Distance(boss.transform.position, player.transform.position) > Reach)
                {
                    continue;
                }
                count++;
                if (what == "")
                {
                    continue;
                }
                if (!boss.m_nview.IsOwner())
                {
                    boss.m_nview.ClaimOwnership();
                }
                switch (what)
                {
                    case "level":
                        boss.m_nview.GetZDO().Set(CallerBoss.LevelKey, level);
                        break;
                    case "health":
                        if (!boss.m_humanoid.IsDead())
                        {
                            boss.m_humanoid.SetHealth(boss.m_humanoid.GetMaxHealth() * percent / 100f);
                        }
                        break;
                }
            }

            if (count == 0)
            {
                context?.AddString($"No boss within {Reach:0} m. Call the twins at an altar with a Lodestone of the Elements (or one with spawn {CallerBosses.StormName}, spawn {CallerBosses.FireName}).");
                return;
            }
            switch (what)
            {
                case "level":
                    context?.AddString($"{count} now wearing level {level} armor.");
                    break;
                case "health":
                    context?.AddString($"{count} now at {percent:0.#}% health.");
                    break;
                default:
                    context?.AddString($"{count} within {Reach:0} m. {Name} level <n> (their armor's level, {CallerBoss.DefaultLevel} at first) or health <%>.");
                    break;
            }
        }

        // nhx_boss damage [<%>]: the world's share of the twins' damage, wherever they are. The server keeps it and sends
        // it to every game, this one too (so it reads as set a moment later)
        private void Damage(string[] args, Terminal context)
        {
            float now = BossDamage.Percent;
            if (args.Length < 2)
            {
                context?.AddString($"The twins deal {now:0.##}% of their damage in this world. {Name} damage <percent> to change it, {BossDamage.MinPercent:0} to {BossDamage.MaxPercent:0} (100: as built).");
                return;
            }
            string text = args[1].TrimEnd('%');
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float percent)
                || percent < BossDamage.MinPercent || percent > BossDamage.MaxPercent)
            {
                context?.AddString($"Usage: {Name} damage <percent>, {BossDamage.MinPercent:0} to {BossDamage.MaxPercent:0} (100: as built).");
                return;
            }
            BossDamage.Set(percent);
            context?.AddString($"The twins now deal {percent:0.##}% of their damage in this world, for every player (was {now:0.##}%). Kept with the world.");
        }
    }
}
