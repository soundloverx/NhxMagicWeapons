using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Staves.StaffOfStorms.Effects
{
    // The Eye of the Storm's cloud (StormEye), on every game but a dedicated server: dark puffs (Magic Supremacy's smoke
    // material: a soft grey puff, alpha blended) gathering in a wide disc high over where the storm strikes, churning
    // slowly around its middle and trailing behind as it drifts, lit from within by flickers, and brightly at each strike.
    // An object of its own: when the storm ends, it stops gathering and fades as its puffs die
    internal class StormCloud : MonoBehaviour
    {
        // Each puff lives about this long; this many a second
        private const float Lifetime = 4f;
        private const float PuffsPerSecond = 16f;
        private static readonly Color DarkPuff = new Color(0.13f, 0.14f, 0.19f, 0.85f);
        private static readonly Color PalePuff = new Color(0.21f, 0.23f, 0.31f, 0.8f);
        // The material's tint (the shader draws 2 × it × the puff's color: 0.5 is neutral), and in a flash
        private static readonly Color Neutral = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        private static readonly Color Lit = new Color(1.7f, 1.8f, 2.4f, 0.5f);
        private static readonly Color LightColor = new Color(0.6f, 0.7f, 1f);
        private const float LightRange = 30f;
        private const float LightIntensity = 6f;
        // A flash dies down this fast; flickers come every so often, this bright
        private const float FlashTime = 0.15f;
        private const float FlickerMin = 0.3f;
        private const float FlickerMax = 1.2f;
        private const float Flicker = 0.35f;

        private static readonly int s_tint = Shader.PropertyToID("_TintColor");

        private ParticleSystem m_particles;
        private ParticleSystemRenderer m_renderer;
        private Light m_light;
        private MaterialPropertyBlock m_block;
        private float m_flash;
        private float m_flickerTimer;
        private bool m_ending;

        // At `position`, `radius` wide, drawn with `material`
        internal static StormCloud Create(Material material, Vector3 position, float radius)
        {
            GameObject go = new GameObject("NhxStormCloud");
            go.transform.position = position;
            ParticleSystem particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 5f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(Lifetime * 0.8f, Lifetime * 1.2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.8f, radius * 1.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.startColor = new ParticleSystem.MinMaxGradient(DarkPuff, PalePuff);
            // So that they trail behind as it drifts
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 150;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = PuffsPerSecond;

            // A flat disc
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.rotation = new Vector3(90f, 0f, 0f);

            // Churning around its middle, drawn slowly in
            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(0.25f);
            velocity.radial = new ParticleSystem.MinMaxCurve(-0.15f);

            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(fade);

            ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 1.2f));

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // Seen from right under it, a puff fills more than half the screen
            renderer.maxParticleSize = 3f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            GameObject lightObject = new GameObject("light");
            lightObject.transform.SetParent(go.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, -2f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LightColor;
            light.range = LightRange;
            light.intensity = 0f;
            light.shadows = LightShadows.None;

            StormCloud cloud = go.AddComponent<StormCloud>();
            cloud.m_particles = particles;
            cloud.m_renderer = renderer;
            cloud.m_light = light;
            cloud.m_block = new MaterialPropertyBlock();
            cloud.m_flickerTimer = Random.Range(FlickerMin, FlickerMax);
            particles.Play();
            return cloud;
        }

        internal void Follow(Vector3 position)
        {
            transform.position = position;
        }

        // Lit from within: 1 for a strike
        internal void Flash(float strength = 1f)
        {
            m_flash = Mathf.Max(m_flash, strength);
        }

        // It stops gathering, and is gone once its last puffs are
        internal void End()
        {
            if (m_ending)
            {
                return;
            }
            m_ending = true;
            m_particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(gameObject, Lifetime * 1.2f + 0.5f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (!m_ending)
            {
                m_flickerTimer -= dt;
                if (m_flickerTimer <= 0f)
                {
                    m_flickerTimer = Random.Range(FlickerMin, FlickerMax);
                    Flash(Random.Range(0.3f, 1f) * Flicker);
                }
            }
            m_flash *= Mathf.Exp(-dt / FlashTime);
            m_block.SetColor(s_tint, Color.Lerp(Neutral, Lit, m_flash));
            m_renderer.SetPropertyBlock(m_block);
            m_light.intensity = LightIntensity * m_flash;
        }
    }
}
