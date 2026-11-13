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

        // A copy of a Magic Supremacy effect's particle system (without its children) under `parent`, at scale 1: stopped
        // and empty, looping, in world space, with no bursts, collisions, sub-emitters or lights, for its user to set up.
        // Null, with a warning that `missing`, if the bundle doesn't have it
        internal static ParticleSystem CopyParticles(string effect, string path, Transform parent, string missing)
        {
            GameObject prefab = Plugin.Assets.LoadAsset<GameObject>(effect);
            Transform source = prefab ? prefab.transform.Find(path) : null;
            if (!source || !source.GetComponent<ParticleSystem>())
            {
                Plugin.Log.LogWarning($"No {path} in {effect}: {missing}.");
                return null;
            }
            GameObject copy = Object.Instantiate(source.gameObject, parent, false);
            for (int i = copy.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(copy.transform.GetChild(i).gameObject);
            }
            copy.layer = parent.gameObject.layer;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            ParticleSystem particles = copy.GetComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.duration = 1f;
            main.playOnAwake = false;
            main.prewarm = false;
            main.startDelay = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSize3D = false;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.SetBursts(new ParticleSystem.Burst[0]);
            ParticleSystem.CollisionModule collision = particles.collision;
            collision.enabled = false;
            ParticleSystem.SubEmittersModule sub = particles.subEmitters;
            sub.enabled = false;
            ParticleSystem.LightsModule lights = particles.lights;
            lights.enabled = false;
            return particles;
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
