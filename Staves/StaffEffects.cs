using Jotunn.Managers;
using UnityEngine;

namespace NhxMagicWeapons.Staves
{
    // The effects staves create, and the Magic Supremacy effects they use
    internal static class StaffEffects
    {
        private static Transform s_container;

        // Where the staves keep the prefabs they make: inactive, so that their components don't start; their copies do
        internal static Transform Container
        {
            get
            {
                if (!s_container)
                {
                    GameObject container = new GameObject("NhxMagicWeapons prefabs");
                    container.SetActive(false);
                    Object.DontDestroyOnLoad(container);
                    s_container = container.transform;
                }
                return s_container;
            }
        }

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

        // An effect's copy, just created, made `scale` times its size: its particles (their size, speed and where they
        // start) and its lights' reach. Their transforms' scale doesn't do it: its particles scale with it in some
        // settings only (ParticleSystem.scalingMode), and those in world space keep their speed
        internal static void Scale(GameObject effect, float scale, float speed = -1f)
        {
            if (speed < 0f)
            {
                speed = scale;
            }
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particles.main;
                if (main.startSize3D)
                {
                    main.startSizeXMultiplier *= scale;
                    main.startSizeYMultiplier *= scale;
                    main.startSizeZMultiplier *= scale;
                }
                else
                {
                    main.startSizeMultiplier *= scale;
                }
                main.startSpeedMultiplier *= speed;
                ParticleSystem.ShapeModule shape = particles.shape;
                shape.radius *= scale;
                shape.scale *= scale;
                ParticleSystem.NoiseModule noise = particles.noise;
                noise.strengthMultiplier *= scale;
            }
            foreach (Light light in effect.GetComponentsInChildren<Light>(true))
            {
                light.range *= scale;
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
