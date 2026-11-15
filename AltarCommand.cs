using Jotunn.Entities;
using NhxMagicWeapons.Bosses;
using UnityEngine;

namespace NhxMagicWeapons
{
    // A cheat for the twins' altars (needs devcommands; place and generate are done by the server, for an admin):
    //   nhx_altar place        an altar where you stand, facing you (TwinsAltars.Place: not in a zone with another
    //                          location, nor near one)
    //   nhx_altar generate [n] up to n altars in the world (3 by default), the missing ones placed by the server in the
    //                          Ashlands, clear of every location and of the players' buildings (TwinsAltars.Generate)
    //   nhx_altar clean        clears the arena of the altar nearest you (within 150 m) of rocks, trees and other debris,
    //                          as placing one now does (for altars placed before)
    //   nhx_altar showroom     the options for the altar, the arena's ring stones and the items, laid out ahead of you
    //                          (Bosses\Showroom: looks only, on this game)
    //   nhx_altar clear        removes them
    internal class AltarCommand : ConsoleCommand
    {
        public override string Name => "nhx_altar";
        public override string Help => "place|generate [n]|clean|showroom|clear - the twins' altars";
        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            Player player = Player.m_localPlayer;
            string what = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            switch (what)
            {
                case "place":
                    if (player)
                    {
                        // Its dais facing you
                        Vector3 toward = -player.transform.forward;
                        TwinsAltars.RequestPlace(player.transform.position, Quaternion.LookRotation(new Vector3(toward.x, 0f, toward.z) + Vector3.forward * 1e-4f).eulerAngles.y);
                        context?.AddString("Asked the server for an altar here.");
                    }
                    break;
                case "clean":
                    if (player)
                    {
                        TwinsAltars.RequestClean(player.transform.position);
                        context?.AddString("Asked the server to clear the nearest altar's arena.");
                    }
                    break;
                case "generate":
                    int count = args.Length > 1 && int.TryParse(args[1], out int n) ? n : 0;
                    TwinsAltars.RequestGenerate(count);
                    context?.AddString("Asked the server for the altars (it says where when done).");
                    break;
                case "showroom":
                    if (player)
                    {
                        context?.AddString(Showroom.Build(player));
                    }
                    break;
                case "clear":
                    Showroom.Clear();
                    context?.AddString("The showroom is gone.");
                    break;
                default:
                    context?.AddString($"Usage: {Name} {Help}");
                    break;
            }
        }
    }
}
