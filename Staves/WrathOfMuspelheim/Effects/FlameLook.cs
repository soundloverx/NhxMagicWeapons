using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects
{
    // The flamethrower's flames (NhxMuspelFlames, Magic Supremacy's Incinerator's), made as long as its reach on every
    // game. The caster's game stores the reach in the effect's ZDO as it creates it (FlameJet); as made, its flames fly
    // about m_madeReach meters. Their particles are slowed to reach as far, and made a little smaller with them
    internal class FlameLook : MonoBehaviour
    {
        public float m_madeReach = 20f;

        private static readonly int s_reachKey = "nhx_flame_reach".GetStableHashCode();

        // On the caster's game, right after creating the effect, so that its first send carries it
        internal static void SetReach(GameObject flames, float reach)
        {
            ZNetView nview = flames ? flames.GetComponent<ZNetView>() : null;
            if (nview && nview.IsValid())
            {
                nview.GetZDO().Set(s_reachKey, reach);
            }
        }

        // After every component's Awake, so that the ZDO is there on every game; its particles started then, so they
        // start again at their new size
        private void Start()
        {
            ZNetView nview = GetComponent<ZNetView>();
            float reach = nview && nview.IsValid() ? nview.GetZDO().GetFloat(s_reachKey) : 0f;
            if (reach <= 0f)
            {
                return;
            }
            float scale = reach / m_madeReach;
            ParticleSystem[] particles = GetComponentsInChildren<ParticleSystem>();
            foreach (ParticleSystem system in particles)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            StaffEffects.Scale(gameObject, Mathf.Lerp(0.6f, 1f, scale), scale);
            foreach (ParticleSystem system in particles)
            {
                system.Play(true);
            }
        }
    }
}
