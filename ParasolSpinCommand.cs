using System.Globalization;
using Jotunn.Entities;

namespace NhxMagicWeapons
{
    // A cheat for trying how the Springbloom Parasol spins around its shaft while channelling (it needs devcommands;
    // ParasolChannel.s_spinTurns, s_spinUp; the user, 2026-11-28: "Can the parasol rotate around its shaft during the
    // channel? I feel like that would be a cute touch"):
    //   nhx_parasolspin            what's set now
    //   nhx_parasolspin 1          one turn a second (negative: the other way; 0: no spin)
    //   nhx_parasolspin 1 0.5      and half a second to get there from still, and to stop
    // On this game only, for every player's parasol it shows. To be removed once the user has picked the numbers
    internal class ParasolSpinCommand : ConsoleCommand
    {
        public override string Name => "nhx_parasolspin";
        public override string Help => "[turns a second] [seconds to spin up] - try how the Springbloom Parasol spins while channelling (negative: the other way)";
        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            if (args.Length > 0)
            {
                float spinUp = ParasolChannel.s_spinUp;
                if (!Parse(args[0], out float turns) || turns < -10f || turns > 10f
                    || (args.Length > 1 && (!Parse(args[1], out spinUp) || spinUp < 0f || spinUp > 10f)))
                {
                    context?.AddString($"Usage: {Name} {Help}");
                    return;
                }
                ParasolChannel.s_spinTurns = turns;
                ParasolChannel.s_spinUp = spinUp;
            }
            context?.AddString(ParasolChannel.s_spinTurns == 0f
                ? "The parasol doesn't spin."
                : $"The parasol spins {Text(System.Math.Abs(ParasolChannel.s_spinTurns))} turns a second{(ParasolChannel.s_spinTurns < 0f ? ", the other way" : "")}, taking {Text(ParasolChannel.s_spinUp)} s to get there and to stop.");
        }

        private static bool Parse(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string Text(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
