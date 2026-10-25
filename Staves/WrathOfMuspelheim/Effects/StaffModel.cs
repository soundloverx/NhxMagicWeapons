using System.Collections.Generic;
using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects
{
    // The staff's model made ready to change with its level (StaffLevelLook). The Incinerator's black swirls, twisting
    // around its head, its lower shaft and its butt (one mesh, which its animation turns), are rebuilt as six parts, so
    // that they can come in pairs: Tools\extract_staff_parts.py splits them into muspelheim_parts.bin, embedded here
    // (ModelParts). The staff's body keeps its mesh: its crystals and the shards in its collar only change their glow. It
    // also gets embers rising from its head while it's held, from the sparks of the blast's effect
    internal static class StaffModel
    {
        private const string Resource = "muspelheim_parts";
        private const string MeshPrefix = "NhxMuspelStaff_";
        private const string BodyPath = "attach/FireWeaps/MainStaff";
        private const string SwirlPath = "attach/FireWeaps/Swirl";
        // Turned on by the game while the staff is held (VisEquipment.EnableEquippedEffects); it's 30 times bigger than
        // "attach", like the model
        private const string EquippedPath = "attach/equiped";
        private const string LightPath = "attach/equiped/Point light";
        // The body's materials: the butt's crystal, the head's (the flame) and the shards in the collar (a scrolling glow)
        private const int ButtCrystalSlot = 0;
        private const int HeadCrystalSlot = 4;
        private const int ShardsSlot = 5;
        private static readonly string[] Swirls = { "swirl_head0", "swirl_head1", "swirl_shaft0", "swirl_shaft1", "swirl_butt0", "swirl_butt1" };
        // The embers: the blast effect's floating sparks
        private const string EmbersEffect = "fx_firecallercaller_staff_blaster_hit_DO";
        private const string EmbersPath = "FireExplosionNapalmALT/FloatEmbers";

        // On the staff's prefab: the swirls in parts, the embers, and the StaffLevelLook showing them by level. Returns it,
        // or null if something isn't as expected: the staff then keeps the Incinerator's look at every level
        internal static StaffLevelLook Build(GameObject staff, Vector3 embersCenter, Vector3 embersSize)
        {
            Transform bodyModel = staff.transform.Find(BodyPath);
            Transform swirlModel = staff.transform.Find(SwirlPath);
            MeshRenderer body = bodyModel ? bodyModel.GetComponent<MeshRenderer>() : null;
            MeshRenderer swirl = swirlModel ? swirlModel.GetComponent<MeshRenderer>() : null;
            Dictionary<string, ModelParts.Part> parts = ModelParts.Load(Resource, MeshPrefix);
            if (!body || body.sharedMaterials.Length <= ShardsSlot || !swirl || parts == null || !System.Array.TrueForAll(Swirls, parts.ContainsKey))
            {
                Plugin.Log.LogWarning("The Wrath of Muspelheim's model parts are missing: it looks the same at every level.");
                return null;
            }

            // The swirls' renderer draws nothing now; its object stays, turned by the animation, with the parts under it
            Material[] materials = swirl.sharedMaterials;
            swirl.enabled = false;
            List<Renderer> swirls = new List<Renderer>();
            foreach (string name in Swirls)
            {
                swirls.Add(ModelParts.Child(swirlModel, swirl, materials, name, parts[name]));
            }
            ModelParts.AddToLod(staff, swirl, swirls);

            StaffLevelLook look = staff.transform.Find("attach").gameObject.AddComponent<StaffLevelLook>();
            look.m_body = body;
            look.m_headSlot = HeadCrystalSlot;
            look.m_buttSlot = ButtCrystalSlot;
            look.m_shardsSlot = ShardsSlot;
            look.m_swirls = swirls.ToArray();
            look.m_spin = swirlModel.GetComponent<Animator>();
            Transform light = staff.transform.Find(LightPath);
            look.m_light = light ? light.GetComponent<Light>() : null;
            look.m_embers = CreateEmbers(staff.transform.Find(EquippedPath), embersCenter, embersSize);
            return look;
        }

        // Embers drifting up from the head crystal, in `size` around `center` (in "attach"), while the staff is held
        private static ParticleSystem CreateEmbers(Transform equipped, Vector3 center, Vector3 size)
        {
            GameObject effect = Plugin.Assets.LoadAsset<GameObject>(EmbersEffect);
            Transform source = effect ? effect.transform.Find(EmbersPath) : null;
            if (!equipped || !source)
            {
                Plugin.Log.LogWarning($"No embers for the Wrath of Muspelheim: {(equipped ? EmbersEffect + " has no " + EmbersPath : "its model has no " + EquippedPath)}.");
                return null;
            }
            GameObject embers = Object.Instantiate(source.gameObject, equipped, false);
            embers.name = "nhx_embers";
            // "equiped" is 30 times bigger than "attach", with no turn
            float scale = equipped.localScale.x;
            embers.transform.localPosition = center / scale;
            embers.transform.localRotation = Quaternion.identity;
            embers.transform.localScale = Vector3.one / scale;

            ParticleSystem particles = embers.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.duration = 1f;
            main.playOnAwake = true;
            main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            // Rising, slowly
            main.gravityModifier = -0.05f;
            main.maxParticles = 100;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 6f;
            emission.SetBursts(new ParticleSystem.Burst[0]);
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = size;
            ParticleSystem.CollisionModule collision = particles.collision;
            collision.enabled = false;
            return particles;
        }
    }
}
