using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Bosses
{
    // A twin's ward against its own element's staff (TwinsRules: a staff cannot hurt its own caller), shown where the hit
    // that did nothing struck it (the fight's redesign, agreed with the user 2026-11-15: "A refused hit shows no number,
    // so the twin shows a ward flash when its own element hits it"): the twins' rune (ElementRune's rings, the
    // Firecaller's sigil's) standing in the air before it, facing out from it, in its element's color; it bursts open,
    // its rings turning, and fades within Duration, a light with it.
    // On each game, not networked: the twin's owner's game tells every game where (CallerBoss.Ward)
    internal class WardFlash : MonoBehaviour
    {
        // How wide (meters, from its middle), how far out from where the hit struck, and how long it shows
        private const float Radius = 1.5f;
        private const float Before = 0.25f;
        private const float Duration = 0.5f;
        // It opens from GrowFrom of its size over GrowTime; its rings turn (degrees a second, the middle first)
        private const float GrowFrom = 0.5f;
        private const float GrowTime = 0.1f;
        private static readonly float[] Spin = { 120f, -80f, 50f };
        // How bright at first (the shader draws twice its color), and its light
        private const float Flash = 2.5f;
        private const float LightIntensity = 2.5f;
        private const float LightRange = 8f;

        private static readonly int s_tint = Shader.PropertyToID("_TintColor");
        private static Mesh s_quad;

        private Transform m_twin;
        private Vector3 m_offset;
        private Transform[] m_layers;
        private MeshRenderer[] m_renderers;
        private Light m_light;
        private MaterialPropertyBlock m_block;
        private Color m_color;
        private float m_age;

        // The ward of `boss` at `point`, on this game
        internal static void Show(CallerBoss boss, Vector3 point)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !boss || !boss.m_humanoid)
            {
                return;
            }
            Material[] materials = ElementRune.Materials();
            if (materials == null)
            {
                return;
            }
            Vector3 outward = point - boss.m_humanoid.GetCenterPoint();
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : boss.transform.forward;
            GameObject go = new GameObject("NhxWardFlash");
            go.transform.SetPositionAndRotation(point + outward * Before, Quaternion.LookRotation(outward));
            go.AddComponent<WardFlash>().Build(boss, materials);
        }

        private void Build(CallerBoss boss, Material[] materials)
        {
            m_twin = boss.transform;
            m_offset = transform.position - m_twin.position;
            m_color = boss.Element == Element.Fire ? ElementRune.Fire : ElementRune.Storm;
            m_block = new MaterialPropertyBlock();
            m_layers = new Transform[materials.Length];
            m_renderers = new MeshRenderer[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                GameObject layer = new GameObject("layer" + i);
                layer.transform.SetParent(transform, false);
                layer.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                layer.AddComponent<MeshFilter>().sharedMesh = Quad();
                MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[i];
                renderer.sortingOrder = i;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                m_layers[i] = layer.transform;
                m_renderers[i] = renderer;
            }

            GameObject lightObject = new GameObject("light");
            lightObject.transform.SetParent(transform, false);
            m_light = lightObject.AddComponent<Light>();
            m_light.type = LightType.Point;
            m_light.color = m_color;
            m_light.range = LightRange;
            m_light.shadows = LightShadows.None;
            Draw(0f);
        }

        // A square across its facing, one unit from its middle to each side, the whole texture on it: made once (also
        // PhaseRunes')
        internal static Mesh Quad()
        {
            if (!s_quad)
            {
                s_quad = new Mesh { name = "NhxWardFlash" };
                s_quad.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) };
                s_quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                // The additive particle shader culls nothing: seen from behind the twin too
                s_quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                s_quad.RecalculateBounds();
            }
            return s_quad;
        }

        private void Update()
        {
            m_age += Time.deltaTime;
            if (m_age >= Duration)
            {
                Destroy(gameObject);
                return;
            }
            // With the twin as it moves
            if (m_twin)
            {
                transform.position = m_twin.position + m_offset;
            }
            Draw(Time.deltaTime);
        }

        private void Draw(float dt)
        {
            float fade = Mathf.Clamp01(1f - m_age / Duration);
            transform.localScale = Vector3.one * (Radius * Mathf.Lerp(GrowFrom, 1f, Mathf.Clamp01(m_age / GrowTime)));
            Color tint = m_color * Mathf.Lerp(1f, Flash, fade);
            tint.a = 0.5f * fade;
            m_block.SetColor(s_tint, tint);
            for (int i = 0; i < m_layers.Length; i++)
            {
                m_layers[i].Rotate(0f, 0f, Spin[i % Spin.Length] * dt, Space.Self);
                m_renderers[i].SetPropertyBlock(m_block);
            }
            m_light.intensity = LightIntensity * fade;
        }
    }
}
