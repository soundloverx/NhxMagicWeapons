using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NhxMagicWeapons.Staves.StaffOfStorms.Effects
{
    // Draws a staff's lightning under an object: arcs between two points, each made of jagged bolts side by side, forks off
    // the first one, a glow along their middle and a light where they end, flashing for a moment from their own start
    // time. Each game draws its own random shapes. Used by LightningBolt (the bolt and its chain's jumps) and
    // StrikeLightning (lightning from the sky)
    internal class LightningArcs
    {
        // How far the bolts jag sideways: this share of their length, at most m_maxJag meters
        public float m_jag = 0.06f;
        public float m_maxJag = 2.5f;
        // A light where each arc ends
        public bool m_lights = true;

        // Midpoint displacement needs 2^n + 1 points
        private const int Points = 33;
        private const int BranchPoints = 9;
        // The bolts side by side share their shape's points this far apart (a quarter of the length), and jag on their own
        // in between
        private const int SharedPoints = 8;
        private const float FlickerInterval = 0.05f;
        private const float FullBrightTime = 0.08f;
        // At size 1. The glow fades out toward its edges, so it looks about half as wide
        private const float CoreWidth = 0.12f;
        private const float GlowWidth = 0.9f;
        private const float LightIntensity = 4f;
        private const float LightRange = 10f;

        private readonly Transform m_parent;
        private readonly LightningLook m_look;
        private readonly float m_duration;
        private readonly List<Arc> m_arcs = new List<Arc>();
        private readonly Vector3[] m_positions = new Vector3[Points];
        private readonly Vector3[] m_middle = new Vector3[Points];
        private readonly Vector3[] m_branchPositions = new Vector3[BranchPoints];

        // The lightning between two points: its ends, when it shows, and what it's drawn with
        private class Arc
        {
            public LightningAnchor m_from;
            public LightningAnchor m_to;
            public float m_start;
            // The bolts side by side, each with its shape (along a line of length 1 (z), with sideways offsets in meters
            // (x, y)) and its brightness now, and one glow along their middle
            public LineRenderer[] m_strands;
            public Vector3[][] m_shapes;
            public float[] m_flickers;
            public LineRenderer m_glow;
            // The first bolt's forks: as many as it can show, and how many it shows on average and this time
            public Branch[] m_branches;
            public float m_branchAverage;
            public int m_branchesShown;
            public Light m_light;
            public float m_lightIntensity;
            public bool m_shaped;
            public float m_flickerTimer;

            public void Show(bool show)
            {
                m_glow.enabled = show;
                if (m_light)
                {
                    m_light.enabled = show;
                }
                foreach (LineRenderer strand in m_strands)
                {
                    strand.enabled = show;
                }
                for (int i = 0; i < m_branches.Length; i++)
                {
                    m_branches[i].m_line.enabled = show && i < m_branchesShown;
                }
            }
        }

        // A fork off an arc's first bolt
        private class Branch
        {
            public LineRenderer m_line;
            public readonly Vector3[] m_shape = new Vector3[BranchPoints];
            // The bolt's point it leaves from, its direction in the arc's frame (x right, y up, z along), and its length as
            // a share of the arc's
            public int m_start;
            public Vector3 m_dir;
            public float m_length;
        }

        // Each arc shows for `duration` seconds
        public LightningArcs(Transform parent, LightningLook look, float duration)
        {
            m_parent = parent;
            m_look = look;
            m_duration = duration;
        }

        public int Count => m_arcs.Count;

        // Where an arc starts now
        public Vector3 From(int arc)
        {
            return m_arcs[arc].m_from.Position;
        }

        // An arc of `size`, showing from `start` seconds
        public void Add(LightningAnchor from, LightningAnchor to, float start, LightningSize size)
        {
            Arc arc = new Arc { m_from = from, m_to = to, m_start = start, m_branchAverage = size.m_branches };
            arc.m_glow = CreateLine("glow", Points, GlowWidth * size.m_glowSize, m_look.m_glowMaterial);
            arc.m_strands = new LineRenderer[size.m_strands];
            arc.m_shapes = new Vector3[size.m_strands][];
            arc.m_flickers = new float[size.m_strands];
            for (int i = 0; i < size.m_strands; i++)
            {
                arc.m_strands[i] = CreateLine("bolt", Points, CoreWidth * size.m_strandSize, m_look.m_coreMaterial);
                arc.m_shapes[i] = new Vector3[Points];
            }
            arc.m_branches = new Branch[Mathf.CeilToInt(size.m_branches)];
            for (int i = 0; i < arc.m_branches.Length; i++)
            {
                arc.m_branches[i] = new Branch { m_line = CreateLine("branch", BranchPoints, CoreWidth * 0.8f * size.m_strandSize, m_look.m_coreMaterial) };
            }

            if (m_lights)
            {
                GameObject light = new GameObject("light");
                light.transform.SetParent(m_parent, false);
                arc.m_light = light.AddComponent<Light>();
                arc.m_light.type = LightType.Point;
                arc.m_light.color = m_look.m_lightColor;
                arc.m_light.range = LightRange * size.m_glowSize;
                arc.m_light.shadows = LightShadows.None;
                arc.m_lightIntensity = LightIntensity * size.m_glowSize;
            }

            // Until its turn
            arc.Show(false);
            m_arcs.Add(arc);
        }

        // At `age` seconds since the arcs' time 0
        public void Draw(float age)
        {
            foreach (Arc arc in m_arcs)
            {
                Draw(arc, age - arc.m_start);
            }
        }

        private LineRenderer CreateLine(string name, int points, float width, Material material)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(m_parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points;
            line.sharedMaterial = material;
            line.widthMultiplier = width;
            // Thinner toward the end
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.5f));
            line.numCapVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private void Draw(Arc arc, float age)
        {
            if (age < 0f || age >= m_duration)
            {
                arc.Show(false);
                return;
            }

            Vector3 start = arc.m_from.Position;
            Vector3 delta = arc.m_to.Position - start;
            float length = delta.magnitude;
            Vector3 dir = length > 0.001f ? delta / length : m_parent.forward;
            Vector3 right = Vector3.Cross(dir, Vector3.up);
            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.Cross(dir, Vector3.forward);
            }
            right.Normalize();
            Vector3 up = Vector3.Cross(right, dir);

            // One shape for its whole life, as real lightning keeps its path: a new one 20 times a second added up to what
            // looked like several bolts. Only their brightness flickers, each bolt's on its own
            if (!arc.m_shaped)
            {
                Shape(arc, length);
                arc.m_shaped = true;
            }
            arc.m_flickerTimer -= Time.deltaTime;
            if (arc.m_flickerTimer <= 0f)
            {
                arc.m_flickerTimer = FlickerInterval;
                for (int i = 0; i < arc.m_flickers.Length; i++)
                {
                    arc.m_flickers[i] = Random.Range(0.6f, 1f);
                }
            }
            arc.Show(true);

            float fade = Mathf.Clamp01(age < FullBrightTime ? 1f : 1f - (age - FullBrightTime) / (m_duration - FullBrightTime));
            float brightest = 0f;
            for (int i = 0; i < Points; i++)
            {
                m_middle[i] = Vector3.zero;
            }
            for (int s = 0; s < arc.m_strands.Length; s++)
            {
                Vector3[] shape = arc.m_shapes[s];
                for (int i = 0; i < Points; i++)
                {
                    m_positions[i] = start + dir * (shape[i].z * length) + right * shape[i].x + up * shape[i].y;
                    m_middle[i] += m_positions[i] / arc.m_strands.Length;
                }
                arc.m_strands[s].SetPositions(m_positions);
                SetColor(arc.m_strands[s], m_look.m_coreColor, fade * arc.m_flickers[s]);
                brightest = Mathf.Max(brightest, arc.m_flickers[s]);
                if (s == 0)
                {
                    DrawBranches(arc, dir, right, up, length, fade * arc.m_flickers[0]);
                }
            }

            float alpha = fade * brightest;
            arc.m_glow.SetPositions(m_middle);
            SetColor(arc.m_glow, m_look.m_glowColor, alpha);
            if (arc.m_light)
            {
                arc.m_light.transform.position = start + delta;
                arc.m_light.intensity = arc.m_lightIntensity * alpha;
            }
        }

        // The first bolt's forks, from its points (in m_positions)
        private void DrawBranches(Arc arc, Vector3 dir, Vector3 right, Vector3 up, float length, float alpha)
        {
            for (int b = 0; b < arc.m_branchesShown; b++)
            {
                Branch branch = arc.m_branches[b];
                Vector3 branchStart = m_positions[branch.m_start];
                Vector3 branchDir = right * branch.m_dir.x + up * branch.m_dir.y + dir * branch.m_dir.z;
                float branchLength = branch.m_length * length;
                for (int i = 0; i < BranchPoints; i++)
                {
                    m_branchPositions[i] = branchStart + branchDir * (branch.m_shape[i].z * branchLength) + right * branch.m_shape[i].x + up * branch.m_shape[i].y;
                }
                branch.m_line.SetPositions(m_branchPositions);
                SetColor(branch.m_line, m_look.m_coreColor, alpha);
            }
        }

        // The bolts' jagged shapes and the first one's forks. The other bolts keep the first one's broad course (its points
        // SharedPoints apart) and jag on their own in between, so they twist around each other
        private void Shape(Arc arc, float length)
        {
            float amplitude = Mathf.Min(length * m_jag, m_maxJag);
            Vector3[] first = arc.m_shapes[0];
            first[0] = Vector3.zero;
            first[Points - 1] = new Vector3(0f, 0f, 1f);
            Displace(first, 0, Points - 1, amplitude);
            for (int s = 1; s < arc.m_shapes.Length; s++)
            {
                Vector3[] shape = arc.m_shapes[s];
                for (int i = 0; i < Points; i += SharedPoints)
                {
                    shape[i] = first[i];
                }
                // Halved at each level of the displacement: a quarter of it at this spacing
                for (int i = 0; i + SharedPoints < Points; i += SharedPoints)
                {
                    Displace(shape, i, i + SharedPoints, amplitude * SharedPoints / (Points - 1));
                }
            }

            // The average's whole part, and one more as often as its fraction says
            arc.m_branchesShown = Mathf.Min(arc.m_branches.Length, Mathf.FloorToInt(arc.m_branchAverage + Random.value));
            for (int i = 0; i < arc.m_branchesShown; i++)
            {
                Branch branch = arc.m_branches[i];
                branch.m_start = Random.Range(Points / 4, Points * 3 / 4);
                float angle = Random.Range(20f, 45f) * Mathf.Deg2Rad;
                float around = Random.Range(0f, 2f * Mathf.PI);
                branch.m_dir = new Vector3(Mathf.Sin(angle) * Mathf.Cos(around), Mathf.Sin(angle) * Mathf.Sin(around), Mathf.Cos(angle));
                branch.m_length = (1f - first[branch.m_start].z) * Random.Range(0.25f, 0.45f);
                branch.m_shape[0] = Vector3.zero;
                branch.m_shape[BranchPoints - 1] = new Vector3(0f, 0f, 1f);
                Displace(branch.m_shape, 0, BranchPoints - 1, Mathf.Min(length * branch.m_length * 0.1f, 1f));
            }
        }

        // Midpoint displacement: each segment's middle moves sideways at random, by half as much at each level
        private static void Displace(Vector3[] shape, int a, int b, float amplitude)
        {
            if (b - a < 2)
            {
                return;
            }
            int middle = (a + b) / 2;
            Vector2 offset = Random.insideUnitCircle * amplitude;
            shape[middle] = (shape[a] + shape[b]) * 0.5f + new Vector3(offset.x, offset.y, 0f);
            Displace(shape, a, middle, amplitude * 0.5f);
            Displace(shape, middle, b, amplitude * 0.5f);
        }

        private static void SetColor(LineRenderer line, Color color, float alpha)
        {
            color.a *= alpha;
            line.startColor = color;
            line.endColor = color;
        }
    }

    // Where an arc starts or ends: a transform (the staff's tip) or a character's center, followed as they move, or a
    // fixed point, also used when they aren't around on this game
    internal struct LightningAnchor
    {
        public Transform m_transform;
        public Character m_character;
        public Vector3 m_point;

        public Vector3 Position => m_transform ? m_transform.position : m_character ? m_character.GetCenterPoint() : m_point;
    }
}
