using NhxMagicWeapons.Staves.StaffOfStorms;
using NhxMagicWeapons.Staves.StaffOfStorms.Effects;
using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Armor
{
    // Stormcaller's Raiment's crackle (the user's pick, 2026-11-10: Eye of the Storm "with the crackling armor visual
    // flavor"): with the whole set on and the Staff of Storms in hand, small arcs of lightning crackle from the robe's
    // crystal now and then, and fast while the wearer's storm is out (StormEye). On the robe's attach_skin, which
    // VisEquipment copies onto whoever wears it, on every game: it knows what they wear and hold (an armor stand too)
    internal class ArmorCrackle : MonoBehaviour
    {
        // The set's four pieces (their prefab names' hashes), all of which must be worn, and the staff that must be held
        public int[] m_set = new int[0];
        public int m_staff;
        public LightningLook m_look;
        // The crystal's renderer, under the robe
        public string m_crystal = "x_Crystal";
        // A burst every so often, idle or with a storm out; each 1-2 arcs (3 with a storm), this long, this far out
        public float m_idleMin = 0.5f;
        public float m_idleMax = 1.4f;
        public float m_stormMin = 0.12f;
        public float m_stormMax = 0.35f;
        public float m_duration = 0.12f;
        public float m_minReach = 0.18f;
        public float m_maxReach = 0.45f;

        // Thin and short: a fraction of the staff's bolt
        private static readonly LightningSize s_size = new LightningSize { m_strands = 1, m_strandSize = 0.15f, m_glowSize = 0.15f, m_branches = 0.5f };

        private class Burst
        {
            public GameObject m_root;
            public LightningArcs m_arcs;
            public float m_age;
        }

        private readonly List<Burst> m_bursts = new List<Burst>();
        private VisEquipment m_equipment;
        private Character m_wearer;
        private Renderer m_crystalRenderer;
        private float m_timer;

        private void Awake()
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
            if (!m_equipment || !m_crystalRenderer || !m_look)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = m_bursts.Count - 1; i >= 0; i--)
            {
                Burst burst = m_bursts[i];
                burst.m_age += dt;
                if (burst.m_age >= m_duration)
                {
                    Destroy(burst.m_root);
                    m_bursts.RemoveAt(i);
                    continue;
                }
                burst.m_arcs.Draw(burst.m_age);
            }

            if (!Ready())
            {
                return;
            }
            m_timer -= dt;
            if (m_timer > 0f)
            {
                return;
            }
            bool storm = StormEye.IsActive(m_wearer);
            m_timer = storm ? Random.Range(m_stormMin, m_stormMax) : Random.Range(m_idleMin, m_idleMax);
            Crackle(storm ? 3 : Random.Range(1, 3));
        }

        // The whole set worn and the staff in hand
        private bool Ready() => ArmorGlow.Wielding(m_equipment, m_set, m_staff);

        private void Crackle(int arcs)
        {
            Vector3 center = m_crystalRenderer.bounds.center;
            GameObject root = new GameObject("crackle");
            root.transform.SetParent(transform, false);
            // Very jagged for their length
            LightningArcs lightning = new LightningArcs(root.transform, m_look, m_duration) { m_jag = 0.3f, m_maxJag = 0.12f };
            for (int i = 0; i < arcs; i++)
            {
                Vector3 to = center + Random.onUnitSphere * Random.Range(m_minReach, m_maxReach);
                lightning.Add(new LightningAnchor { m_point = center }, new LightningAnchor { m_point = to }, Random.Range(0f, 0.04f), s_size);
            }
            m_bursts.Add(new Burst { m_root = root, m_arcs = lightning });
        }
    }
}
