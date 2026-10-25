using Jotunn.Managers;
using UnityEngine;

namespace NhxMagicWeapons.Staves
{
    // The effects staves create, and the Magic Supremacy effects they use
    internal static class StaffEffects
    {
        // Effects with a ZNetView are networked: created here, they show on every game
        internal static void Create(GameObject[] prefabs, Vector3 position, Quaternion rotation)
        {
            foreach (GameObject prefab in prefabs)
            {
                if (prefab)
                {
                    Object.Instantiate(prefab, position, rotation);
                }
            }
        }

        // A copy of a Magic Supremacy effect with its sounds and lights and none of its particles, registered so that every
        // game can create it. Their particles can be too much on top of a staff's own visuals
        internal static GameObject AddWithoutParticles(string name, string effect)
        {
            GameObject source = Plugin.Assets.LoadAsset<GameObject>(effect);
            if (!source)
            {
                Plugin.Log.LogWarning($"{effect} isn't in the asset bundle: the staff that uses it goes without it.");
                return null;
            }
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(name, source);
            foreach (ParticleSystem particles in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                // Gone already if a parent was
                if (!particles)
                {
                    continue;
                }
                if (particles.gameObject != prefab)
                {
                    Object.DestroyImmediate(particles.gameObject);
                    continue;
                }
                // On the root, which holds the rest: it stays, with no particles and nothing drawn
                ParticleSystem.EmissionModule emission = particles.emission;
                emission.enabled = false;
                Renderer renderer = particles.GetComponent<Renderer>();
                if (renderer)
                {
                    renderer.enabled = false;
                }
            }
            PrefabManager.Instance.AddPrefab(prefab);
            AudioRouting.Add(prefab);
            return prefab;
        }
    }
}
