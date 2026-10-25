using NhxMagicWeapons.Staves.StaffOfStorms.Effects;
using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Staves.StaffOfStorms
{
    // The staff's lightning bolt. Instead of flying like the game's projectiles, it hits the first thing in line at once,
    // up to its range, and every game draws jagged lightning from the caster's staff to that point for a moment: one thin
    // bolt, or several side by side at higher levels of the staff. When it hits an enemy, it may chain: jump on to other
    // enemies nearby, one after the other, each jump drawn the same way.
    // The attack creates it on the caster's game (Attack.FireProjectileBurst), and calls Setup there: that game finds what
    // it hits and where the chain goes, deals the damage, creates the effects and stores the bolt's end, its caster, the
    // staff's level and the chain's enemies in its ZDO. The other games get the ZDO and draw the same bolt from their copy
    // of the caster's staff, and the same jumps.
    internal class LightningBolt : MonoBehaviour, IProjectile
    {
        public float m_range = 50f;
        // How long the bolt and each jump show, and how long its ZDO lasts so that the other games get it
        public float m_duration = 0.3f;
        public float m_lifetime = 1.5f;
        // Created at the staff's tip when it fires, where the bolt hits, and on each enemy the chain jumps to
        public GameObject[] m_castEffects = new GameObject[0];
        public GameObject[] m_hitEffects = new GameObject[0];
        public GameObject[] m_jumpEffects = new GameObject[0];

        // Drawn by m_look as one bolt, and one more every m_levelsPerStrand levels (at 5, 10...), up to m_maxStrands
        public LightningLook m_look;
        public int m_levelsPerStrand = 5;
        public int m_maxStrands = 5;

        // Chain lightning, when the bolt hits an enemy. Its chance and its number of jumps grow with the staff's level: the
        // chance by m_chainChancePerLevel, and a jump more every m_chainLevelsPerJump levels
        public float m_chainChancePerLevel = 0.05f;
        public int m_chainLevelsPerJump = 2;
        public int m_chainMaxJumps = 10;
        // How far it jumps from one enemy to the next, and how many of its jumps the same enemy can take (the bolt's own
        // hit not counted)
        public float m_chainRange = 8f;
        public int m_chainMaxHitsPerEnemy = 2;
        // The first jump's damage as a share of the bolt's, and each next one's as a share of the one before
        public float m_chainDamage = 0.5f;
        public float m_chainFalloff = 0.85f;
        // Between one jump and the next
        public float m_chainJumpDelay = 0.06f;
        // A jump is one bolt, with this share of the bolt's widths, glow, light and forks
        public float m_jumpScale = 0.7f;

        // The level the staff's secondary attack comes at (SecondaryUnlock), told in its tooltip until then
        public int m_secondaryLevel = 1;

        private static readonly int s_endKey = "nhx_bolt_end".GetStableHashCode();
        private static readonly KeyValuePair<int, int> s_casterKey = ZDO.GetHashZDOID("nhx_bolt_caster");
        private static readonly int s_levelKey = "nhx_bolt_level".GetStableHashCode();
        private static readonly int s_jumpsKey = "nhx_bolt_jumps".GetStableHashCode();

        private static readonly List<Character> s_nearby = new List<Character>();
        private static readonly Dictionary<Character, int> s_jumpHits = new Dictionary<Character, int>();

        private ZNetView m_nview;
        private float m_age;
        // The bolt, then its jumps; from when the ZDO has the bolt's end
        private LightningArcs m_arcs;

        // On the caster's game, to deal the jumps' hits as they show
        private Character m_owner;
        private HitData m_hitData;
        private readonly List<Character> m_jumpTargets = new List<Character>();
        private int m_jumpsHit;

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

            Character struck = null;
            if (StaffHits.Trace(owner, from, spawn, end, out Vector3 point, out Collider collider))
            {
                end = point;
                if (StaffHits.Damage(owner, hitData, collider, point, dir, out struck) && struck)
                {
                    StaffHits.OnCharacterHit(owner, hitData);
                }
                StaffEffects.Create(m_hitEffects, point, Quaternion.LookRotation(-dir));
                BaseAI.DoProjectileHitNoise(point, hitNoise, owner);
            }

            Transform tip = StaffHits.FindTip(owner);
            StaffEffects.Create(m_castEffects, tip ? tip.position : spawn, Quaternion.LookRotation(dir));

            ZDO zdo = m_nview.GetZDO();
            zdo.Set(s_endKey, end);
            zdo.Set(s_casterKey, owner.GetZDOID());
            zdo.Set(s_levelKey, level);
            if (struck)
            {
                Chain(owner, struck, hitData, level);
            }
        }

        // Shown in the staff's tooltip for its level, after a blank line (ItemData.GetTooltip, if m_projectileToolTip)
        public string GetTooltipString(int itemQuality)
        {
            string text = $"$item_nhx_chain: <color=orange>{Mathf.RoundToInt(ChainChance(itemQuality) * 100f)}%</color>" +
                $"\n$item_nhx_chainjumps: <color=orange>{ChainJumps(itemQuality)}</color>";
            if (itemQuality < m_secondaryLevel)
            {
                text += $"\n$item_nhx_strike: <color=orange>$item_nhx_fromlevel {m_secondaryLevel}</color>";
            }
            return text;
        }

        private float ChainChance(int level)
        {
            return Mathf.Min(1f, m_chainChancePerLevel * level);
        }

        private int ChainJumps(int level)
        {
            return Mathf.Min(m_chainMaxJumps, (level + m_chainLevelsPerJump - 1) / m_chainLevelsPerJump);
        }

        private float JumpStart(int jump)
        {
            return m_chainJumpDelay * (jump + 1);
        }

        private static KeyValuePair<int, int> JumpTargetKey(int jump)
        {
            return ZDO.GetHashZDOID("nhx_bolt_jump" + jump);
        }

        private static int JumpPointKey(int jump)
        {
            return ("nhx_bolt_jump_point" + jump).GetStableHashCode();
        }

        // On the caster's game: whether the bolt chains from the enemy it struck, and to whom. Stored for every game to draw
        private void Chain(Character owner, Character struck, HitData hitData, int level)
        {
            if (Random.value > ChainChance(level))
            {
                return;
            }
            FindJumps(owner, struck, ChainJumps(level));
            if (m_jumpTargets.Count == 0)
            {
                return;
            }
            m_owner = owner;
            m_hitData = hitData.Clone();

            ZDO zdo = m_nview.GetZDO();
            zdo.Set(s_jumpsKey, m_jumpTargets.Count);
            for (int i = 0; i < m_jumpTargets.Count; i++)
            {
                zdo.Set(JumpTargetKey(i), m_jumpTargets[i].GetZDOID());
                zdo.Set(JumpPointKey(i), m_jumpTargets[i].GetCenterPoint());
            }
        }

        // The enemies the chain jumps to from the one the bolt struck, in order: each time the nearest in range and in
        // sight that it hasn't hit yet, or if none is left, one that hasn't taken m_chainMaxHitsPerEnemy of its jumps. It
        // stops when there's none, so a lone enemy only takes the bolt
        private void FindJumps(Character owner, Character struck, int jumps)
        {
            s_jumpHits.Clear();
            Character from = struck;
            for (int i = 0; i < jumps; i++)
            {
                Vector3 center = from.GetCenterPoint();
                s_nearby.Clear();
                Character.GetCharactersInRange(center, m_chainRange, s_nearby);

                Character next = null;
                bool nextIsNew = false;
                float nextDistance = 0f;
                foreach (Character candidate in s_nearby)
                {
                    if (candidate == from || candidate.IsDead())
                    {
                        continue;
                    }
                    s_jumpHits.TryGetValue(candidate, out int hits);
                    if (hits >= m_chainMaxHitsPerEnemy)
                    {
                        continue;
                    }
                    bool isNew = hits == 0 && candidate != struck;
                    Vector3 point = candidate.GetCenterPoint();
                    float distance = Vector3.Distance(center, point);
                    // Enemies it hasn't hit first, then the nearest
                    if (next && (nextIsNew != isNew ? nextIsNew : nextDistance <= distance))
                    {
                        continue;
                    }
                    if (!StaffHits.IsValidTarget(owner, candidate) || !StaffHits.InSight(center, point))
                    {
                        continue;
                    }
                    next = candidate;
                    nextIsNew = isNew;
                    nextDistance = distance;
                }
                if (!next)
                {
                    break;
                }

                m_jumpTargets.Add(next);
                s_jumpHits.TryGetValue(next, out int done);
                s_jumpHits[next] = done + 1;
                from = next;
            }
            s_jumpHits.Clear();
            s_nearby.Clear();
        }

        private void Update()
        {
            if (!m_nview.IsValid())
            {
                return;
            }
            // On the other games, from when the ZDO has the bolt's end
            if (m_arcs == null && !Init())
            {
                return;
            }

            m_age += Time.deltaTime;
            if (m_nview.IsOwner())
            {
                if (m_age >= m_lifetime + JumpStart(m_arcs.Count - 2))
                {
                    ZNetScene.instance.Destroy(gameObject);
                    return;
                }
                HitJumps();
            }
            m_arcs.Draw(m_age);
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
            LightningSize size = m_look.Size(zdo.GetInt(s_levelKey, 1), 0, m_levelsPerStrand, m_maxStrands);

            m_arcs = new LightningArcs(transform, m_look, m_duration);
            // From the staff as it moves, or from where the attack created the bolt if the caster isn't around here
            LightningAnchor from = new LightningAnchor { m_transform = tip, m_point = transform.position };
            LightningAnchor to = new LightningAnchor { m_point = end };
            m_arcs.Add(from, to, 0f, size);

            // Then from each enemy to the next
            LightningSize jumpSize = new LightningSize
            {
                m_strands = 1,
                m_strandSize = size.m_strandSize * m_jumpScale,
                m_glowSize = size.m_glowSize * m_jumpScale,
                m_branches = size.m_branches * m_jumpScale,
            };
            int jumps = zdo.GetInt(s_jumpsKey);
            for (int i = 0; i < jumps; i++)
            {
                from = to;
                zdo.GetVec3(JumpPointKey(i), out Vector3 point);
                ZDOID targetId = zdo.GetZDOID(JumpTargetKey(i));
                GameObject target = targetId.IsNone() ? null : ZNetScene.instance.FindInstance(targetId);
                to = new LightningAnchor { m_character = target ? target.GetComponent<Character>() : null, m_point = point };
                m_arcs.Add(from, to, JumpStart(i), jumpSize);
            }
            return true;
        }

        // On the caster's game: each jump's hit, when it shows
        private void HitJumps()
        {
            while (m_jumpsHit < m_jumpTargets.Count && m_age >= JumpStart(m_jumpsHit))
            {
                int jump = m_jumpsHit++;
                Character target = m_jumpTargets[jump];
                if (!target || target.IsDead())
                {
                    continue;
                }
                Vector3 point = target.GetCenterPoint();
                Vector3 dir = point - m_arcs.From(jump + 1);
                dir = dir.sqrMagnitude > 0.001f ? dir.normalized : target.transform.forward;

                float share = m_chainDamage * Mathf.Pow(m_chainFalloff, jump);
                HitData hit = m_hitData.Clone();
                hit.m_damage.Modify(share);
                hit.m_pushForce *= share;
                StaffHits.Damage(m_owner, hit, target.GetCollider(), point, dir, out _);
                StaffEffects.Create(m_jumpEffects, point, Quaternion.LookRotation(dir));
            }
        }
    }
}
