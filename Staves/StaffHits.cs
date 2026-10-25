using System;
using UnityEngine;

namespace NhxMagicWeapons.Staves
{
    // What a staff's spells hit and how, following the game's projectiles (Projectile.OnHit, IsValidTarget, DoAOE)
    internal static class StaffHits
    {
        // The child of a staff's "attach" model that its spells come out of, see StaffOfStorms.Staff.TipPosition
        internal const string TipName = "nhx_tip";

        // What the game's projectiles collide with (Projectile.s_rayMaskSolids)
        private static int s_rayMask;
        // What a strike can land on: no characters
        private static int s_groundMask;
        // What a chain can't jump through: the ground, rocks, trees, walls; not characters or small props
        private static int s_sightMask;

        internal static int RayMask
        {
            get
            {
                if (s_rayMask == 0)
                {
                    s_rayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain",
                        "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
                }
                return s_rayMask;
            }
        }

        internal static int GroundMask
        {
            get
            {
                if (s_groundMask == 0)
                {
                    s_groundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
                }
                return s_groundMask;
            }
        }

        internal static int SightMask
        {
            get
            {
                if (s_sightMask == 0)
                {
                    s_sightMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
                }
                return s_sightMask;
            }
        }

        // Whether nothing solid stands between two points
        internal static bool InSight(Vector3 from, Vector3 to)
        {
            return !Physics.Linecast(from, to, SightMask, QueryTriggerInteraction.Ignore);
        }

        // Where a spell cast from `spawn` should go: at what's under the crosshair. The attack aims along the caster's look
        // (Attack.GetProjectileSpawnPoint), from a spot in front of them, but the crosshair is the camera's line, from
        // behind their shoulder, and higher or lower as it pitches (GameCamera.GetCameraPosition): along the look, a spell
        // would pass up to a meter or more beside what the crosshair shows. On the caster's game, this aims at the first
        // thing on the camera's line within `range` past the caster (characters the spell can't hit don't count), or at
        // its end; elsewhere, or for a target closer than `spawn`, it keeps `dir`
        internal static Vector3 AimAtCrosshair(Character owner, Vector3 spawn, Vector3 dir, float range)
        {
            GameCamera camera = GameCamera.instance;
            if (!camera || !owner || owner != Player.m_localPlayer)
            {
                return dir;
            }

            Vector3 origin = camera.transform.position;
            Vector3 forward = camera.transform.forward;
            // What's between the camera and the caster doesn't count
            float skip = Mathf.Max(0f, Vector3.Dot(owner.GetCenterPoint() - origin, forward));
            TraceSegment(owner, origin + forward * skip, origin + forward * (skip + range), out Vector3 target, out _);
            Vector3 aim = target - spawn;
            return Vector3.Dot(aim, dir) > MinAimDistance ? aim.normalized : dir;
        }

        // Below this, ahead of `spawn`, a target isn't worth turning for: the trace from the caster to `spawn` finds it
        private const float MinAimDistance = 0.5f;

        // The first thing in the way from the caster's center to `spawn` (where the attack creates the spell, in front of
        // them), then on to `end`. Like the game's projectiles, a wall between the caster and the spot in front of them
        // stops it, and characters it can't hit (the caster, their friends, someone dodging) are passed through.
        internal static bool Trace(Character owner, Vector3 from, Vector3 spawn, Vector3 end, out Vector3 point, out Collider collider)
        {
            return TraceSegment(owner, from, spawn, out point, out collider) || TraceSegment(owner, spawn, end, out point, out collider);
        }

        private static bool TraceSegment(Character owner, Vector3 from, Vector3 to, out Vector3 point, out Collider collider)
        {
            point = to;
            collider = null;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f)
            {
                return false;
            }

            RaycastHit[] hits = Physics.RaycastAll(from, delta / distance, distance, RayMask);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                GameObject target = Projectile.FindHitObject(hit.collider);
                Character character = target ? target.GetComponent<Character>() : null;
                if (character && !IsValidTarget(owner, character))
                {
                    continue;
                }
                point = hit.distance > 0f ? hit.point : from;
                collider = hit.collider;
                return true;
            }
            return false;
        }

        // Projectile.IsValidTarget: not the caster; not a friend unless the caster has PvP on (the game then spares players
        // who don't), though a player can always anger a creature that allows it; not someone dodging
        internal static bool IsValidTarget(Character owner, Character target)
        {
            if (target == owner)
            {
                return false;
            }
            if (owner)
            {
                bool enemy = BaseAI.IsEnemy(owner, target) || (target.GetBaseAI() && target.GetBaseAI().IsAggravatable() && owner.IsPlayer());
                if (!enemy && (!owner.IsPlayer() || !owner.IsPVPEnabled()))
                {
                    return false;
                }
            }
            if (target.IsDodgeInvincible())
            {
                if (target is Player player)
                {
                    player.HitWhileDodging();
                }
                return false;
            }
            return true;
        }

        // Deals the attack's hit (damage already scaled by the caster's skill, push, status effect...) to what `collider`
        // belongs to. Building pieces take it too, as from the game's projectiles (their own resistances apply: iron and
        // black marble shrug off lightning). Returns whether it hit something, and the character if that's what it was.
        internal static bool Damage(Character owner, HitData attack, Collider collider, Vector3 point, Vector3 dir, out Character character)
        {
            character = null;
            GameObject target = Projectile.FindHitObject(collider);
            IDestructible destructible = target ? target.GetComponent<IDestructible>() : null;
            if (destructible == null)
            {
                return false;
            }

            HitData hit = attack.Clone();
            hit.m_hitCollider = collider;
            hit.m_point = point;
            hit.m_dir = dir;
            hit.m_ranged = true;
            destructible.Damage(hit);
            if (hit.m_healthReturn > 0f && owner)
            {
                owner.Heal(hit.m_healthReturn);
            }
            character = destructible as Character;
            return true;
        }

        // Like a projectile that hit a character: the skill rises and the caster gains adrenaline
        internal static void OnCharacterHit(Character owner, HitData attack, float adrenaline = Adrenaline)
        {
            if (!owner)
            {
                return;
            }
            owner.RaiseSkill(attack.m_skill, attack.m_skillRaiseAmount);
            owner.AddAdrenaline(adrenaline);
        }

        // Projectile.m_adrenaline's default
        private const float Adrenaline = 2f;

        // The tip of the staff in the character's hands, on this game's copy of them
        internal static Transform FindTip(Character character)
        {
            Humanoid humanoid = character as Humanoid;
            VisEquipment equipment = humanoid ? humanoid.m_visEquipment : null;
            GameObject item = equipment ? equipment.m_rightItemInstance : null;
            return item ? item.transform.Find(TipName) : null;
        }
    }
}
