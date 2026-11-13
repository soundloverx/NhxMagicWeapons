using System.Collections.Generic;
using HarmonyLib;
using NhxMagicWeapons.Staves;
using NhxMagicWeapons.Staves.StaffOfStorms.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Armor
{
    // Two of the legendary looks being tried at high levels (ArmorLegend), on the robe's attach_skin of each set, on every
    // game but a dedicated server:
    // - Particles, from level 12 (a quarter of them), all at 20: the Firecaller's Regalia gives off embers from the
    //   shoulders, the hood and the cloak's hem (more of them as the wearer walks: a trail); Stormcaller's Raiment static
    //   motes crawling around the body (and the robe's crystal crackles without the staff now and then: ArmorCrackle)
    // - Signature, at 20: flames trailing from the hood's eyes and embers at each step (Firecaller), or three crystal
    //   shards circling the shoulders, arcs jumping between them (Stormcaller)
    // Made the first time they show, so a set that never reaches them costs nothing. The particles live in the world, so
    // they trail behind; their emitters follow the wearer's bones (VisEquipment's body), under an object of scale 1
    internal class ArmorLegendFx : MonoBehaviour
    {
        // The set's four pieces and staff (their prefab names' hashes), its name, which one it is, and the staff's lightning
        public int[] m_set = new int[0];
        public int m_staff;
        public string m_setName = "";
        public bool m_storm;
        public LightningLook m_look;
        // The robe's crystal, which the shards' arcs jump to now and then
        public string m_crystal = "x_Crystal";

        // The Firecaller's embers at the full level: where (a bone, an offset in the wearer's frame: right, up, forward, and
        // the box they rise from), how many a second, and how many more per meter walked
        private static readonly Spot[] EmberSpots =
        {
            new Spot("Spine2", new Vector3(0f, 0.12f, -0.02f), new Vector3(0.5f, 0.08f, 0.22f), 5f, 0f),
            new Spot("Head", new Vector3(0f, 0.12f, -0.08f), new Vector3(0.18f, 0.06f, 0.18f), 2f, 0f),
            new Spot("Hips", new Vector3(0f, -0.55f, -0.12f), new Vector3(0.5f, 0.15f, 0.3f), 5f, 2.5f),
        };
        // The Stormcaller's motes, around the body from the thighs to the head
        private static readonly Spot MoteSpot = new Spot("Spine1", new Vector3(0f, 0.05f, 0f), new Vector3(0.6f, 1.1f, 0.4f), 16f, 0f);
        // The hood's eyes in the head bone's space (measured from the hood's yEyes mesh and its bind pose, 2026-11-13: each
        // eye's four vertices, all on the head bone)
        private static readonly Vector3[] Eyes = { new Vector3(-0.000921f, 0.001777f, -0.000337f), new Vector3(-0.000921f, 0.001777f, 0.000423f) };
        private const float EyeFlamesPerSecond = 24f;
        // A burst of embers at each step
        private const int StepEmbers = 8;
        // The shards: how many, how far out from the body's axis and how high above the upper chest (Spine2), how fast
        // they go round (degrees a second), bob (meters, a second) and spin, and how big (their longest side, meters)
        private const int Shards = 3;
        private const float ShardRadius = 0.55f;
        private const float ShardHeight = 0.15f;
        private const float ShardOrbitSpeed = 40f;
        private const float ShardBob = 0.04f;
        private const float ShardBobPeriod = 2f;
        private const float ShardSpinSpeed = 90f;
        private const float ShardSize = 0.09f;
        // An arc between two shards (or one and the robe's crystal) every so often, this long
        private const float ArcMin = 0.35f;
        private const float ArcMax = 0.9f;
        private const float ArcDuration = 0.15f;
        private static readonly LightningSize s_arcSize = new LightningSize { m_strands = 1, m_strandSize = 0.15f, m_glowSize = 0.15f, m_branches = 0.3f };

        private const float LevelInterval = 0.25f;
        // Where Magic Supremacy's effects are (the bundle's)
        private const string EmbersEffect = "fx_firecallercaller_staff_blaster_hit_DO";
        private const string EmbersPath = "FireExplosionNapalmALT/FloatEmbers";
        private const string FlamesEffect = "fx_firecaller_staff_thrower_DO";
        private const string FlamesPath = "FlamethrowerBurstALT";
        private const string SparksModel = "StaffStormcallerShocker_DO";
        private const string SparksPath = "attach/equiped/Flash";
        private const string ShardParts = "staff_of_storms_parts";
        private const string ShardPart = "crystal_head";

        // Who shows the Firecaller's signature now, for their footsteps (FootStepEmbers)
        private static readonly Dictionary<Character, ArmorLegendFx> s_stepping = new Dictionary<Character, ArmorLegendFx>();
        private static GameObject s_stepTemplate;
        private static Mesh s_shardMesh;
        private static bool s_shardLooked;

        private struct Spot
        {
            public string m_bone;
            public Vector3 m_offset;
            public Vector3 m_box;
            public float m_rate;
            public float m_perMeter;

            public Spot(string bone, Vector3 offset, Vector3 box, float rate, float perMeter)
            {
                m_bone = bone;
                m_offset = offset;
                m_box = box;
                m_rate = rate;
                m_perMeter = perMeter;
            }
        }

        // A particle system following a bone: at an offset in the wearer's frame, or a point in the bone's space
        private class Emitter
        {
            public ParticleSystem m_particles;
            public Transform m_bone;
            public Vector3 m_offset;
            public bool m_inBone;
            public float m_rate;
            public float m_perMeter;
            public bool m_playing;
        }

        private class Arc
        {
            public GameObject m_root;
            public LightningArcs m_arcs;
            public float m_age;
        }

        private VisEquipment m_equipment;
        private Character m_wearer;
        private Transform m_root;
        private readonly Dictionary<string, Transform> m_bones = new Dictionary<string, Transform>();
        private readonly List<Emitter> m_particles = new List<Emitter>();
        private readonly List<Emitter> m_eyeFlames = new List<Emitter>();
        private Transform[] m_shards;
        private Renderer m_crystalRenderer;
        private readonly List<Arc> m_arcs = new List<Arc>();
        private bool m_built;
        private bool m_failed;
        private int m_level;
        private float m_levelTimer;
        private float m_amount = -1f;
        private bool m_signature;
        private float m_arcTimer;
        private float m_orbit;

        private void Awake()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                enabled = false;
            }
        }

        private void Start()
        {
            m_equipment = GetComponentInParent<VisEquipment>();
            m_wearer = GetComponentInParent<Character>();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == m_crystal)
                {
                    m_crystalRenderer = renderer;
                }
            }
        }

        private void OnDestroy()
        {
            if (m_wearer && s_stepping.TryGetValue(m_wearer, out ArmorLegendFx fx) && fx == this)
            {
                s_stepping.Remove(m_wearer);
            }
            foreach (Arc arc in m_arcs)
            {
                Destroy(arc.m_root);
            }
        }

        private void Update()
        {
            m_levelTimer -= Time.deltaTime;
            if (m_levelTimer <= 0f)
            {
                m_levelTimer = LevelInterval;
                m_level = ArmorLegend.Shows(m_equipment, m_set, m_staff) ? ArmorLegend.Level(m_wearer, m_setName) : 0;
                float amount = ArmorLegend.On(ArmorLegend.Ideas.Particles) ? ArmorLegend.Ramp(m_level, ArmorLegend.ParticlesFrom, 0.25f) : 0f;
                bool signature = ArmorLegend.On(ArmorLegend.Ideas.Signature) && m_level >= ArmorLegend.SignatureAt;
                if ((amount > 0f || signature) && !m_built && !m_failed)
                {
                    Build();
                }
                if (m_built)
                {
                    if (amount != m_amount)
                    {
                        m_amount = amount;
                        foreach (Emitter emitter in m_particles)
                        {
                            SetRate(emitter, amount);
                        }
                    }
                    if (signature != m_signature)
                    {
                        m_signature = signature;
                        Signature(signature);
                    }
                }
            }
            UpdateArcs(Time.deltaTime);
            // The Firecaller's showpieces held up for a look, without a channel (nhx_armorlook show), on this game only,
            // at the levels the channel shows them
            if (!m_storm && ArmorLegend.s_showpieces && m_wearer && m_wearer == Player.m_localPlayer)
            {
                if (ArmorLegend.On(ArmorLegend.Ideas.Sigil) && m_level >= ArmorLegend.SigilFrom)
                {
                    Staves.WrathOfMuspelheim.Effects.ChannelSigil.Hold(m_wearer, 3, 0f);
                }
                if (ArmorLegend.On(ArmorLegend.Ideas.Wings) && m_level >= ArmorLegend.WingsFrom)
                {
                    Staves.WrathOfMuspelheim.Effects.ChannelWings.Hold(m_wearer, true, 0f);
                }
            }
        }

        // After the animation has posed the bones
        private void LateUpdate()
        {
            if (!m_built || !m_wearer)
            {
                return;
            }
            Quaternion facing = m_wearer.transform.rotation;
            foreach (Emitter emitter in m_particles)
            {
                Follow(emitter, facing);
            }
            foreach (Emitter emitter in m_eyeFlames)
            {
                Follow(emitter, facing);
            }
            if (m_shards != null && m_signature && m_bones.TryGetValue("Spine2", out Transform chest))
            {
                m_orbit += ShardOrbitSpeed * Time.deltaTime;
                Vector3 center = chest.position + Vector3.up * ShardHeight;
                for (int i = 0; i < m_shards.Length; i++)
                {
                    float angle = m_orbit + 360f * i / m_shards.Length;
                    float bob = ShardBob * Mathf.Sin(2f * Mathf.PI * (Time.time / ShardBobPeriod + (float)i / m_shards.Length));
                    // Round the world's up, so that turning doesn't swing them
                    Vector3 outward = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                    m_shards[i].position = center + outward * ShardRadius + Vector3.up * bob;
                    // Upright (the staff's crystal runs along its mesh's z), turning about it
                    m_shards[i].rotation = Quaternion.AngleAxis(Time.time * ShardSpinSpeed + 120f * i, Vector3.up) * Quaternion.Euler(-90f, 0f, 0f);
                }
            }
        }

        private static void Follow(Emitter emitter, Quaternion facing)
        {
            if (!emitter.m_bone || !emitter.m_particles)
            {
                return;
            }
            Transform t = emitter.m_particles.transform;
            if (emitter.m_inBone)
            {
                t.SetPositionAndRotation(emitter.m_bone.TransformPoint(emitter.m_offset), facing);
            }
            else
            {
                t.SetPositionAndRotation(emitter.m_bone.position + facing * emitter.m_offset, facing);
            }
        }

        private static void SetRate(Emitter emitter, float amount)
        {
            if (!emitter.m_particles)
            {
                return;
            }
            ParticleSystem.EmissionModule emission = emitter.m_particles.emission;
            emission.rateOverTimeMultiplier = emitter.m_rate * amount;
            emission.rateOverDistanceMultiplier = emitter.m_perMeter * amount;
            bool play = amount > 0f;
            if (play && !emitter.m_playing)
            {
                emitter.m_particles.Play(true);
            }
            else if (!play && emitter.m_playing)
            {
                // What's out fades as it would
                emitter.m_particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            emitter.m_playing = play;
        }

        // The particles and the signature's parts, hidden until they show
        private void Build()
        {
            SkinnedMeshRenderer body = m_equipment ? m_equipment.m_bodyModel : null;
            if (!body || !m_wearer)
            {
                m_failed = true;
                return;
            }
            foreach (Transform bone in body.bones)
            {
                if (bone)
                {
                    m_bones[bone.name] = bone;
                }
            }
            GameObject root = new GameObject("nhx_legend");
            root.layer = gameObject.layer;
            root.transform.SetParent(transform, false);
            Vector3 scale = transform.lossyScale;
            root.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            m_root = root.transform;

            if (m_storm)
            {
                AddEmitter(Sparks(MoteSpot.m_box), MoteSpot, false);
                m_shards = MakeShards();
            }
            else
            {
                foreach (Spot spot in EmberSpots)
                {
                    AddEmitter(Embers(spot.m_box), spot, false);
                }
                foreach (Vector3 eye in Eyes)
                {
                    ParticleSystem flame = EyeFlame();
                    if (flame && m_bones.TryGetValue("Head", out Transform head))
                    {
                        Emitter emitter = new Emitter { m_particles = flame, m_bone = head, m_offset = eye, m_inBone = true, m_rate = EyeFlamesPerSecond };
                        SetRate(emitter, 0f);
                        m_eyeFlames.Add(emitter);
                    }
                }
            }
            m_built = true;
        }

        private void AddEmitter(ParticleSystem particles, Spot spot, bool inBone)
        {
            if (!particles || !m_bones.TryGetValue(spot.m_bone, out Transform bone))
            {
                if (particles)
                {
                    Destroy(particles.gameObject);
                }
                return;
            }
            Emitter emitter = new Emitter { m_particles = particles, m_bone = bone, m_offset = spot.m_offset, m_inBone = inBone, m_rate = spot.m_rate, m_perMeter = spot.m_perMeter };
            SetRate(emitter, 0f);
            m_particles.Add(emitter);
        }

        private void Signature(bool on)
        {
            foreach (Emitter emitter in m_eyeFlames)
            {
                SetRate(emitter, on ? 1f : 0f);
            }
            if (m_shards != null)
            {
                foreach (Transform shard in m_shards)
                {
                    shard.gameObject.SetActive(on);
                }
            }
            if (!m_storm && m_wearer)
            {
                if (on)
                {
                    s_stepping[m_wearer] = this;
                }
                else if (s_stepping.TryGetValue(m_wearer, out ArmorLegendFx fx) && fx == this)
                {
                    s_stepping.Remove(m_wearer);
                }
            }
            m_arcTimer = Random.Range(ArcMin, ArcMax);
        }

        // A copy of a Magic Supremacy effect's particle system (without its children) under the root, at scale 1
        private ParticleSystem Copy(string effect, string path)
        {
            return StaffEffects.CopyParticles(effect, path, m_root, "a legendary armor look is missing");
        }

        // Embers drifting up out of a box (the Wrath of Muspelheim's, from the blast's floating sparks)
        private ParticleSystem Embers(Vector3 box)
        {
            ParticleSystem particles = Copy(EmbersEffect, EmbersPath);
            if (!particles)
            {
                return null;
            }
            ParticleSystem.MainModule main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            main.gravityModifier = -0.06f;
            main.maxParticles = 60;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = box;
            return particles;
        }

        // Small sparks crawling about in a box, with short trails (Thundersurge's own sparks, made slow and jittery)
        private ParticleSystem Sparks(Vector3 box)
        {
            ParticleSystem particles = Copy(SparksModel, SparksPath);
            if (!particles)
            {
                return null;
            }
            ParticleSystem.MainModule main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.025f);
            main.gravityModifier = 0f;
            main.maxParticles = 60;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = box;
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strengthMultiplier = 0.8f;
            noise.frequency = 4f;
            noise.scrollSpeedMultiplier = 1f;
            ParticleSystem.TrailModule trails = particles.trails;
            if (trails.enabled)
            {
                trails.lifetimeMultiplier = 0.12f;
                trails.minVertexDistance = 0.01f;
            }
            return particles;
        }

        // A small flame at one of the hood's eyes, rising and trailing behind (the flamethrower's fire, without the rest)
        private ParticleSystem EyeFlame()
        {
            ParticleSystem particles = Copy(FlamesEffect, FlamesPath);
            if (!particles)
            {
                return null;
            }
            ParticleSystem.MainModule main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.gravityModifier = -0.12f;
            main.maxParticles = 40;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.006f;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = Vector3.one;
            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = false;
            ParticleSystem.LimitVelocityOverLifetimeModule limit = particles.limitVelocityOverLifetime;
            limit.enabled = false;
            ParticleSystem.ForceOverLifetimeModule force = particles.forceOverLifetime;
            force.enabled = false;
            ParticleSystem.InheritVelocityModule inherit = particles.inheritVelocity;
            inherit.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = false;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            return particles;
        }

        // The Staff of Storms' head crystal, three times, hidden until the signature shows
        private Transform[] MakeShards()
        {
            if (!s_shardLooked)
            {
                s_shardLooked = true;
                Dictionary<string, ModelParts.Part> parts = ModelParts.Load(ShardParts, "NhxStormShard_");
                s_shardMesh = parts != null && parts.TryGetValue(ShardPart, out ModelParts.Part part) ? part.m_mesh : null;
                if (!s_shardMesh)
                {
                    Plugin.Log.LogWarning($"No {ShardPart} in {ShardParts}: Stormcaller's Raiment has no shards.");
                }
            }
            Material material = m_crystalRenderer ? m_crystalRenderer.sharedMaterial : null;
            if (!s_shardMesh || !material)
            {
                return null;
            }
            Bounds bounds = s_shardMesh.bounds;
            float scale = ShardSize / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 1e-6f);
            Transform[] shards = new Transform[Shards];
            for (int i = 0; i < Shards; i++)
            {
                GameObject shard = new GameObject("nhx_shard" + i);
                shard.layer = m_root.gameObject.layer;
                shard.transform.SetParent(m_root, false);
                // Turning about its middle
                GameObject model = new GameObject("model");
                model.layer = shard.layer;
                model.transform.SetParent(shard.transform, false);
                model.transform.localScale = Vector3.one * scale;
                model.transform.localPosition = -bounds.center * scale;
                model.AddComponent<MeshFilter>().sharedMesh = s_shardMesh;
                MeshRenderer renderer = model.AddComponent<MeshRenderer>();
                Material[] materials = new Material[s_shardMesh.subMeshCount];
                for (int m = 0; m < materials.Length; m++)
                {
                    materials[m] = material;
                }
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                shard.SetActive(false);
                shards[i] = shard.transform;
            }
            return shards;
        }

        // The shards' arcs: one now and then between two of them, or one and the robe's crystal
        private void UpdateArcs(float dt)
        {
            for (int i = m_arcs.Count - 1; i >= 0; i--)
            {
                Arc arc = m_arcs[i];
                arc.m_age += dt;
                if (arc.m_age >= ArcDuration)
                {
                    Destroy(arc.m_root);
                    m_arcs.RemoveAt(i);
                    continue;
                }
                arc.m_arcs.Draw(arc.m_age);
            }
            if (!m_signature || m_shards == null || !m_look)
            {
                return;
            }
            m_arcTimer -= dt;
            if (m_arcTimer > 0f)
            {
                return;
            }
            m_arcTimer = Random.Range(ArcMin, ArcMax);
            int from = Random.Range(0, m_shards.Length);
            LightningAnchor start = new LightningAnchor { m_transform = m_shards[from] };
            // The crystal's renderer sits at the body's root, skinned onto the bones: its bounds' center is where it shows
            // (fixed for the arc's moment)
            LightningAnchor end = m_crystalRenderer && Random.value < 0.33f
                ? new LightningAnchor { m_point = m_crystalRenderer.bounds.center }
                : new LightningAnchor { m_transform = m_shards[(from + 1) % m_shards.Length] };
            GameObject root = new GameObject("nhx_shard_arc");
            root.transform.SetParent(m_root, false);
            LightningArcs arcs = new LightningArcs(root.transform, m_look, ArcDuration) { m_jag = 0.25f, m_maxJag = 0.1f, m_lights = false };
            arcs.Add(start, end, 0f, s_arcSize);
            m_arcs.Add(new Arc { m_root = root, m_arcs = arcs });
        }

        // Embers kicked up where a foot of a wearer showing the Firecaller's signature lands (FootStepEmbers)
        internal static void Step(Character character, Vector3 point)
        {
            if (!character || !s_stepping.TryGetValue(character, out ArmorLegendFx fx) || !fx || !fx.m_signature)
            {
                return;
            }
            if (!s_stepTemplate)
            {
                s_stepTemplate = fx.StepTemplate();
                if (!s_stepTemplate)
                {
                    return;
                }
            }
            GameObject embers = Instantiate(s_stepTemplate, point + Vector3.up * 0.03f, Quaternion.LookRotation(Vector3.up));
            embers.SetActive(true);
            Destroy(embers, 2.5f);
        }

        // A burst of embers rising from the ground, kept inactive in the staves' container
        private GameObject StepTemplate()
        {
            ParticleSystem particles = Copy(EmbersEffect, EmbersPath);
            if (!particles)
            {
                return null;
            }
            GameObject template = particles.gameObject;
            template.name = "NhxFirecallerStep";
            template.transform.SetParent(StaffEffects.Container, false);
            template.transform.localScale = Vector3.one;
            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.duration = 0.5f;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
            main.gravityModifier = -0.03f;
            main.maxParticles = StepEmbers;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)StepEmbers) });
            // A cone up (the object looks up)
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.08f;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = Vector3.one;
            return template;
        }
    }

    // Every game shows a character's footsteps through FootStep.DoEffect (its "Step" RPC, sent to everyone): the
    // Firecaller's signature adds its embers there
    [HarmonyPatch(typeof(FootStep), "DoEffect")]
    internal static class FootStepEmbers
    {
        private static void Postfix(FootStep __instance, Vector3 point)
        {
            ArmorLegendFx.Step(__instance.m_character, point);
        }
    }
}
