using System.Globalization;
using Jotunn.Entities;

namespace NhxMagicWeapons
{
    // A cheat for finding the pose for holding the Springbloom Parasol's shield (it needs devcommands; ParasolPose; the
    // user, 2026-11-28: "let's start with step 1: the pose. I will be the one to figure out first"):
    //   nhx_parasolpose rec      the next attack you make is recorded (the parasol's secondary is sword_secondary);
    //                            stand still for it, or the walk blends into its start
    //   nhx_parasolpose 0.4      your torso and arms held as they were 0.4 s into that attack, your legs and head free
    //   nhx_parasolpose off      not held any more
    //   nhx_parasolpose save     the held pose to BepInEx\config\NhxMagicWeapons.parasol_pose.txt, for the shield
    //   nhx_parasolpose          what's recorded and held
    // Any attack can be recorded, with any weapon in hand: the Wrath of Muspelheim's flamethrower too, the design's
    // fallback. On this game only, and only you see it. To be removed once the user has picked the pose
    internal class ParasolPoseCommand : ConsoleCommand
    {
        public override string Name => "nhx_parasolpose";
        public override string Help => "rec | <seconds> | off | save - record your next attack, then hold your upper body at a moment of it";
        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            if (args.Length == 0)
            {
                context?.AddString(Status());
                return;
            }
            switch (args[0].ToLowerInvariant())
            {
                case "rec":
                    ParasolPose.Arm();
                    context?.AddString("Make the attack you want to hold a moment of. Stand still for it.");
                    return;
                case "off":
                    ParasolPose.Release();
                    context?.AddString("Not held.");
                    return;
                case "save":
                    string path = ParasolPose.Save();
                    context?.AddString(path != null ? $"Saved to {path}." : "Nothing is held to save.");
                    return;
            }
            if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float time) || time < 0f)
            {
                context?.AddString($"Usage: {Name} {Help}");
                return;
            }
            if (!ParasolPose.Hold(time))
            {
                context?.AddString($"Nothing recorded yet: {Name} rec, then make the attack.");
                return;
            }
            context?.AddString(Status());
        }

        private static string Status()
        {
            if (ParasolPose.Armed)
            {
                return "Waiting for your next attack to record it.";
            }
            if (ParasolPose.Frames == 0)
            {
                return "Nothing recorded: nhx_parasolpose rec, then make the attack.";
            }
            string recorded = $"{ParasolPose.Clip ?? "the attack"}, {ParasolPose.Number(ParasolPose.Length)} s ({ParasolPose.Frames} frames)";
            return ParasolPose.Holding
                ? $"Holding your upper body {ParasolPose.Number(ParasolPose.HeldTime)} s into {recorded}."
                : $"Recorded {recorded}. Nothing held.";
        }
    }
}
