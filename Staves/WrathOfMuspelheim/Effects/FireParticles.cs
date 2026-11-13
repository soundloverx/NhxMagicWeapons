using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects
{
    // Flames and embers put one by one where the code wants them (the Firecaller's Regalia's wings, sigil and comet):
    // copies of Magic Supremacy's particle systems that emit nothing by themselves, in world space, so what's out stays
    // behind as its wearer moves. The flames are the flamethrower's (as at the hood's eyes), the embers the blast's
    // floating sparks (as the armor's)
    internal static class FireParticles
    {
        private const string FlamesEffect = "fx_firecaller_staff_thrower_DO";
        private const string FlamesPath = "FlamethrowerBurstALT";
        private const string EmbersEffect = "fx_firecallercaller_staff_blaster_hit_DO";
        private const string EmbersPath = "FireExplosionNapalmALT/FloatEmbers";

        // Flames this big (meters) living this long (seconds), rising a little and shrinking as they burn out; `max` of
        // them at most
        internal static ParticleSystem Flames(Transform parent, float minSize, float maxSize, float minLife, float maxLife, int max)
        {
            ParticleSystem particles = StaffEffects.CopyParticles(FlamesEffect, FlamesPath, parent, "the Firecaller's Regalia's fire has no flames");
            if (!particles)
            {
                return null;
            }
            ParticleSystem.MainModule main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.gravityModifier = -0.1f;
            main.maxParticles = max;
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
            return Manual(particles);
        }

        // Embers drifting up, living this long; `max` of them at most
        internal static ParticleSystem Embers(Transform parent, float minLife, float maxLife, int max)
        {
            ParticleSystem particles = StaffEffects.CopyParticles(EmbersEffect, EmbersPath, parent, "the Firecaller's Regalia's fire has no embers");
            if (!particles)
            {
                return null;
            }
            ParticleSystem.MainModule main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            main.gravityModifier = -0.06f;
            main.maxParticles = max;
            return Manual(particles);
        }

        // Running, and emitting only what Emit asks for
        private static ParticleSystem Manual(ParticleSystem particles)
        {
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = false;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            particles.Play(true);
            return particles;
        }

        // One more, at `position`, moving at `velocity` (both in the world)
        internal static void Emit(ParticleSystem particles, Vector3 position, Vector3 velocity)
        {
            if (!particles)
            {
                return;
            }
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams { position = position, velocity = velocity };
            particles.Emit(emit, 1);
        }
    }
}
