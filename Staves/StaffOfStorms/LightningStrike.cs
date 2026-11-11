using NhxMagicWeapons.Staves.StaffOfStorms.Effects;
using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Staves.StaffOfStorms
{
    // The staff's call of lightning: a strike from the sky on the spot the caster aims at, up to its range, that damages
    // everything around it except the caster and their friends, building pieces included.
    // The attack creates it on the caster's game only (Attack.FireProjectileBurst; it has no ZNetView). Setup finds the
    // spot, creates the strike's effect there (networked, so every game sees and hears it), deals the damage and removes
    // this object.
    internal class LightningStrike : MonoBehaviour, IProjectile
    {
        public float m_range = 60f;
        public float m_radius = 3.5f;
        // The strike's effect, created on the ground
        public GameObject m_strike;
        // Created at the staff's tip when it's cast
        public GameObject[] m_castEffects = new GameObject[0];

        // How far down to look for the ground under the spot aimed at
        private const float GroundSearch = 200f;

        public void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            Vector3 spawn = transform.position;
            Vector3 dir = StaffHits.AimAtCrosshair(owner, spawn, velocity.normalized, m_range);
            Vector3 point = Aimed(owner, spawn, dir, m_range);

            Transform tip = StaffHits.FindTip(owner);
            StaffEffects.Create(m_castEffects, tip ? tip.position : spawn, Quaternion.LookRotation(dir));
            if (m_strike)
            {
                // Its lightning is drawn for the staff's level
                StrikeLightning.SetLevel(Instantiate(m_strike, point, Quaternion.identity), item != null ? item.m_quality : 1);
            }

            if (HitArea(owner, hitData, point, m_radius, dir))
            {
                StaffHits.OnCharacterHit(owner, hitData);
            }
            BaseAI.DoProjectileHitNoise(point, hitNoise, owner);

            Destroy(gameObject);
        }

        // Everything within `radius` of `point` but the caster and their friends takes the hit, pushed away from it
        // (along `dir` if right on it); building pieces too. Returns whether it hit a character. Also the storm's strikes
        // Each object once, also across several calls sharing `done` (the storm's bolts of one second)
        internal static bool HitArea(Character owner, HitData hitData, Vector3 point, float radius, Vector3 dir, HashSet<GameObject> done = null)
        {
            bool hitCharacter = false;
            done = done ?? new HashSet<GameObject>();
            foreach (Collider collider in Physics.OverlapSphere(point, radius, StaffHits.RayMask))
            {
                GameObject target = Projectile.FindHitObject(collider);
                if (!target || !done.Add(target))
                {
                    continue;
                }
                Character character = target.GetComponent<Character>();
                if (character && !StaffHits.IsValidTarget(owner, character))
                {
                    continue;
                }
                Vector3 at = collider.ClosestPointOnBounds(point);
                Vector3 away = at - point;
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : dir;
                if (StaffHits.Damage(owner, hitData, collider, at, away, out Character hit) && hit)
                {
                    hitCharacter = true;
                }
            }
            return hitCharacter;
        }

        public string GetTooltipString(int itemQuality)
        {
            return "";
        }

        // Down to what's under the spot (ground, rock, roof), so the strike lands on something when aimed at a creature
        // or at the sky
        // The ground under what's in line from `spawn` along `dir` within `range` (or under its end): where a strike aimed
        // that way lands. Also the storm's aim
        internal static Vector3 Aimed(Character owner, Vector3 spawn, Vector3 dir, float range)
        {
            Vector3 from = owner.GetCenterPoint();
            from.y = spawn.y;
            StaffHits.Trace(owner, from, spawn, spawn + dir * range, out Vector3 aimed, out _);
            return Ground(aimed);
        }

        internal static Vector3 Ground(Vector3 point)
        {
            if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, GroundSearch, StaffHits.GroundMask))
            {
                return hit.point;
            }
            return point;
        }
    }
}
