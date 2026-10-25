using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Staves.StaffOfStorms.Effects
{
    // The staff's model rebuilt in parts, so that its prongs, spikes and crystals can come with its level (StaffLevelLook).
    // Tools\extract_staff_parts.py splits Thundersurge's mesh into staff_of_storms_parts.bin, embedded here (ModelParts):
    // the body, 4 prongs (in the order they come), 2 spikes (each with its pivot at its root, to grow from there) and 2
    // crystals
    internal static class StaffModel
    {
        private const string Resource = "staff_of_storms_parts";
        private const string MeshPrefix = "NhxStormStaff_";
        private const string ModelPath = "attach/StormShockerNew";
        private const string LightPath = "attach/Point light";
        // Turned on by the game while the staff is held (VisEquipment.EnableEquippedEffects)
        private const string SparksPath = "attach/equiped/Flash";
        // The original material of the inlays along the shaft (the scrolling glow)
        private const int InlayMaterial = 4;

        // On the staff's prefab: its model as a body with the parts as children, and the StaffLevelLook showing them by
        // level. Returns it, or null if something isn't as expected: the staff then keeps Thundersurge's whole model
        internal static StaffLevelLook Build(GameObject staff)
        {
            Transform model = staff.transform.Find(ModelPath);
            MeshFilter filter = model ? model.GetComponent<MeshFilter>() : null;
            MeshRenderer renderer = model ? model.GetComponent<MeshRenderer>() : null;
            Dictionary<string, ModelParts.Part> parts = ModelParts.Load(Resource, MeshPrefix);
            string[] names = { "body", "prong0", "prong1", "prong2", "prong3", "spike0", "spike1", "crystal_head", "crystal_butt" };
            if (!filter || !renderer || parts == null || !System.Array.TrueForAll(names, parts.ContainsKey))
            {
                Plugin.Log.LogWarning("The Staff of Storms' model parts are missing: it looks the same at every level.");
                return null;
            }
            Material[] materials = renderer.sharedMaterials;

            ModelParts.Part body = parts["body"];
            filter.sharedMesh = body.m_mesh;
            renderer.sharedMaterials = ModelParts.Materials(materials, body);

            MeshRenderer Child(string name) => ModelParts.Child(model, renderer, materials, name, parts[name]);
            StaffLevelLook look = model.parent.gameObject.AddComponent<StaffLevelLook>();
            look.m_body = renderer;
            look.m_inlaySlot = System.Array.IndexOf(body.m_materials, InlayMaterial);
            look.m_prongs = new[] { Child("prong0").gameObject, Child("prong1").gameObject, Child("prong2").gameObject, Child("prong3").gameObject };
            look.m_spikes = new[] { Child("spike0").transform, Child("spike1").transform };
            look.m_crystals = new Renderer[] { Child("crystal_head"), Child("crystal_butt") };
            Transform light = staff.transform.Find(LightPath);
            look.m_light = light ? light.GetComponent<Light>() : null;
            Transform sparks = staff.transform.Find(SparksPath);
            look.m_sparks = sparks ? sparks.GetComponent<ParticleSystem>() : null;

            List<Renderer> added = new List<Renderer>();
            foreach (MeshRenderer part in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (part != renderer)
                {
                    added.Add(part);
                }
            }
            ModelParts.AddToLod(staff, renderer, added);
            return look;
        }
    }
}
