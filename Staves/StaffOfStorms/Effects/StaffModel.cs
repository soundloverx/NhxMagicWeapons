using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace NhxMagicWeapons.Staves.StaffOfStorms.Effects
{
    // The staff's model rebuilt in parts, so that its prongs, spikes and crystals can come with its level (StaffLevelLook).
    // The game can't change Thundersurge's mesh (its data isn't kept readable), so Tools\extract_staff_parts.py splits it
    // from the bundle into staff_of_storms_parts.bin, embedded here: the body, 4 prongs (in the order they come), 2 spikes
    // (each with its pivot at its root, to grow from there) and 2 crystals, each with its submeshes' original materials
    internal static class StaffModel
    {
        private const string Resource = "staff_of_storms_parts";
        private const string ModelPath = "attach/StormShockerNew";
        private const string LightPath = "attach/Point light";
        // Turned on by the game while the staff is held (VisEquipment.EnableEquippedEffects)
        private const string SparksPath = "attach/equiped/Flash";
        // The original material of the inlays along the shaft (the scrolling glow)
        private const int InlayMaterial = 4;

        private class Part
        {
            public Vector3 m_pivot;
            public Mesh m_mesh;
            // The original materials of its submeshes
            public int[] m_materials;
        }

        // On the staff's prefab: its model as a body with the parts as children, and the StaffLevelLook showing them by
        // level. Returns it, or null if something isn't as expected: the staff then keeps Thundersurge's whole model
        internal static StaffLevelLook Build(GameObject staff)
        {
            Transform model = staff.transform.Find(ModelPath);
            MeshFilter filter = model ? model.GetComponent<MeshFilter>() : null;
            MeshRenderer renderer = model ? model.GetComponent<MeshRenderer>() : null;
            Dictionary<string, Part> parts = Load();
            string[] names = { "body", "prong0", "prong1", "prong2", "prong3", "spike0", "spike1", "crystal_head", "crystal_butt" };
            if (!filter || !renderer || parts == null || !System.Array.TrueForAll(names, parts.ContainsKey))
            {
                Plugin.Log.LogWarning("The Staff of Storms' model parts are missing: it looks the same at every level.");
                return null;
            }
            Material[] materials = renderer.sharedMaterials;

            Part body = parts["body"];
            filter.sharedMesh = body.m_mesh;
            renderer.sharedMaterials = Materials(materials, body);

            StaffLevelLook look = model.parent.gameObject.AddComponent<StaffLevelLook>();
            look.m_body = renderer;
            look.m_inlaySlot = System.Array.IndexOf(body.m_materials, InlayMaterial);
            look.m_prongs = new[]
            {
                Child(model, renderer, materials, "prong0", parts["prong0"]).gameObject,
                Child(model, renderer, materials, "prong1", parts["prong1"]).gameObject,
                Child(model, renderer, materials, "prong2", parts["prong2"]).gameObject,
                Child(model, renderer, materials, "prong3", parts["prong3"]).gameObject,
            };
            look.m_spikes = new[]
            {
                Child(model, renderer, materials, "spike0", parts["spike0"]).transform,
                Child(model, renderer, materials, "spike1", parts["spike1"]).transform,
            };
            look.m_crystals = new Renderer[]
            {
                Child(model, renderer, materials, "crystal_head", parts["crystal_head"]),
                Child(model, renderer, materials, "crystal_butt", parts["crystal_butt"]),
            };
            Transform light = staff.transform.Find(LightPath);
            look.m_light = light ? light.GetComponent<Light>() : null;
            Transform sparks = staff.transform.Find(SparksPath);
            look.m_sparks = sparks ? sparks.GetComponent<ParticleSystem>() : null;

            // A dropped staff's LODGroup hides its renderers at a distance: the parts too
            LODGroup lodGroup = staff.GetComponent<LODGroup>();
            if (lodGroup)
            {
                LOD[] lods = lodGroup.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                {
                    if (System.Array.IndexOf(lods[i].renderers, renderer) >= 0)
                    {
                        List<Renderer> renderers = new List<Renderer>(lods[i].renderers);
                        foreach (MeshRenderer part in model.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            if (part != renderer)
                            {
                                renderers.Add(part);
                            }
                        }
                        lods[i].renderers = renderers.ToArray();
                    }
                }
                lodGroup.SetLODs(lods);
            }
            return look;
        }

        private static MeshRenderer Child(Transform model, MeshRenderer original, Material[] materials, string name, Part part)
        {
            GameObject go = new GameObject(name);
            go.layer = model.gameObject.layer;
            go.transform.SetParent(model, false);
            go.transform.localPosition = part.m_pivot;
            go.AddComponent<MeshFilter>().sharedMesh = part.m_mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = Materials(materials, part);
            renderer.shadowCastingMode = original.shadowCastingMode;
            renderer.receiveShadows = original.receiveShadows;
            renderer.lightProbeUsage = original.lightProbeUsage;
            renderer.reflectionProbeUsage = original.reflectionProbeUsage;
            return renderer;
        }

        private static Material[] Materials(Material[] materials, Part part)
        {
            Material[] result = new Material[part.m_materials.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = materials[part.m_materials[i]];
            }
            return result;
        }

        // The parts in the embedded file (see Tools\extract_staff_parts.py for its layout), or null
        private static Dictionary<string, Part> Load()
        {
            using (Stream stream = typeof(StaffModel).Assembly.GetManifestResourceStream(Resource))
            {
                if (stream == null)
                {
                    return null;
                }
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "NHXP" || reader.ReadInt32() != 1)
                    {
                        return null;
                    }
                    Dictionary<string, Part> parts = new Dictionary<string, Part>();
                    int count = reader.ReadInt32();
                    for (int p = 0; p < count; p++)
                    {
                        string name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
                        Part part = new Part { m_pivot = ReadVector3(reader) };
                        int vertices = reader.ReadInt32();
                        Vector3[] positions = new Vector3[vertices];
                        Vector3[] normals = new Vector3[vertices];
                        Vector4[] tangents = new Vector4[vertices];
                        Vector2[] uvs = new Vector2[vertices];
                        for (int i = 0; i < vertices; i++)
                        {
                            positions[i] = ReadVector3(reader);
                        }
                        for (int i = 0; i < vertices; i++)
                        {
                            normals[i] = ReadVector3(reader);
                        }
                        for (int i = 0; i < vertices; i++)
                        {
                            tangents[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        }
                        for (int i = 0; i < vertices; i++)
                        {
                            uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                        }

                        Mesh mesh = new Mesh { name = "NhxStormStaff_" + name };
                        mesh.vertices = positions;
                        mesh.normals = normals;
                        mesh.tangents = tangents;
                        mesh.uv = uvs;
                        int submeshes = reader.ReadInt32();
                        mesh.subMeshCount = submeshes;
                        part.m_materials = new int[submeshes];
                        for (int s = 0; s < submeshes; s++)
                        {
                            part.m_materials[s] = reader.ReadInt32();
                            int[] indices = new int[reader.ReadInt32()];
                            for (int i = 0; i < indices.Length; i++)
                            {
                                indices[i] = reader.ReadUInt16();
                            }
                            mesh.SetTriangles(indices, s);
                        }
                        mesh.RecalculateBounds();
                        // Its data stays on the graphics card only, as the game's own meshes
                        mesh.UploadMeshData(true);
                        part.m_mesh = mesh;
                        parts[name] = part;
                    }
                    return parts;
                }
            }
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
    }
}
