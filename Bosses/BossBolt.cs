using System.Collections.Generic;
using NhxMagicWeapons.Staves;
using NhxMagicWeapons.Staves.StaffOfStorms;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // The Stormcaller's bolt from its hands (the user, 2026-11-14: "build the hand bolt too"), so that hunting it has
    // something to come after a player with (until now its only spell reached 35 m, and it never closed in: "things are
    // pretty chill because the stormcaller boss doesn't pursue"). As offered: "the sleeve crackles for about a second, then
    // the bolt fires at where you were when it started, chaining to anyone close to you".
    // One puff of its attack (the Wrath of Muspelheim's held flamethrower attack, both hands held out, BossAttacks), every
    // 0.2 s: the first m_charge puffs are the charge, a thin arc snapping between its hands each time (the staff's bolt, at
    // level 1), the target's middle noted at the first; the last fires the Staff of Storms' bolt from its hands toward that
    // point, on through it up to m_range, hitting the first thing in the way (StaffHits.Trace: the caster's friends and a
    // player dodging are passed through) and chaining from a character it hits as the staff at m_chainLevel would
    // (LightningBolt.ChainFrom: a lone player only takes the bolt), then ends the attack.
    // Created by the attack on the boss's game only, for the moment it takes (no ZNetView); the arcs are networked
    internal class BossBolt : MonoBehaviour, IProjectile
    {
        public int m_charge = 5;
        public float m_range = 30f;
        public int m_chainLevel = 10;
        public int m_lookLevel = 15;
        public int m_chargeLookLevel = 1;
        // The staff's bolt (networked, drawn on every game: LightningBolt), and effects as it starts charging and where it hits
        public GameObject m_bolt;
        public GameObject[] m_chargeEffects = new GameObject[0];
        public GameObject[] m_hitEffects = new GameObject[0];
        // Without a target: this far ahead
        public float m_ahead = 10f;

        // Where each boss's bolt goes, from its charge's start
        private static readonly Dictionary<Character, Vector3> s_aims = new Dictionary<Character, Vector3>();

        public void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            if (!owner)
            {
                Destroy(gameObject);
                return;
            }
            Attack attack = owner is Humanoid humanoid ? humanoid.m_currentAttack : null;
            int puff = attack != null ? attack.m_projectileBurstsFired : m_charge;
            int puffs = attack != null ? attack.m_projectileBursts : m_charge + 1;
            Vector3 hands = BossHands.Between(owner, transform.position);
            Vector3 forward = owner.transform.forward;

            if (puff == 0)
            {
                Character target = owner.GetBaseAI() ? owner.GetBaseAI().GetTargetCreature() : null;
                s_aims[owner] = target ? target.GetCenterPoint() : hands + forward * m_ahead;
                StaffEffects.Create(m_chargeEffects, hands, Quaternion.LookRotation(forward));
            }
            if (puff < puffs - 1)
            {
                // Charging: an arc between its hands
                if (m_bolt && BossHands.Both(owner, out Vector3 left, out Vector3 right))
                {
                    LightningBolt.Arc(m_bolt, owner, left, right, m_chargeLookLevel);
                }
                Destroy(gameObject);
                return;
            }

            if (!s_aims.TryGetValue(owner, out Vector3 aim))
            {
                aim = hands + forward * m_ahead;
            }
            s_aims.Remove(owner);
            Vector3 dir = aim - hands;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : forward;
            StaffHits.Trace(owner, owner.GetCenterPoint(), hands, hands + dir * m_range, out Vector3 point, out Collider collider);
            if (m_bolt)
            {
                LightningBolt.Arc(m_bolt, owner, hands, point, m_lookLevel);
            }
            StaffEffects.Create(m_hitEffects, point, Quaternion.LookRotation(dir));
            if (collider && StaffHits.Damage(owner, hitData, collider, point, dir, out Character struck) && struck && m_bolt)
            {
                LightningBolt.ChainFrom(m_bolt, owner, struck, hitData, m_chainLevel);
            }
            BaseAI.DoProjectileHitNoise(point, hitNoise, owner);
            attack?.Abort();
            Destroy(gameObject);
        }

        public string GetTooltipString(int itemQuality)
        {
            return "";
        }
    }
}
