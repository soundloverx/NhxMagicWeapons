using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;
using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim
{
    // The staff's fire beam. Like the Staff of Storms' bolt, it hits the first thing in line at once, up to its range, and
    // every game draws it from the caster's staff to that point for a moment: one beam, or several braided together at
    // higher levels of the staff. Where it hits, it explodes: everything else around takes part of its damage, in a radius
    // growing with the staff's level, and every game shows the blast at that size.
    // The attack creates it on the caster's game (Attack.FireProjectileBurst), and calls Setup there: that game finds what
    // it hits, deals the damage, creates the shot's sound and stores the beam's end, its caster, the staff's level and
    // whether it hit something in its ZDO. Every game (that one too) then draws the beam and its blast from the ZDO.
    // With the Firecaller's Regalia (its set bonus), each shot is a tick of the caster's channelled beam instead
    // (FireChannel), and this draws nothing.
    internal class FireBeam : MonoBehaviour, IProjectile
    {
        public float m_range = 50f;
        // How long the beam shows, and how long its ZDO lasts so that the other games get it
        public float m_duration = 0.35f;
        public float m_lifetime = 1.5f;
        // Created at the staff's tip when it fires (networked)
        public GameObject[] m_castEffects = new GameObject[0];

        // Drawn by m_look as one beam, and one more every m_levelsPerStrand levels (at 5, 10...), up to m_maxStrands
        public BeamLook m_look;
        public int m_levelsPerStrand = 5;
        public int m_maxStrands = 5;

        // The blast where it hits: m_blastRadius at level 1 and m_blastRadiusPerLevel more each level, up to
        // m_maxBlastLevel. Everything in it but what the beam hit takes this share of the beam's damage
        public float m_blastRadius = 1f;
        public float m_blastRadiusPerLevel = 0.15f;
        public int m_maxBlastLevel = 20;
        public float m_blastDamage = 0.5f;
        // Its effect, made on each game (not networked), scaled from its own size: about m_blastEffectRadius around
        public GameObject m_blast;
        public float m_blastEffectRadius = 3.5f;
        // Its camera shake: this share of the effect's at full size, and only this close
        public float m_blastShake = 0.5f;
        public float m_blastShakeRange = 30f;

        // The staff's secondary attack, told in its tooltip: from which level, and its reach then
        public FlameJet m_flames;

        // The channelled beam (a FireChannel prefab), for a caster with this status effect (the hash of its name)
        public GameObject m_channel;
        public int m_channelStatus;

        private static readonly int s_endKey = "nhx_beam_end".GetStableHashCode();
        private static readonly KeyValuePair<int, int> s_casterKey = ZDO.GetHashZDOID("nhx_beam_caster");
        private static readonly int s_levelKey = "nhx_beam_level".GetStableHashCode();
        private static readonly int s_hitKey = "nhx_beam_hit".GetStableHashCode();

        private ZNetView m_nview;
        private float m_age;
        // From when the ZDO has the beam's end
        private FireBeams m_beams;
        // A tick of a channelled beam: nothing to show
        private bool m_channelled;

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
        }

        public void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            Vector3 spawn = transform.position;
            Vector3 dir = StaffHits.AimAtCrosshair(owner, spawn, velocity.normalized, m_range);
            Vector3 end = spawn + dir * m_range;
            Vector3 from = owner.GetCenterPoint();
            from.y = spawn.y;
            int level = item != null ? item.m_quality : 1;

            if (m_channel && m_channelStatus != 0 && owner.GetSEMan().HaveStatusEffect(m_channelStatus))
            {
                FireChannel.Tick(m_channel, owner, hitData, level, hitNoise);
                m_channelled = true;
                return;
            }

            bool hit = StaffHits.Trace(owner, from, spawn, end, out Vector3 point, out Collider collider);
            if (hit)
            {
                end = point;
                bool hitCharacter = StaffHits.Damage(owner, hitData, collider, point, dir, out Character struck) && struck;
                HashSet<GameObject> done = new HashSet<GameObject> { collider ? Projectile.FindHitObject(collider) : null };
                hitCharacter |= Blast(owner, hitData, point, dir, BlastRadius(level), done);
                if (hitCharacter)
                {
                    StaffHits.OnCharacterHit(owner, hitData);
                }
                BaseAI.DoProjectileHitNoise(point, hitNoise, owner);
            }

            Transform tip = StaffHits.FindTip(owner);
            StaffEffects.Create(m_castEffects, tip ? tip.position : spawn, Quaternion.LookRotation(dir));

            ZDO zdo = m_nview.GetZDO();
            zdo.Set(s_endKey, end);
            zdo.Set(s_casterKey, owner.GetZDOID());
            zdo.Set(s_levelKey, level);
            zdo.Set(s_hitKey, hit);
        }

        // Shown in the staff's tooltip for its level, after a blank line (ItemData.GetTooltip, if m_projectileToolTip). The
        // flamethrower's reach is the local player's: farther with the Firecaller's Regalia on
        public string GetTooltipString(int itemQuality)
        {
            string text = $"$item_nhx_blastradius: <color=orange>{BlastRadius(itemQuality):0.#} m</color>";
            if (m_flames)
            {
                text += itemQuality < m_flames.m_firstLevel
                    ? $"\n$item_nhx_flamethrower: <color=orange>$item_nhx_fromlevel {m_flames.m_firstLevel}</color>"
                    : $"\n$item_nhx_flamereach: <color=orange>{m_flames.Reach(itemQuality, Player.m_localPlayer):0.#} m</color>";
            }
            return text;
        }

        internal float BlastRadius(int level)
        {
            return m_blastRadius + m_blastRadiusPerLevel * (Mathf.Clamp(level, 1, m_maxBlastLevel) - 1);
        }

        // Everything within `radius` of the point but what's `done` (what the beam hit; what's hit is added) takes the
        // blast's share of the hit, pushed away from the point; building pieces too, as from the game's exploding
        // projectiles. Returns whether it hit a character
        internal bool Blast(Character owner, HitData hitData, Vector3 point, Vector3 dir, float radius, HashSet<GameObject> done)
        {
            HitData hit = hitData.Clone();
            hit.m_damage.Modify(m_blastDamage);
            hit.m_pushForce *= m_blastDamage;
            bool hitCharacter = false;
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
                away = away.sqrMagnitude > 0.01f ? away.normalized : dir;
                if (StaffHits.Damage(owner, hit, collider, at, away, out Character damaged) && damaged)
                {
                    hitCharacter = true;
                }
            }
            return hitCharacter;
        }

        private void Update()
        {
            if (!m_nview.IsValid())
            {
                return;
            }
            if (m_channelled)
            {
                ZNetScene.instance.Destroy(gameObject);
                return;
            }
            // On the other games, from when the ZDO has the beam's end
            if (m_beams == null && !Init())
            {
                return;
            }

            m_age += Time.deltaTime;
            if (m_nview.IsOwner() && m_age >= m_lifetime)
            {
                ZNetScene.instance.Destroy(gameObject);
                return;
            }
            m_beams.Draw(m_age);
        }

        private bool Init()
        {
            ZDO zdo = m_nview.GetZDO();
            if (!zdo.GetVec3(s_endKey, out Vector3 end))
            {
                return false;
            }
            ZDOID casterId = zdo.GetZDOID(s_casterKey);
            GameObject caster = casterId.IsNone() ? null : ZNetScene.instance.FindInstance(casterId);
            Transform tip = caster ? StaffHits.FindTip(caster.GetComponent<Character>()) : null;
            int level = zdo.GetInt(s_levelKey, 1);
            // From the staff as it moves, or from where the attack created the beam if the caster isn't around here
            m_beams = new FireBeams(transform, m_look, m_duration, tip, transform.position, end, m_look.Size(level, m_levelsPerStrand, m_maxStrands));

            if (zdo.GetBool(s_hitKey))
            {
                ShowBlast(end, level, 1f);
            }
            return true;
        }

        // The blast's effect at `point`, for a staff at `level`, on this game, shaking the camera by `shake` (0: not at
        // all) of its share
        internal void ShowBlast(Vector3 point, int level, float shake)
        {
            if (!m_blast)
            {
                return;
            }
            GameObject blast = Instantiate(m_blast, point, Quaternion.identity);
            float scale = BlastRadius(level) / m_blastEffectRadius;
            StaffEffects.Scale(blast, scale);
            // As made, every shot would shake the camera hard 50 m around
            CamShaker shaker = blast.GetComponentInChildren<CamShaker>();
            if (shaker)
            {
                shaker.m_strength *= m_blastShake * Mathf.Min(1f, scale) * shake;
                shaker.m_range = Mathf.Min(shaker.m_range, m_blastShakeRange);
            }
        }
    }
}
