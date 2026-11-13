using System.Globalization;
using Jotunn.Entities;
using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;

namespace NhxMagicWeapons
{
    // A cheat for trying a look of the Wrath of Muspelheim's beam (it needs devcommands): the weavers fanning out from
    // the main beam's origin and turning around it (FireBeams.s_orbitRadius, s_orbitSpeed, s_orbitFan; the user,
    // 2026-11-13: "I want to see what it would look like if the origins of the thin beams were to rotate around the
    // origin of the main beam slowly").
    //   nhx_beamorbit              what's set now
    //   nhx_beamorbit 0.2 0.33     the weavers fan out to 0.2 m from the main beam and turn around it 0.33 times a
    //                              second (negative: the other way)
    //   nhx_beamorbit 0.2 0.33 1   and they take the beam's first meter to fan out
    //   nhx_beamorbit 0 0          off: the weavers in a fixed frame, as before
    // On this game only, for beams drawn from then on (a channelled beam's thread changes at once). To be removed once the
    // user has picked the numbers
    internal class BeamOrbitCommand : ConsoleCommand
    {
        public override string Name => "nhx_beamorbit";
        public override string Help => "[radius in meters] [turns a second] [meters to fan out] - try the Wrath of Muspelheim's weavers turning around its main beam (0 0: off)";
        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            if (args.Length > 0)
            {
                float speed = FireBeams.s_orbitSpeed;
                float fan = FireBeams.s_orbitFan;
                if (!Parse(args[0], out float radius) || radius < 0f || radius > 5f || (args.Length > 1 && !Parse(args[1], out speed))
                    || (args.Length > 2 && (!Parse(args[2], out fan) || fan < 0f)))
                {
                    context?.AddString($"Usage: {Name} {Help}");
                    return;
                }
                FireBeams.s_orbitRadius = radius;
                FireBeams.s_orbitSpeed = speed;
                FireBeams.s_orbitFan = fan;
            }
            context?.AddString(FireBeams.s_orbitRadius == 0f && FireBeams.s_orbitSpeed == 0f
                ? "The weavers don't turn around the main beam."
                : $"The weavers fan out to {Text(FireBeams.s_orbitRadius)} m from the main beam over its first {Text(FireBeams.s_orbitFan)} m and turn around it {Text(FireBeams.s_orbitSpeed)} times a second.");
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
