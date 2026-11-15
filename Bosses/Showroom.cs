using System.Collections.Generic;
using NhxMagicWeapons.Staves;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // Options for the twins' altar, the arena's ring stones and the three new items' models, laid out in front of the
    // player to be walked around and picked (the user, 2026-11-15: of the altar, "Feel free to experiment and show me a
    // few options"; of the Stormheart and Surtr's Tear, "I am not sure yet what sort of 3D object to use for these").
    // Made in game rather than drawn offline, for the glows, particles and the twins' size. The user's picks: altar C, ring
    // stone 3 with option 2's crystal, the blue Morgen heart, the orange dragon tear, the thunder stone (TwinsAltars,
    // TwinsItems). Kept to try more.
    // Only looks, on this game (Looks.Copy), nothing saved or sent (nhx_altar showroom / clear). From the player, facing as
    // they face:
    // - 32 m ahead, three altars 26 m apart: A (left) the game's Ashlands altar with the twins' crystals over it; B (middle)
    //   two of Fader's twisted pillars, storm and fire, around a grausten pedestal with the crystals; C (right) a ruined
    //   floor with a rune circle on it, broken pillars at its corners, the crystals over its middle
    // - 16 m ahead, three ring stones for the arena's edge, each as a pair (storm, fire): 1 a broken pillar with a rune
    //   at its foot, 2 a grausten pillar base with a crystal on it, 3 a twisted pillar base with a light
    // - 5 to 9 m ahead, the items on pedestals, a row each: the Stormheart (nearest: a Morgen heart, a crystal, the
    //   staff's crystal), Surtr's Tear (a dragon tear, a crystal, a surtling core), the lodestone (a Vegvisir shard, a
    //   thunder stone, a grausten), left to right
    internal static class Showroom
    {
        private static GameObject s_root;
        private static readonly List<string> s_missing = new List<string>();
        private static Vector3 s_origin;
        private static Vector3 s_forward;
        private static Vector3 s_right;
        private static Quaternion s_facing;

        internal static string Build(Player player)
        {
            Clear();
            s_missing.Clear();
            s_root = new GameObject("NhxShowroom");
            s_origin = player.transform.position;
            s_forward = player.transform.forward;
            s_forward.y = 0f;
            s_forward = s_forward.sqrMagnitude > 0.01f ? s_forward.normalized : Vector3.forward;
            s_right = Vector3.Cross(Vector3.up, s_forward);
            s_facing = Quaternion.LookRotation(-s_forward);

            AltarA(At(-26f, 32f));
            AltarB(At(0f, 32f));
            AltarC(At(26f, 32f));
            Stones();
            Items();

            return s_missing.Count == 0 ? "The showroom is out (nhx_altar clear to remove it)." :
                "The showroom is out, without: " + string.Join(", ", s_missing) + " (not in the game).";
        }

        internal static void Clear()
        {
            if (s_root)
            {
                Object.Destroy(s_root);
            }
            s_root = null;
        }

        // A point `right` meters to the side and `ahead` meters ahead of where the player stood, on the ground
        private static Vector3 At(float right, float ahead)
        {
            return Ground(s_origin + s_right * right + s_forward * ahead);
        }

        private static Vector3 Ground(Vector3 point)
        {
            if (Physics.Raycast(point + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 150f, StaffHits.GroundMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }
            return point;
        }

        private static GameObject Copy(string name, Vector3 at, Quaternion rotation, float scale)
        {
            return Looks.Copy(name, s_root.transform, at, rotation, scale, s_missing);
        }

        private static float Top(GameObject model) => Looks.MeshBounds(model).max.y;

        private static void AltarA(Vector3 at)
        {
            GameObject altar = Copy("Ashlands_Altar", at, s_facing, 1f);
            float top = altar ? Top(altar) : at.y + 1.5f;
            Looks.Twins(s_root.transform, new Vector3(at.x, top + 1.4f, at.z), 0.55f);
        }

        private static void AltarB(Vector3 at)
        {
            Vector3 left = Ground(at - s_right * 4.5f);
            Vector3 right = Ground(at + s_right * 4.5f);
            GameObject stormPillar = Copy("Ashlands_Boss_Pillar", left, s_facing, 1f);
            GameObject firePillar = Copy("Ashlands_Boss_Pillar", right, s_facing, 1f);
            if (stormPillar)
            {
                Vector3 top = new Vector3(left.x, Top(stormPillar), left.z);
                Looks.Motes(s_root.transform, top, false, 1.2f, 30f);
                Looks.Glow(s_root.transform, top, Looks.Storm, 2.5f, 9f);
            }
            if (firePillar)
            {
                Vector3 top = new Vector3(right.x, Top(firePillar), right.z);
                Looks.Motes(s_root.transform, top, true, 1.2f, 30f);
                Looks.Glow(s_root.transform, top, Looks.Fire, 2.5f, 9f);
            }
            GameObject pedestal = Copy("Piece_grausten_pillarbase_tapered", at, s_facing, 1f);
            float height = pedestal ? Top(pedestal) : at.y + 1f;
            Looks.Twins(s_root.transform, new Vector3(at.x, height + 1.2f, at.z), 0.5f);
        }

        private static void AltarC(Vector3 at)
        {
            GameObject floor = Copy("Ashlands_Ruins_Floor_6x6", at, s_facing, 1f);
            float top = floor ? Top(floor) : at.y;
            Vector3 middle = new Vector3(at.x, top + 0.03f, at.z);
            Looks.Disc(s_root.transform, middle, 2.6f, 0, Looks.Storm, 8f);
            Looks.Disc(s_root.transform, middle, 2.6f, 1, Looks.Both, -5f);
            Looks.Disc(s_root.transform, middle, 2.6f, 2, Looks.Fire, 3f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = Quaternion.AngleAxis(45f + 90f * i, Vector3.up) * s_forward * 3.6f;
                Copy("Ashlands_Pillar4_tip_broken1", new Vector3(at.x + corner.x, top, at.z + corner.z), Quaternion.Euler(0f, 90f * i, 0f), 0.6f);
            }
            Looks.Twins(s_root.transform, middle + Vector3.up * 2.2f, 0.55f);
        }

        private static void Stones()
        {
            string[] models = { "Ashlands_Pillar4_tip_broken2", "Piece_grausten_pillarbase_small", "Ashlands_Ruins_twist_PillarBaseSmall" };
            for (int option = 0; option < 3; option++)
            {
                for (int side = 0; side < 2; side++)
                {
                    bool fire = side == 1;
                    Vector3 at = At(-12f + 12f * option + (fire ? 2.2f : -2.2f), 16f);
                    GameObject stone = Copy(models[option], at, s_facing, option == 0 ? 0.5f : 1f);
                    float top = stone ? Top(stone) : at.y + 1f;
                    Color color = fire ? Looks.Fire : Looks.Storm;
                    switch (option)
                    {
                        case 0:
                            Looks.Disc(s_root.transform, at + Vector3.up * 0.05f - s_forward * 1.2f, 0.8f, 1, color, fire ? -10f : 10f);
                            break;
                        case 1:
                            GameObject crystal = Looks.Crystal(s_root.transform, fire, 0.45f);
                            if (crystal)
                            {
                                crystal.transform.localPosition = new Vector3(at.x, top + 0.35f, at.z);
                                crystal.AddComponent<LookSpin>().m_speed = 30f;
                            }
                            Looks.Glow(s_root.transform, new Vector3(at.x, top + 0.4f, at.z), color, 1.5f, 5f);
                            break;
                        default:
                            Looks.Glow(s_root.transform, new Vector3(at.x, top + 0.3f, at.z), color, 2.2f, 6f);
                            Looks.Motes(s_root.transform, new Vector3(at.x, top, at.z), fire, 0.4f, 8f);
                            break;
                    }
                }
            }
        }

        private static void Items()
        {
            Item("MorgenHeart", At(-2.5f, 5f), Looks.Storm, 2.5f, false);
            Item("Crystal", At(0f, 5f), Looks.Storm, 2.5f, false);
            StaffCrystal(At(2.5f, 5f));
            Item("DragonTear", At(-2.5f, 7f), Looks.Fire, 2.5f, true);
            Item("Crystal", At(0f, 7f), Looks.Fire, 2.5f, true);
            Item("SurtlingCore", At(2.5f, 7f), Looks.Fire, 1.5f, true);
            Item("VegvisirShard_Bonemass", At(-2.5f, 9f), Looks.Both, 1.5f, null);
            Item("Thunderstone", At(0f, 9f), Looks.Both, 1.5f, null);
            Item("Grausten", At(2.5f, 9f), Looks.Both, 1.2f, null);
        }

        private static void Item(string prefab, Vector3 at, Color color, float glow, bool? fire)
        {
            Vector3 point = new Vector3(at.x, Pedestal(at) + 0.45f, at.z);
            GameObject model = Copy(prefab, point, Quaternion.identity, 1f);
            if (!model)
            {
                return;
            }
            model.transform.position += point - Looks.MeshBounds(model).center;
            Looks.Tint(model, color, glow);
            Float(model, point);
            Looks.Glow(s_root.transform, point, color, 1.2f, 3f);
            if (fire != true)
            {
                Looks.Motes(s_root.transform, point, false, 0.2f, 5f);
            }
            if (fire != false)
            {
                Looks.Motes(s_root.transform, point, true, 0.2f, 5f);
            }
        }

        private static void StaffCrystal(Vector3 at)
        {
            Vector3 point = new Vector3(at.x, Pedestal(at) + 0.45f, at.z);
            GameObject crystal = Looks.Crystal(s_root.transform, false, 0.25f);
            if (!crystal)
            {
                return;
            }
            crystal.transform.localPosition = point;
            Float(crystal, point);
            Looks.Glow(s_root.transform, point, Looks.Storm, 1.2f, 3f);
            Looks.Motes(s_root.transform, point, false, 0.2f, 5f);
        }

        // A grausten pillar base, a meter tall, under an item; the height of its top
        private static float Pedestal(Vector3 at)
        {
            GameObject pedestal = Copy("Piece_grausten_pillarbase_small", at, s_facing, 1f);
            if (!pedestal)
            {
                return at.y + 1f;
            }
            float height = Mathf.Max(0.1f, Looks.MeshBounds(pedestal).size.y);
            pedestal.transform.localScale *= 1f / height;
            return Top(pedestal);
        }

        private static void Float(GameObject model, Vector3 point)
        {
            GameObject pivot = new GameObject("float");
            pivot.transform.SetParent(s_root.transform, false);
            pivot.transform.position = point;
            model.transform.SetParent(pivot.transform, true);
            LookSpin spin = pivot.AddComponent<LookSpin>();
            spin.m_speed = 40f;
            spin.m_bob = 0.05f;
        }
    }
}
