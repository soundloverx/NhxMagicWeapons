using NhxMagicWeapons.Staves;
using NhxMagicWeapons.Staves.StaffOfStorms;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // A boss's telegraphed spell (its attack item's projectile, BossAttacks): the Stormcaller's call of lightning, the
    // Firecaller's sigil. A sound in its hands, and a rune under its target where it stands now (ElementRune), which
    // strikes after the rune's delay. Created by the attack on the boss's game only, for the moment it takes (no
    // ZNetView), as the staff's own call (LightningStrike)
    internal class BossCall : MonoBehaviour, IProjectile
    {
        public GameObject m_rune;
        public GameObject[] m_castEffects = new GameObject[0];
        // Without a target (it lost it as it cast): this far ahead
        public float m_ahead = 10f;

        public void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            Vector3 dir = velocity.sqrMagnitude > 0.001f ? velocity.normalized : (owner ? owner.transform.forward : Vector3.forward);
            Character target = owner && owner.GetBaseAI() ? owner.GetBaseAI().GetTargetCreature() : null;
            Vector3 point = LightningStrike.Ground(target ? target.transform.position : transform.position + dir * m_ahead);

            StaffEffects.Create(m_castEffects, BossHands.Between(owner, transform.position), Quaternion.LookRotation(dir));
            ElementRune kind = m_rune ? m_rune.GetComponent<ElementRune>() : null;
            if (kind)
            {
                ElementRune rune = ElementRune.Create(m_rune, point, kind.m_delay, kind.m_radius);
                if (rune)
                {
                    rune.m_caster = owner;
                    rune.m_hit = hitData;
                }
            }
            Destroy(gameObject);
        }

        public string GetTooltipString(int itemQuality)
        {
            return "";
        }
    }

    // Where a boss's hands are: they cast from them, though they aren't drawn
    internal static class BossHands
    {
        // Between both hands (the body's bones, as the animation has them), or `fallback` without them
        internal static Vector3 Between(Character caster, Vector3 fallback)
        {
            Animator animator = caster ? caster.m_animator : null;
            if (!animator || !animator.isHuman)
            {
                return fallback;
            }
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (left && right)
            {
                return (left.position + right.position) * 0.5f;
            }
            return right ? right.position : left ? left.position : fallback;
        }

        // Both hands, if the body has them
        internal static bool Both(Character caster, out Vector3 left, out Vector3 right)
        {
            left = right = Vector3.zero;
            Animator animator = caster ? caster.m_animator : null;
            if (!animator || !animator.isHuman)
            {
                return false;
            }
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (!leftHand || !rightHand)
            {
                return false;
            }
            left = leftHand.position;
            right = rightHand.position;
            return true;
        }
    }
}
