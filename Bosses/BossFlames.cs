using NhxMagicWeapons.Staves.WrathOfMuspelheim;
using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;
using UnityEngine;

namespace NhxMagicWeapons.Bosses
{
    // One puff of the Firecaller's flamethrower (its attack item's projectile, BossAttacks): the Wrath of Muspelheim's
    // held flamethrower attack, fired as the AI's attack. Its first puffs are the wind-up (the user, 2026-11-14: "we don't
    // want the players to be instantly targeted and hit"): small flames in its hands, burning nothing. Then the flames
    // reach out from its hands along its facing, which turns slowly toward its target as the attack goes
    // (Attack.m_speedFactorRotation), so a player can step aside; every few puffs they burn what's in the cone
    // (FlameJet.Burn): a few big hits rather than many small ones, which armor would all but cancel. The last puff ends the
    // attack (it loops until told, as the player's would until the button is let go).
    // Created by the attack on the boss's game only, for the moment it takes (no ZNetView), as the staff's (FlameJet)
    internal class BossFlames : MonoBehaviour, IProjectile
    {
        // Puffs of wind-up; then the first burn this many puffs into the flames, once they've flown out (the Incinerator's
        // flames fly 13 to 20 m a second at the reach they're drawn with here: burning at once, they hit a player 10 m off
        // half a second before reaching them, the user, 2026-11-14: "I found myself being pushed back by nothing [...] it
        // was the firecaller boss with the flamethrower, but quite far from me"), and again every so many puffs
        public int m_windup = 5;
        public int m_firstBurn = 3;
        public int m_burnEvery = 3;
        // The flames' reach while winding up; then how far they're drawn (FlameLook), farther than they burn, as they fade
        // out toward the end of their flight; and the cone they burn (FlameJet's: meters around its line at the hands,
        // degrees wider on each side farther out)
        public float m_windupReach = 0.8f;
        public float m_flameReach = 12f;
        public float m_reach = 10f;
        public float m_width = 0.9f;
        public float m_angle = 12f;
        // From a little ahead of the hands (meters)
        public float m_ahead = 0.4f;
        public GameObject m_flames;

        public void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            Attack attack = owner is Humanoid humanoid ? humanoid.m_currentAttack : null;
            int puff = attack != null ? attack.m_projectileBurstsFired : 0;
            int puffs = attack != null ? attack.m_projectileBursts : 1;
            Vector3 dir = velocity.sqrMagnitude > 0.001f ? velocity.normalized : (owner ? owner.transform.forward : Vector3.forward);
            Vector3 origin = BossHands.Between(owner, transform.position) + dir * m_ahead;
            int flaming = puff - m_windup;

            if (m_flames)
            {
                FlameLook.SetReach(Instantiate(m_flames, origin, Quaternion.LookRotation(dir)), flaming < 0 ? m_windupReach : m_flameReach);
            }
            if (flaming >= m_firstBurn && (flaming - m_firstBurn) % m_burnEvery == 0 && owner)
            {
                if (FlameJet.Burn(owner, hitData, origin, dir, m_reach, m_width, m_angle, out Vector3 farthest))
                {
                    BaseAI.DoProjectileHitNoise(farthest, hitNoise, owner);
                }
            }
            if (attack != null && puff >= puffs - 1)
            {
                attack.Abort();
            }
            Destroy(gameObject);
        }

        public string GetTooltipString(int itemQuality)
        {
            return "";
        }
    }
}
