using HarmonyLib;
using NhxMagicWeapons.Armor;
using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim
{
    // The comet, one of the Firecaller's Regalia's three showpieces (see Effects\ChannelWings; the user, 2026-11-13: "I
    // want to try all 3 of them on", then "The blink can stay from level 1, like the Stormcaller's"): with the whole set
    // on, at any level, and the Wrath of Muspelheim in hand, a dodge
    // turns the player into a streak of fire that shoots along the dodge's direction and bursts where they reappear,
    // leaving the way burning for a few seconds (FireComet, on every game). Offered as "close to being a red Lightning
    // Step", and built as its twin until the user says otherwise: the same way found (LightningStep.Walk: along the
    // ground, never through anything solid, never into deep water, with room to stand), the same numbers (set by the
    // staff), eitr instead of the roll's stamina, the enemies along the way taking a share of the beam's hit. The burst
    // and the burning way are only looks: they deal nothing more. Without the eitr, or without room for a meter, or with
    // the comet switched off (nhx_armorlook -comet), it's the usual roll
    internal static class CometDash
    {
        // Set by the staff (Staff.cs)
        internal static string s_staff;
        internal static int s_status;
        internal static GameObject s_comet;
        internal static GameObject[] s_leaveEffects = new GameObject[0];
        internal static float s_distance = 12f;
        internal static float s_eitr = 20f;
        internal static float s_damage = 0.5f;
        internal static float s_width = 1.5f;

        private static readonly List<Character> s_nearby = new List<Character>();

        // Where the game rolls (Player.UpdateDodge, on the player's own game), as Lightning Step: the comet goes first,
        // and the roll isn't done
        [HarmonyPatch(typeof(Player), "UpdateDodge")]
        private static class Dodge
        {
            private static void Prefix(Player __instance, float dt, ref float ___m_queuedDodgeTimer, Vector3 ___m_queuedDodgeDir)
            {
                if (___m_queuedDodgeTimer - dt <= 0f || !Ready(__instance))
                {
                    return;
                }
                if (Dash(__instance, ___m_queuedDodgeDir))
                {
                    ___m_queuedDodgeTimer = 0f;
                }
            }
        }

        // As the game's roll asks, and the set, the staff in hand, and the eitr
        private static bool Ready(Player player)
        {
            if (s_staff == null || !ArmorLegend.On(ArmorLegend.Ideas.Comet) || player != Player.m_localPlayer || !player.IsOnGround()
                || player.IsDead() || player.InAttack() || player.IsEncumbered() || player.InDodge() || player.IsStaggering())
            {
                return false;
            }
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            return weapon != null && weapon.m_shared.m_name == s_staff && player.GetSEMan().HaveStatusEffect(s_status) && player.HaveEitr(s_eitr);
        }

        private static bool Dash(Player player, Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
            {
                dir = player.transform.forward;
                dir.y = 0f;
            }
            dir.Normalize();
            Vector3 start = player.transform.position;
            if (!StaffOfStorms.LightningStep.Walk(player, start, dir, s_distance, out Vector3 end))
            {
                return false;
            }

            ItemDrop.ItemData staff = player.GetCurrentWeapon();
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

            Burn(player, staff, from, to);
            StaffEffects.Create(s_leaveEffects, from, rotation);
            if (s_comet)
            {
                FireComet.Create(s_comet, from, to, staff.m_quality);
            }
            return true;
        }

        // The enemies along the way, from the player's middle where they were to where they are, take the comet's hit
        private static void Burn(Player player, ItemDrop.ItemData staff, Vector3 from, Vector3 to)
        {
            Vector3 path = to - from;
            float length = path.magnitude;
            if (length < 0.01f)
            {
                return;
            }
            Vector3 dir = path / length;
            HitData hit = null;
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
                    hit = CometHit(player, staff);
                }
                StaffHits.Damage(player, hit, character.GetCollider(), center, dir, out _);
            }
            s_nearby.Clear();
            if (hit != null)
            {
                StaffHits.OnCharacterHit(player, hit);
            }
        }

        // The staff's hit, as its attacks make it (Attack.FireProjectileBurst: the weapon's damage at its level, the
        // caster's skill, their status effects), times s_damage: as Lightning Step's
        private static HitData CometHit(Player player, ItemDrop.ItemData staff)
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
