using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;
using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim
{
    // One puff of the staff's flamethrower (its secondary attack, held down: the attack fires one every few tenths of a
    // second, each taking its eitr). It burns everything in a cone in front of the staff's head toward the crosshair,
    // reaching farther at higher levels of the staff, except what's behind something solid.
    // The attack creates it on the caster's game only (Attack.FireProjectileBurst; it has no ZNetView). Setup creates the
    // flames at the staff's head (networked, so every game sees and hears them, made as long as the reach), deals the
    // damage and removes this object.
    internal class FlameJet : MonoBehaviour, IProjectile
    {
        // The reach at m_firstLevel, where the flamethrower comes, and m_reachPerLevel more each level, up to m_maxLevel
        public int m_firstLevel = 5;
        public int m_maxLevel = 20;
        public float m_reach = 7f;
        public float m_reachPerLevel = 1f / 3f;
        // The cone: m_width meters around its line at the head, and this many degrees wider on each side farther out
        public float m_width = 0.5f;
        public float m_angle = 12f;
        // Adrenaline for each puff that burns a character (a projectile's default is 2, for one hit)
        public float m_adrenaline = 0.5f;
        // The flames, created at the staff's head (networked, FlameLook)
        public GameObject m_flames;

        internal float Reach(int level)
        {
            return m_reach + m_reachPerLevel * (Mathf.Clamp(level, m_firstLevel, m_maxLevel) - m_firstLevel);
        }

        public void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            float reach = Reach(item != null ? item.m_quality : 1);
            Transform tip = StaffHits.FindTip(owner);
            Vector3 origin = tip ? tip.position : transform.position;
            Vector3 dir = StaffHits.AimAtCrosshair(owner, origin, velocity.normalized, reach);

            if (m_flames)
            {
                FlameLook.SetReach(Instantiate(m_flames, origin, Quaternion.LookRotation(dir)), reach);
            }

            bool hitCharacter = false;
            Vector3 farthest = origin;
            HashSet<GameObject> done = new HashSet<GameObject>();
            foreach (Collider collider in Physics.OverlapSphere(origin, reach + m_width, StaffHits.RayMask))
            {
                GameObject target = Projectile.FindHitObject(collider);
                if (!target || done.Contains(target) || !InCone(collider, origin, dir, reach, out Vector3 point))
                {
                    continue;
                }
                Character character = target.GetComponent<Character>();
                if (character && !StaffHits.IsValidTarget(owner, character))
                {
                    continue;
                }
                done.Add(target);
                if (StaffHits.Damage(owner, hitData, collider, point, dir, out Character burnt) && burnt)
                {
                    hitCharacter = true;
                    farthest = Vector3.Dot(point - origin, dir) > Vector3.Dot(farthest - origin, dir) ? point : farthest;
                }
            }
            if (hitCharacter)
            {
                StaffHits.OnCharacterHit(owner, hitData, m_adrenaline);
                BaseAI.DoProjectileHitNoise(farthest, hitNoise, owner);
            }

            Destroy(gameObject);
        }

        public string GetTooltipString(int itemQuality)
        {
            return "";
        }

        // Whether some of the collider is in the cone and can be seen from its start, and its point nearest the cone's line
        private bool InCone(Collider collider, Vector3 origin, Vector3 dir, float reach, out Vector3 point)
        {
            Vector3 center = collider.bounds.center;
            float along = Mathf.Clamp(Vector3.Dot(center - origin, dir), 0f, reach);
            point = collider.ClosestPointOnBounds(origin + dir * along);
            along = Vector3.Dot(point - origin, dir);
            if (along < 0f || along > reach)
            {
                return false;
            }
            float off = Vector3.Distance(point, origin + dir * along);
            if (off > m_width + along * Mathf.Tan(m_angle * Mathf.Deg2Rad))
            {
                return false;
            }
            // Not through walls or the ground; something solid that's in the way is what's burnt
            return !Physics.Linecast(origin, point, out RaycastHit hit, StaffHits.SightMask, QueryTriggerInteraction.Ignore) || hit.collider == collider;
        }
    }
}
