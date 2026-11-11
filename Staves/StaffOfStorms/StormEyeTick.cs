using UnityEngine;

namespace NhxMagicWeapons.Staves.StaffOfStorms
{
    // One shot of the Eye of the Storm's held attack (every second, each paying its eitr), on the caster's game: a strike of
    // the caster's storm (StormEye). Created by the attack (Attack.FireProjectileBurst) for the moment it takes: not
    // registered, no ZNetView
    internal class StormEyeTick : MonoBehaviour, IProjectile
    {
        // The storm's prefab
        public GameObject m_eye;

        public void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            if (m_eye && owner)
            {
                StormEye.Tick(m_eye, owner, hitData, item != null ? item.m_quality : 1, hitNoise);
            }
            Destroy(gameObject);
        }

        public string GetTooltipString(int itemQuality)
        {
            return "";
        }
    }
}
