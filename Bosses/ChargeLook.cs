using NhxMagicWeapons.Staves;
using NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Bosses
{
    // A player's charge in the twins' fight, seen on them by every game (Claude's proposal, 2026-11-14, taken by the user:
    // "Both show on you: sparks crawling for Static, embers rising for Scorch"): more of them with each stack. From the
    // player's ZDO (TwinsDuel: their element and stacks), on any player who comes near a twin (TwinsDuel.GiveLooks).
    // Static's sparks are Thundersurge's (as the Stormcaller's Raiment's legendary motes), Scorch's embers the Wrath's blast's
    // (FireParticles), put out one by one around the body, in the world, so they trail behind as the player moves
    internal class ChargeLook : MonoBehaviour
    {
        // Sparks or embers a second for each stack, from this much of the body's height up (its feet at 0), and how fast
        private const float PerStack = 7f;
        private const float BodyFrom = 0.1f;
        private const float BodyTo = 0.95f;
        private const float SparkSpeed = 0.35f;
        private const float EmberRise = 0.45f;
        private const float ReadInterval = 0.25f;

        private const string SparksModel = "StaffStormcallerShocker_DO";
        private const string SparksPath = "attach/equiped/Flash";

        private Player m_player;
        private ParticleSystem m_sparks;
        private ParticleSystem m_embers;
        private bool m_triedSparks;
        private bool m_triedEmbers;
        private Element m_element;
        private int m_stacks;
        private float m_readTimer;
        private float m_debt;

        private void Awake()
        {
            m_player = GetComponent<Player>();
            if (!m_player || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            m_readTimer -= Time.deltaTime;
            if (m_readTimer <= 0f)
            {
                m_readTimer = ReadInterval;
                ZDO zdo = m_player.m_nview && m_player.m_nview.IsValid() ? m_player.m_nview.GetZDO() : null;
                m_element = zdo != null ? (Element)zdo.GetInt(TwinsDuel.ElementKey) : Element.None;
                m_stacks = zdo != null && !m_player.IsDead() ? zdo.GetInt(TwinsDuel.StacksKey) : 0;
            }
            if (m_stacks <= 0 || m_element == Element.None)
            {
                m_debt = 0f;
                return;
            }
            ParticleSystem particles = m_element == Element.Storm ? Sparks() : Embers();
            if (!particles)
            {
                return;
            }
            m_debt += PerStack * m_stacks * Time.deltaTime;
            Collider body = m_player.GetCollider();
            Bounds bounds = body ? body.bounds : new Bounds(m_player.GetCenterPoint(), new Vector3(0.8f, 1.8f, 0.8f));
            while (m_debt >= 1f)
            {
                m_debt -= 1f;
                Vector3 point = new Vector3(
                    Random.Range(bounds.min.x, bounds.max.x),
                    Mathf.Lerp(bounds.min.y, bounds.max.y, Random.Range(BodyFrom, BodyTo)),
                    Random.Range(bounds.min.z, bounds.max.z));
                // Pulled in toward the body's axis, so they sit on it rather than in the air around it
                Vector3 axis = new Vector3(bounds.center.x, point.y, bounds.center.z);
                point = Vector3.Lerp(axis, point, 0.6f);
                Vector3 velocity = m_element == Element.Storm ? Random.insideUnitSphere * SparkSpeed : Vector3.up * EmberRise * Random.Range(0.6f, 1.2f);
                FireParticles.Emit(particles, point, velocity);
            }
        }

        private ParticleSystem Embers()
        {
            if (!m_embers && !m_triedEmbers)
            {
                m_triedEmbers = true;
                m_embers = FireParticles.Embers(transform, 0.8f, 1.6f, 120);
            }
            return m_embers;
        }

        // Thundersurge's sparks, put out one by one (as FireParticles does the fire): short-lived, wandering
        private ParticleSystem Sparks()
        {
            if (m_sparks || m_triedSparks)
            {
                return m_sparks;
            }
            m_triedSparks = true;
            ParticleSystem particles = StaffEffects.CopyParticles(SparksModel, SparksPath, transform, "the Static charge has no sparks");
            if (!particles)
            {
                return null;
            }
            ParticleSystem.MainModule main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
            main.gravityModifier = 0f;
            main.maxParticles = 120;
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strengthMultiplier = 1f;
            noise.frequency = 5f;
            noise.scrollSpeedMultiplier = 1.5f;
            ParticleSystem.TrailModule trails = particles.trails;
            if (trails.enabled)
            {
                trails.lifetimeMultiplier = 0.15f;
                trails.minVertexDistance = 0.01f;
            }
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = false;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            particles.Play(true);
            m_sparks = particles;
            return particles;
        }
    }
}
