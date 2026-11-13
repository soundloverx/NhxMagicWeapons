using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Staves.StaffOfStorms
{
    // Lightning Step: with the whole Stormcaller's Raiment on and the Staff of Storms in hand, a dodge is a blink instead
    // (the user's pick, 2026-11-10: "blink for 12m, avoiding getting stuck in terrain or objects"): the player turns into
    // lightning and is s_distance farther along the dodge's direction at once, for eitr instead of the roll's stamina.
    // The way is walked in short steps along the ground: up or down no more than a step allows, never through anything
    // solid (checked at knee and chest height), never into water too deep to wade, with room to stand; it stops at the
    // last spot that passes. Enemies along the way are struck (s_damage of the bolt's hit), and the farthest starts the
    // staff's chain. Every game draws lightning from where they were to where they are (LightningBolt.Arc), with the
    // staff's crackle where they leave and its bang where they land. Without the eitr, or without room for a meter, it's
    // the usual roll
    internal static class LightningStep
    {
        // Set by the staff (Staff.cs)
        internal static string s_staff;
        internal static int s_status;
        internal static GameObject s_bolt;
        internal static GameObject[] s_leaveEffects = new GameObject[0];
        internal static GameObject[] s_landEffects = new GameObject[0];
        internal static float s_distance = 12f;
        internal static float s_eitr = 20f;
        internal static float s_damage = 0.5f;
        internal static float s_width = 1.5f;

        // Walked in steps this long, each up at most MaxRise (about 50 degrees) or down at most MaxDrop; under MinDistance
        // it's not worth it
        private const float Step = 0.5f;
        private const float MaxRise = 0.6f;
        private const float MaxDrop = 2f;
        private const float MinDistance = 1f;
        // How far under the sea's level the ground may be: wading, not swimming
        private const float MaxWading = 0.8f;
        // Nothing solid in the way at these heights
        private const float Knee = 0.4f;
        private const float Chest = 1.4f;
        // Room to stand: the player's capsule, this much above the ground and this much thinner (on a slope, the ground
        // would touch it)
        private const float Lift = 0.25f;
        private const float Thinner = 0.8f;

        private static readonly List<Character> s_nearby = new List<Character>();

        // Where the game rolls (Player.UpdateDodge, on the player's own game: a queued dodge, done when they're on the
        // ground and free), the blink goes first, and the roll isn't done
        [HarmonyPatch(typeof(Player), "UpdateDodge")]
        private static class Dodge
        {
            private static void Prefix(Player __instance, float dt, ref float ___m_queuedDodgeTimer, Vector3 ___m_queuedDodgeDir)
            {
                if (___m_queuedDodgeTimer - dt <= 0f || !Ready(__instance))
                {
                    return;
                }
                if (Blink(__instance, ___m_queuedDodgeDir))
                {
                    ___m_queuedDodgeTimer = 0f;
                }
            }
        }

        // As the game's roll asks, and the set, the staff in hand, and the eitr
        private static bool Ready(Player player)
        {
            if (s_staff == null || player != Player.m_localPlayer || !player.IsOnGround() || player.IsDead() || player.InAttack()
                || player.IsEncumbered() || player.InDodge() || player.IsStaggering())
            {
                return false;
            }
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            return weapon != null && weapon.m_shared.m_name == s_staff && player.GetSEMan().HaveStatusEffect(s_status) && player.HaveEitr(s_eitr);
        }

        private static bool Blink(Player player, Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
            {
                dir = player.transform.forward;
                dir.y = 0f;
            }
            dir.Normalize();
            Vector3 start = player.transform.position;
            if (!Walk(player, start, dir, s_distance, out Vector3 end))
            {
                return false;
            }

            ItemDrop.ItemData staff = player.GetCurrentWeapon();
            int level = staff.m_quality;
            Vector3 from = player.GetCenterPoint();
            Vector3 to = end + (from - start);
            player.UseEitr(s_eitr);
            Quaternion rotation = Quaternion.LookRotation(dir);
            player.transform.SetPositionAndRotation(end, rotation);
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body)
            {
                body.position = end;
                body.rotation = rotation;
            }

            Strike(player, staff, from, to, level);
            StaffEffects.Create(s_leaveEffects, from, rotation);
            StaffEffects.Create(s_landEffects, to, rotation);
            if (s_bolt)
            {
                LightningBolt.Arc(s_bolt, player, from, to, level);
            }
            return true;
        }

        // The farthest spot the player can blink to along `dir`, within `reach`, walked in steps from `start` (their feet).
        // The Firecaller's Regalia's comet goes the same way (CometDash)
        internal static bool Walk(Player player, Vector3 start, Vector3 dir, float reach, out Vector3 end)
        {
            CapsuleCollider capsule = player.GetCollider() as CapsuleCollider;
            float radius = capsule ? capsule.radius : 0.4f;
            float height = capsule ? capsule.height : 1.8f;
            float water = ZoneSystem.instance ? ZoneSystem.instance.m_waterLevel : float.MinValue;
            Vector3 last = start;
            for (float distance = Step; distance <= reach + 0.001f; distance += Step)
            {
                Vector3 probe = start + dir * distance;
                probe.y = last.y;
                // The ground there, from no higher than a step up: none within a step down is a drop too deep
                if (!Physics.Raycast(probe + Vector3.up * MaxRise, Vector3.down, out RaycastHit hit, MaxRise + MaxDrop, StaffHits.GroundMask, QueryTriggerInteraction.Ignore))
                {
                    break;
                }
                Vector3 ground = hit.point;
                if (ground.y < water - MaxWading || Blocked(last, ground) || !Free(ground, radius, height))
                {
                    break;
                }
                last = ground;
            }
            end = last;
            return (end - start).magnitude >= MinDistance;
        }

        private static bool Blocked(Vector3 from, Vector3 to)
        {
            return Physics.Linecast(from + Vector3.up * Knee, to + Vector3.up * Knee, StaffHits.SightMask, QueryTriggerInteraction.Ignore)
                || Physics.Linecast(from + Vector3.up * Chest, to + Vector3.up * Chest, StaffHits.SightMask, QueryTriggerInteraction.Ignore);
        }

        private static bool Free(Vector3 ground, float radius, float height)
        {
            float r = radius * Thinner;
            return !Physics.CheckCapsule(ground + Vector3.up * (Lift + r), ground + Vector3.up * Mathf.Max(Lift + r, height - r), r,
                StaffHits.SightMask, QueryTriggerInteraction.Ignore);
        }

        // The enemies along the way, from the player's middle where they were to where they are: struck, the farthest
        // starting a chain
        private static void Strike(Player player, ItemDrop.ItemData staff, Vector3 from, Vector3 to, int level)
        {
            Vector3 path = to - from;
            float length = path.magnitude;
            if (length < 0.01f)
            {
                return;
            }
            Vector3 dir = path / length;
            HitData hit = null;
            Character farthest = null;
            float farthestAlong = -1f;
            s_nearby.Clear();
            Character.GetCharactersInRange((from + to) * 0.5f, length * 0.5f + s_width + 1f, s_nearby);
            foreach (Character character in s_nearby)
            {
                if (character == player || character.IsDead())
                {
                    continue;
                }
                Vector3 center = character.GetCenterPoint();
                float along = Mathf.Clamp(Vector3.Dot(center - from, dir), 0f, length);
                if ((from + dir * along - center).sqrMagnitude > s_width * s_width || !StaffHits.IsValidTarget(player, character))
                {
                    continue;
                }
                if (hit == null)
                {
                    hit = StepHit(player, staff);
                }
                StaffHits.Damage(player, hit, character.GetCollider(), center, dir, out _);
                if (along > farthestAlong)
                {
                    farthest = character;
                    farthestAlong = along;
                }
            }
            s_nearby.Clear();
            if (farthest)
            {
                StaffHits.OnCharacterHit(player, hit);
                LightningBolt.ChainFrom(s_bolt, player, farthest, hit, level);
            }
        }

        // The staff's hit, as its attacks make it (Attack.FireProjectileBurst: the weapon's damage at its level, the
        // caster's skill, their status effects), times s_damage
        private static HitData StepHit(Player player, ItemDrop.ItemData staff)
        {
            Skills.SkillType skill = staff.m_shared.m_skillType;
            HitData hit = new HitData();
            hit.m_toolTier = (short)staff.m_shared.m_toolTier;
            hit.m_pushForce = staff.m_shared.m_attackForce * s_damage;
            hit.m_damage.Add(staff.GetDamage());
            hit.m_damage.Modify(s_damage * player.GetRandomSkillFactor(skill));
            hit.m_skill = skill;
            hit.m_skillRaiseAmount = 1f;
            hit.m_blockable = staff.m_shared.m_blockable;
            hit.m_dodgeable = staff.m_shared.m_dodgeable;
            hit.SetAttacker(player);
            player.GetSEMan().ModifyAttack(skill, ref hit);
            return hit;
        }
    }
}
