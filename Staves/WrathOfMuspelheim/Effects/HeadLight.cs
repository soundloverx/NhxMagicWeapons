using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects
{
    // The light at the staff's head (on while it's held), in place of the Incinerator's LightFlicker. That one moved its
    // light around its spot by up to 0.1 on each axis, a few times a second, meant as a 10 cm wobble; but the light's
    // parent ("equiped") is 30 times bigger than the staff, so the light flew up to 3 m around the head. It also set the
    // intensity every frame from what it read in Awake, which undid the light's brightness by level. This one flickers
    // as it did (the game's formula), circles the head on a tight circle, turning with the swirls, and is as bright as
    // StaffLevelLook says
    internal class HeadLight : MonoBehaviour
    {
        // The circle's radius in the parent's units, and its turn in degrees a second at m_spin 1, about the staff's axis
        // (the parent's z)
        public float m_radius;
        public float m_turnSpeed;
        // LightFlicker's values on the Incinerator's light: ±10% at speed 10, fading in for 0.2 s when it comes on
        public float m_flicker = 0.1f;
        public float m_flickerSpeed = 10f;
        public float m_fadeIn = 0.2f;
        // Set by StaffLevelLook: the brightness (1 as made) and how fast the swirls turn (1 as made, 0 standing still)
        public float m_brightness = 1f;
        public float m_spin = 1f;

        private Light m_light;
        private float m_intensity;
        private Vector3 m_center;
        private float m_angle;
        private float m_flickerOffset;
        private float m_time;

        // Puts one on `light` (the Incinerator's head light), removing its LightFlicker. Null if there's no light there
        internal static HeadLight Replace(Transform light, float radius, float turnSpeed)
        {
            if (!light || !light.GetComponent<Light>())
            {
                return null;
            }
            HeadLight headLight = light.gameObject.AddComponent<HeadLight>();
            headLight.m_radius = radius;
            headLight.m_turnSpeed = turnSpeed;
            LightFlicker flicker = light.GetComponent<LightFlicker>();
            if (flicker)
            {
                // Its flicker kept as it was
                headLight.m_flicker = flicker.m_flickerIntensity;
                headLight.m_flickerSpeed = flicker.m_flickerSpeed;
                headLight.m_fadeIn = flicker.m_fadeInDuration;
                Object.DestroyImmediate(flicker);
            }
            return headLight;
        }

        private void Awake()
        {
            m_light = GetComponent<Light>();
            m_intensity = m_light.intensity;
            m_center = transform.localPosition;
            m_angle = Random.Range(0f, 360f);
            m_flickerOffset = Random.Range(0f, 10f);
        }

        private void OnEnable()
        {
            m_time = 0f;
            if (m_fadeIn > 0f)
            {
                m_light.intensity = 0f;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            m_time += dt;
            m_angle = Mathf.Repeat(m_angle + m_turnSpeed * m_spin * dt, 360f);
            float angle = m_angle * Mathf.Deg2Rad;
            transform.localPosition = m_center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * m_radius;

            float t = m_flickerOffset + Time.time * m_flickerSpeed;
            float flicker = 1f + Mathf.Sin(t) * Mathf.Sin(t * 0.56436f) * Mathf.Cos(t * 0.758348f) * m_flicker;
            float fade = m_fadeIn > 0f ? Mathf.Clamp01(m_time / m_fadeIn) : 1f;
            m_light.intensity = m_intensity * m_brightness * flicker * fade;
        }
    }
}
