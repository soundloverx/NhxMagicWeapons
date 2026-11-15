using Jotunn.Entities;
using NhxMagicWeapons.Bosses;
using UnityEngine;

namespace NhxMagicWeapons
{
    // A cheat for the sets' bosses, the twins (Bosses\CallerBosses; needs devcommands), on the bosses near you:
    //   nhx_boss               how many there are, and how to summon them
    //   nhx_boss summon        both twins, linked (they die together: TwinsBond), in front of you, as the altar will
    //   nhx_boss follow        they follow you: walking, running when you're more than 10 m away (BaseAI.Follow)
    //   nhx_boss stay          they stop and wander where they are
    //   nhx_boss level <n>     their armor's level, for the legendary looks (20 at first; 1 for the plain sets)
    //   nhx_boss health <%>    their health, as a share of the most (51 then a hit, or 50, to see their second phase:
    //                          Bosses\TwinsPhase)
    // As a tame's follow and stay (Tameable), on the game that runs their AI: this one, which takes them over
    internal class BossCommand : ConsoleCommand
    {
        public override string Name => "nhx_boss";
        public override string Help => "[summon|follow|stay|level <n>|health <%>] - the armor sets' twin bosses near you";
        public override bool IsCheat => true;

        // How near (meters)
        private const float Reach = 60f;
        // The summon: this far ahead of you
        private const float SummonAhead = 14f;

        public override void Run(string[] args, Terminal context)
        {
            Player player = Player.m_localPlayer;
            if (!player)
            {
                return;
            }
            string what = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (what == "summon")
            {
                Summon(player, context);
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
            if (what != "" && what != "follow" && what != "stay" && what != "level" && what != "health")
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
                    case "follow":
                        boss.m_ai.ResetPatrolPoint();
                        boss.m_ai.SetFollowTarget(player.gameObject);
                        break;
                    case "stay":
                        boss.m_ai.SetFollowTarget(null);
                        boss.m_ai.SetPatrolPoint();
                        break;
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
                context?.AddString($"No boss within {Reach:0} m. Summon the twins with: {Name} summon (or one with spawn {CallerBosses.StormName}, spawn {CallerBosses.FireName})");
                return;
            }
            switch (what)
            {
                case "follow":
                    context?.AddString($"{count} following you.");
                    break;
                case "stay":
                    context?.AddString($"{count} staying here.");
                    break;
                case "level":
                    context?.AddString($"{count} now wearing level {level} armor.");
                    break;
                case "health":
                    context?.AddString($"{count} now at {percent:0.#}% health.");
                    break;
                default:
                    context?.AddString($"{count} within {Reach:0} m. {Name} summon, follow, stay, or level <n> (their armor's level, {CallerBoss.DefaultLevel} at first).");
                    break;
            }
        }

        // Both twins in front of you, side by side, facing you, linked (as an altar calls them, but with no arena)
        private static void Summon(Player player, Terminal context)
        {
            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
            Vector3 center = player.transform.position + forward * SummonAhead;
            context?.AddString(TwinsBond.Summon(center, -forward, null)
                ? "The twins are here, linked: they die together (no arena: an altar's twins have one)."
                : "The twins aren't in the game (see the log).");
        }
    }
}
