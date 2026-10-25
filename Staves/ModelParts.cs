using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace NhxMagicWeapons.Staves
{
    // A staff's model in parts that come with its level. The game can't change Magic Supremacy's meshes (their data isn't
    // kept readable), so Tools\extract_staff_parts.py splits them from the bundle into a file embedded in the DLL: each
    // part with its pivot, its mesh and its submeshes' original materials
    internal static class ModelParts
    {
        internal class Part
        {
            public Vector3 m_pivot;
            public Mesh m_mesh;
            // The original materials of its submeshes
            public int[] m_materials;
        }

        // The parts in an embedded file (see Tools\extract_staff_parts.py for its layout), their meshes named
        // `meshPrefix` + the part's name; null if it's missing or not as expected
        internal static Dictionary<string, Part> Load(string resource, string meshPrefix)
        {
            using (Stream stream = typeof(ModelParts).Assembly.GetManifestResourceStream(resource))
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

                        Mesh mesh = new Mesh { name = meshPrefix + name };
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

        // A part as a child of `model`, at its pivot, drawn like `original` (shadows, probes) with the materials of its
        // submeshes taken from `materials` (the original renderer's)
        internal static MeshRenderer Child(Transform model, MeshRenderer original, Material[] materials, string name, Part part)
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

        internal static Material[] Materials(Material[] materials, Part part)
        {
            Material[] result = new Material[part.m_materials.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = materials[part.m_materials[i]];
            }
            return result;
        }

        // A dropped staff's LODGroup hides its renderers at a distance: renderers added to its model join the LOD that has
        // `original`, or they'd stay visible past it
        internal static void AddToLod(GameObject staff, Renderer original, IEnumerable<Renderer> added)
        {
            LODGroup lodGroup = staff.GetComponent<LODGroup>();
            if (!lodGroup)
            {
                return;
            }
            LOD[] lods = lodGroup.GetLODs();
            for (int i = 0; i < lods.Length; i++)
            {
                if (System.Array.IndexOf(lods[i].renderers, original) >= 0)
                {
                    List<Renderer> renderers = new List<Renderer>(lods[i].renderers);
                    renderers.AddRange(added);
                    lods[i].renderers = renderers.ToArray();
                }
            }
            lodGroup.SetLODs(lods);
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
    }
}
