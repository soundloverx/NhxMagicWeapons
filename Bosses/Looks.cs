using System.Collections.Generic;
using Jotunn.Managers;
using NhxMagicWeapons.Staves;
using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Bosses
{
    // What the twins' altar, its arena and the showroom are made of (Showroom, TwinsAltars): copies of the game's models
    // as looks only, the twins' crystals, flat rune rings, lights, sparks and embers. Shared so that the altar is what the
    // user picked in the showroom (2026-11-15: altar C, the twisted pillar bases with crystals for the arena's edge)
    internal static class Looks
    {
        internal static readonly Color Storm = new Color(0.35f, 0.6f, 1f);
        internal static readonly Color Fire = new Color(1f, 0.45f, 0.1f);
        internal static readonly Color Both = new Color(0.85f, 0.55f, 0.9f);

        private static Mesh s_crystalMesh;
        private static bool s_crystalLooked;

        // A copy of the game's prefab `name` as a look only (every script, rigidbody and joint off; its colliders kept),
        // under `parent`. Null, with its name added to `missing`, if the game hasn't it
        internal static GameObject Copy(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, float scale, List<string> missing)
        {
            GameObject prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(name) : null;
            if (!prefab)
            {
                prefab = PrefabManager.Cache.GetPrefab<GameObject>(name);
            }
            if (!prefab)
            {
                if (missing != null && !missing.Contains(name))
                {
                    missing.Add(name);
                }
                return null;
            }
            // Made in the staves' inactive container, so nothing of it wakes up before it's stripped
            GameObject copy = Object.Instantiate(prefab, StaffEffects.Container, false);
            copy.name = name;
            foreach (Joint joint in copy.GetComponentsInChildren<Joint>(true))
            {
                Object.DestroyImmediate(joint);
            }
            // Again while some go (one that another needs only goes after it)
            for (int pass = 0; pass < 3; pass++)
            {
                MonoBehaviour[] behaviours = copy.GetComponentsInChildren<MonoBehaviour>(true);
                if (behaviours.Length == 0)
                {
                    break;
                }
                for (int i = behaviours.Length - 1; i >= 0; i--)
                {
                    if (behaviours[i])
                    {
                        Object.DestroyImmediate(behaviours[i]);
                    }
                }
            }
            foreach (Rigidbody body in copy.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.DestroyImmediate(body);
            }
            copy.transform.SetParent(parent, false);
            copy.transform.localPosition = localPosition;
            copy.transform.localRotation = localRotation;
            copy.transform.localScale = prefab.transform.localScale * scale;
            return copy;
        }

        // Its meshes' bounds in the world, from the meshes themselves (renderers have none while inactive, as in a prefab)
        internal static Bounds MeshBounds(GameObject model)
        {
            bool any = false;
            Bounds bounds = new Bounds(model.transform.position, Vector3.zero);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (!mesh || !renderer || !renderer.enabled || renderer is ParticleSystemRenderer)
                {
                    continue;
                }
                Bounds local = mesh.bounds;
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = local.center + Vector3.Scale(local.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector3 world = matrix.MultiplyPoint3x4(point);
                    if (any)
                    {
                        bounds.Encapsulate(world);
                    }
                    else
                    {
                        bounds = new Bounds(world, Vector3.zero);
                        any = true;
                    }
                }
            }
            return bounds;
        }

        // Its materials copied, their color toward `color` and glowing with it (the showroom's items, the new items)
        internal static void Tint(GameObject model, Color color, float glow)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!materials[i])
                    {
                        continue;
                    }
                    Material material = new Material(materials[i]);
                    if (material.HasProperty("_Color"))
                    {
                        Color original = material.GetColor("_Color");
                        Color tinted = Color.Lerp(original, color, 0.65f);
                        tinted.a = original.a;
                        material.SetColor("_Color", tinted);
                    }
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", color * glow * 0.4f);
                    }
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        internal static Light Glow(Transform parent, Vector3 localPosition, Color color, float intensity, float range)
        {
            GameObject go = new GameObject("light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        // Sparks (the Stormcaller's) or embers (the Firecaller's) about it, `rate` a second within `radius`
        internal static GameObject Motes(Transform parent, Vector3 localPosition, bool fire, float radius, float rate)
        {
            GameObject go = new GameObject(fire ? "embers" : "sparks");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            LookMotes motes = go.AddComponent<LookMotes>();
            motes.m_fire = fire;
            motes.m_radius = radius;
            motes.m_rate = rate;
            return go;
        }

        // A flat rune ring, the sigil's layer `layer` (the sun star, the runes, the flame teeth), `radius` around, turning
        // `turn` degrees a second
        internal static GameObject Disc(Transform parent, Vector3 localPosition, float radius, int layer, Color color, float turn)
        {
            Texture2D[] textures = ChannelSigil.Textures;
            if (textures == null || layer >= textures.Length)
            {
                return null;
            }
            GameObject go = new GameObject("rune");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition + Vector3.up * 0.01f * (layer + 1);
            Mesh mesh = new Mesh { name = "NhxRuneRing" };
            mesh.vertices = new[] { new Vector3(-radius, 0f, -radius), new Vector3(-radius, 0f, radius), new Vector3(radius, 0f, radius), new Vector3(radius, 0f, -radius) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            Material material = LineMaterials.Additive(Staves.WrathOfMuspelheim.Staff.Look ? Staves.WrathOfMuspelheim.Staff.Look.m_glowMaterial : null);
            material.mainTexture = textures[layer];
            if (material.HasProperty("_TintColor"))
            {
                Color tint = color;
                tint.a = 0.5f;
                material.SetColor("_TintColor", tint);
            }
            if (material.HasProperty("_InvFade"))
            {
                material.SetFloat("_InvFade", 30f);
            }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = layer;
            go.AddComponent<LookSpin>().m_speed = turn;
            return go;
        }

        // The staves' head crystal, `size` meters long, upright, with the Stormcaller's robe's blue crystal or the
        // Firecaller's red one; null without them
        internal static GameObject Crystal(Transform parent, bool fire, float size)
        {
            if (!s_crystalLooked)
            {
                s_crystalLooked = true;
                Dictionary<string, ModelParts.Part> parts = ModelParts.Load("staff_of_storms_parts", "NhxTwinsCrystal_");
                s_crystalMesh = parts != null && parts.TryGetValue("crystal_head", out ModelParts.Part part) ? part.m_mesh : null;
            }
            Material material = RobeCrystal(fire ? Armor.StormcallerArmor.FirePieces : Armor.StormcallerArmor.StormPieces);
            if (!s_crystalMesh || !material)
            {
                return null;
            }
            GameObject crystal = new GameObject("crystal");
            crystal.transform.SetParent(parent, false);
            GameObject upright = new GameObject("upright");
            upright.transform.SetParent(crystal.transform, false);
            // The staff's crystal runs along its mesh's z
            upright.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            Bounds bounds = s_crystalMesh.bounds;
            float scale = size / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 1e-6f);
            GameObject model = new GameObject("model");
            model.transform.SetParent(upright.transform, false);
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = -bounds.center * scale;
            model.AddComponent<MeshFilter>().sharedMesh = s_crystalMesh;
            MeshRenderer renderer = model.AddComponent<MeshRenderer>();
            Material[] materials = new Material[s_crystalMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = material;
            }
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return crystal;
        }

        private static Material RobeCrystal(List<GameObject> pieces)
        {
            GameObject robe = pieces.Count > 1 ? pieces[1] : null;
            if (!robe)
            {
                return null;
            }
            foreach (Renderer renderer in robe.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == "x_Crystal")
                {
                    return renderer.sharedMaterial;
                }
            }
            return null;
        }

        // The twins' crystals, blue and red, circling each other `radius` apart about `localPosition`, each with its light
        // and motes
        internal static GameObject Twins(Transform parent, Vector3 localPosition, float radius)
        {
            GameObject pair = new GameObject("twins");
            pair.transform.SetParent(parent, false);
            pair.transform.localPosition = localPosition;
            pair.AddComponent<LookSpin>().m_speed = 25f;
            for (int i = 0; i < 2; i++)
            {
                bool fire = i == 1;
                GameObject holder = new GameObject(fire ? "fire" : "storm");
                holder.transform.SetParent(pair.transform, false);
                holder.transform.localPosition = (fire ? Vector3.right : Vector3.left) * radius;
                LookSpin bob = holder.AddComponent<LookSpin>();
                bob.m_speed = fire ? -60f : 60f;
                bob.m_bob = 0.08f;
                bob.m_phase = i * 0.5f;
                Crystal(holder.transform, fire, 0.7f);
                Glow(holder.transform, Vector3.zero, fire ? Fire : Storm, 2f, 6f);
                Motes(holder.transform, Vector3.zero, fire, 0.35f, 10f);
            }
            return pair;
        }
    }

    // Turning about the world's up (degrees a second), and bobbing (meters, every 2 s)
    internal class LookSpin : MonoBehaviour
    {
        public float m_speed = 30f;
        public float m_bob;
        public float m_phase;
        private Vector3 m_rest;
        private bool m_started;

        private void Update()
        {
            if (!m_started)
            {
                m_started = true;
                m_rest = transform.localPosition;
            }
            transform.Rotate(Vector3.up, m_speed * Time.deltaTime, Space.World);
            if (m_bob > 0f)
            {
                transform.localPosition = m_rest + Vector3.up * m_bob * Mathf.Sin(Mathf.PI * (Time.time + m_phase));
            }
        }
    }

    // Sparks wandering or embers rising about it (the Stormcaller's motes are Thundersurge's sparks, the Firecaller's the
    // Wrath's blast's embers), put out one by one in the world
    internal class LookMotes : MonoBehaviour
    {
        public bool m_fire;
        public float m_radius = 0.3f;
        public float m_rate = 6f;
        private ParticleSystem m_particles;
        private float m_debt;

        private void Start()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            if (m_fire)
            {
                m_particles = FireParticles.Embers(transform, 0.8f, 1.6f, 80);
                return;
            }
            m_particles = StaffEffects.CopyParticles("StaffStormcallerShocker_DO", "attach/equiped/Flash", transform, "the twins' looks have no sparks");
            if (!m_particles)
            {
                return;
            }
            ParticleSystem.MainModule main = m_particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
            main.gravityModifier = 0f;
            main.maxParticles = 80;
            ParticleSystem.NoiseModule noise = m_particles.noise;
            noise.enabled = true;
            noise.strengthMultiplier = 1f;
            noise.frequency = 5f;
            ParticleSystem.ShapeModule shape = m_particles.shape;
            shape.enabled = false;
            ParticleSystem.EmissionModule emission = m_particles.emission;
            emission.rateOverTime = 0f;
            m_particles.Play(true);
        }

        private void Update()
        {
            if (!m_particles)
            {
                return;
            }
            m_debt += m_rate * Time.deltaTime;
            while (m_debt >= 1f)
            {
                m_debt -= 1f;
                Vector3 point = transform.position + Random.insideUnitSphere * m_radius;
                Vector3 velocity = m_fire ? Vector3.up * Random.Range(0.3f, 0.6f) : Random.insideUnitSphere * 0.35f;
                FireParticles.Emit(m_particles, point, velocity);
            }
        }
    }
}
