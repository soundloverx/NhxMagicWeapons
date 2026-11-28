using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace NhxMagicWeapons
{
    // The pose for holding the Springbloom Parasol's shield (the Lightcaller tank design, "The held pose"; the user,
    // 2026-11-28: "let's start with step 1: the pose"). Its plan: one moment of an attack held on the upper body while
    // the legs keep walking, since the attack plays once and ends. For now only for finding that moment, with
    // ParasolPoseCommand: an attack recorded as the local player plays it, then the torso and arms set back each frame to
    // how they were at the chosen moment, after the game has animated the character. On this game only, for the local
    // player; the others see the character as the game animates it.
    [HarmonyPatch]
    internal static class ParasolPose
    {
        // Nothing longer than this is recorded (a looping attack, held down, goes on for as long as it's held)
        private const float MaxRecording = 5f;
        private const string SaveFile = "NhxMagicWeapons.parasol_pose.txt";

        // The recording: the bones' names, and each frame's time from the attack's start and the bones' rotations
        private struct Frame
        {
            internal float m_time;
            internal Quaternion[] m_rotations;
        }

        private static readonly List<Frame> s_frames = new List<Frame>();
        private static string[] s_names;
        private static bool s_armed;
        private static bool s_recording;
        private static float s_recordTime;

        // The attack's animation clip, as the game names it
        internal static string Clip { get; private set; }
        internal static int Frames => s_frames.Count;
        internal static float Length => s_frames.Count > 0 ? s_frames[s_frames.Count - 1].m_time : 0f;
        internal static bool Armed => s_armed || s_recording;
        // The frame held, if any
        internal static bool Holding => s_held != null;
        internal static float HeldTime { get; private set; }

        private static Quaternion[] s_held;
        // The local player's bones for s_names, found again when the player or its body is a new one (after death or
        // another world)
        private static Player s_bonesOf;
        private static SkinnedMeshRenderer s_bodyOf;
        private static Transform[] s_bones;

        // The next attack the local player makes is recorded, from its start to its end; nothing is held meanwhile
        internal static void Arm()
        {
            s_held = null;
            s_armed = true;
            s_recording = false;
        }

        // The recorded frame nearest `time` seconds from the attack's start, held until Release; false if nothing is recorded
        internal static bool Hold(float time)
        {
            if (s_frames.Count == 0)
            {
                return false;
            }
            Frame nearest = s_frames[0];
            foreach (Frame frame in s_frames)
            {
                if (Mathf.Abs(frame.m_time - time) < Mathf.Abs(nearest.m_time - time))
                {
                    nearest = frame;
                }
            }
            s_held = nearest.m_rotations;
            HeldTime = nearest.m_time;
            return true;
        }

        internal static void Release()
        {
            s_held = null;
        }

        // The held frame, to a file in BepInEx's config folder: each bone's name and its rotation (x y z w), for the
        // shield's pose once the moment is picked. Its path, or null if nothing is held
        internal static string Save()
        {
            if (s_held == null)
            {
                return null;
            }
            StringBuilder text = new StringBuilder();
            text.AppendLine($"# NhxMagicWeapons: the upper body {Number(HeldTime)} s into {Clip ?? "the attack"} (of {Number(Length)} s), as ParasolPose holds it: bone x y z w (local rotation)");
            for (int i = 0; i < s_names.Length; i++)
            {
                Quaternion rotation = s_held[i];
                text.AppendLine(string.Join(" ", s_names[i], rotation.x.ToString("R", CultureInfo.InvariantCulture),
                    rotation.y.ToString("R", CultureInfo.InvariantCulture), rotation.z.ToString("R", CultureInfo.InvariantCulture),
                    rotation.w.ToString("R", CultureInfo.InvariantCulture)));
            }
            string path = Path.Combine(BepInEx.Paths.ConfigPath, SaveFile);
            File.WriteAllText(path, text.ToString());
            return path;
        }

        internal static string Number(float value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        // After the game has animated the character and turned its head (its look is IK, in the animation), every frame
        [HarmonyPostfix, HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomLateUpdate))]
        private static void AfterAnimation(CharacterAnimEvent __instance, float deltaTime)
        {
            Player player = Player.m_localPlayer;
            if (!player || __instance.m_character != player)
            {
                return;
            }
            if (s_armed || s_recording)
            {
                Record(player, deltaTime);
            }
            if (s_held != null && Bones(player))
            {
                for (int i = 0; i < s_bones.Length; i++)
                {
                    if (s_bones[i])
                    {
                        s_bones[i].localRotation = s_held[i];
                    }
                }
            }
        }

        // From the frame the game counts the player in an attack (its animation tagged attack, playing or next) to the
        // frame it doesn't
        private static void Record(Player player, float deltaTime)
        {
            bool attacking = player.InAttack();
            if (s_armed)
            {
                if (!attacking)
                {
                    return;
                }
                List<Transform> bones = UpperBody(player);
                if (bones == null)
                {
                    s_armed = false;
                    Tell(player, "Couldn't find the character's spine to record.");
                    return;
                }
                s_armed = false;
                s_recording = true;
                s_frames.Clear();
                s_recordTime = 0f;
                Clip = null;
                s_names = new string[bones.Count];
                for (int i = 0; i < bones.Count; i++)
                {
                    s_names[i] = bones[i].name;
                }
                s_bones = bones.ToArray();
                s_bonesOf = player;
                s_bodyOf = player.m_visEquipment.m_bodyModel;
            }
            else
            {
                s_recordTime += deltaTime;
            }
            if (!attacking || s_recordTime > MaxRecording || !Bones(player))
            {
                s_recording = false;
                Tell(player, $"Recorded {Number(Length)} s of {Clip ?? "the attack"} ({s_frames.Count} frames). nhx_parasolpose <seconds> holds a moment of it.");
                return;
            }
            Quaternion[] rotations = new Quaternion[s_bones.Length];
            for (int i = 0; i < s_bones.Length; i++)
            {
                rotations[i] = s_bones[i] ? s_bones[i].localRotation : Quaternion.identity;
            }
            s_frames.Add(new Frame { m_time = s_recordTime, m_rotations = rotations });
            Animator animator = player.m_animator;
            if (Clip == null && animator)
            {
                AnimatorClipInfo[] clips = animator.GetCurrentAnimatorStateInfo(0).tagHash == Humanoid.s_animatorTagAttack
                    ? animator.GetCurrentAnimatorClipInfo(0)
                    : animator.GetNextAnimatorStateInfo(0).tagHash == Humanoid.s_animatorTagAttack
                    ? animator.GetNextAnimatorClipInfo(0)
                    : new AnimatorClipInfo[0];
                if (clips.Length > 0 && clips[0].clip)
                {
                    Clip = clips[0].clip.name;
                }
            }
        }

        // The torso and arms down to the fingers: the body's bones from the spine up, without the neck and head, which keep
        // looking where the player looks. The legs and hips stay the game's
        private static List<Transform> UpperBody(Player player)
        {
            Animator animator = player.m_animator;
            SkinnedMeshRenderer body = player.m_visEquipment ? player.m_visEquipment.m_bodyModel : null;
            Transform spine = animator ? animator.GetBoneTransform(HumanBodyBones.Spine) : null;
            if (!body || !spine)
            {
                return null;
            }
            Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (!neck)
            {
                neck = animator.GetBoneTransform(HumanBodyBones.Head);
            }
            List<Transform> bones = new List<Transform>();
            foreach (Transform bone in body.bones)
            {
                if (bone && bone.IsChildOf(spine) && !(neck && bone.IsChildOf(neck)) && !bones.Contains(bone))
                {
                    bones.Add(bone);
                }
            }
            return bones;
        }

        // s_bones for this player's body, found by name if it's a new one; false if it has no spine
        private static bool Bones(Player player)
        {
            if (s_names == null)
            {
                return false;
            }
            SkinnedMeshRenderer body = player.m_visEquipment ? player.m_visEquipment.m_bodyModel : null;
            if (s_bonesOf == player && s_bodyOf == body && body && s_bones != null)
            {
                return true;
            }
            List<Transform> found = UpperBody(player);
            if (found == null)
            {
                return false;
            }
            s_bones = new Transform[s_names.Length];
            for (int i = 0; i < s_names.Length; i++)
            {
                string name = s_names[i];
                s_bones[i] = found.Find(bone => bone.name == name);
            }
            s_bonesOf = player;
            s_bodyOf = body;
            return true;
        }

        // In the console and on screen, since the console may be closed by the time the attack ends
        private static void Tell(Player player, string text)
        {
            Console.instance?.Print(text);
            player.Message(MessageHud.MessageType.TopLeft, text);
        }
    }
}
