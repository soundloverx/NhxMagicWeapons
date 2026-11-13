using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace NhxMagicWeapons.Staves.WrathOfMuspelheim.Effects
{
    // The channelled beam's sound (the Firecaller's Regalia's), on every game, at the staff's head: two Mixkit sounds the
    // user picked ("Long fire beam" and, half as loud under it, "Aggressive fire flame"), each cut by
    // Tools\make_channel_sound.py into an intro, a seamless loop and a tail, 6 dB louder than the files. The beam's shot
    // sounds as the channel begins (FireChannel, as without the set: a tap stays a shot). The intros play with it and run
    // on into the loops (scheduled to the sample); the loops pulse with the thread and swell at each hit; when the
    // channel ends, the tails come in over EndFade while the rest fades out. The user approved it as a preview: "Oh the
    // pulsing one sounds amazing!". On an object of its own, so that the tail outlives the channel
    internal class ChannelSound : MonoBehaviour
    {
        private const string Resource = "muspel_channel_sound";
        private const string Magic = "NHXS";
        private const int Version = 1;

        // The beam's loudness (the swells take it up to 1.4 times this, which must stay under 1), and the flame's, of it
        private const float BeamVolume = 0.65f;
        private const float FlameShare = 0.5f;
        // As in the preview: dipping 20% 1.3 times a second (with the thread's brightness), 40% louder at each hit, dying
        // down in a quarter of a second
        private const float PulseDepth = 0.2f;
        private const float PulseRate = 1.3f;
        private const float Swell = 0.4f;
        private const float SwellTime = 0.25f;
        private const float EndFade = 0.25f;
        // So that the intros and loops can be scheduled ahead
        private const double StartDelay = 0.05;

        private static Dictionary<string, AudioClip> s_clips;
        private static bool s_loaded;
        // AudioClip.SetData(float[], int): called by reflection, as the game's Unity also has a ReadOnlySpan overload,
        // which .NET Framework 4.8 doesn't define, so the compiler can't choose between them
        private static readonly MethodInfo s_setData = typeof(AudioClip).GetMethod(nameof(AudioClip.SetData), new[] { typeof(float[]), typeof(int) });

        private class Layer
        {
            public AudioSource m_intro;
            public AudioSource m_loop;
            public AudioSource m_tail;
            public float m_volume;
        }

        // The channel it follows; when it's gone, the sound ends
        private FireChannel m_channel;
        private readonly List<Layer> m_layers = new List<Layer>();
        private float m_envelope = 1f;
        private bool m_ending;
        private float m_endAge;
        private float m_endLength;

        // Its sound for `channel`, sounding like `template` (3D, its distances, the game's mixer group): null when the
        // clips aren't there
        internal static ChannelSound Create(FireChannel channel, AudioSource template)
        {
            Dictionary<string, AudioClip> clips = Clips();
            if (clips == null)
            {
                return null;
            }
            GameObject go = new GameObject("NhxMuspelChannelSound");
            go.transform.position = channel.transform.position;
            ChannelSound sound = go.AddComponent<ChannelSound>();
            sound.m_channel = channel;
            double start = AudioSettings.dspTime + StartDelay;
            sound.AddLayer(clips, "beam", BeamVolume, template, start);
            sound.AddLayer(clips, "flame", BeamVolume * FlameShare, template, start);
            return sound;
        }

        private void AddLayer(Dictionary<string, AudioClip> clips, string name, float volume, AudioSource template, double start)
        {
            if (!clips.TryGetValue(name + "_intro", out AudioClip intro) || !clips.TryGetValue(name + "_loop", out AudioClip loop)
                || !clips.TryGetValue(name + "_tail", out AudioClip tail))
            {
                return;
            }
            Layer layer = new Layer
            {
                m_intro = Source(intro, false, template),
                m_loop = Source(loop, true, template),
                m_tail = Source(tail, false, template),
                m_volume = volume,
            };
            layer.m_intro.volume = volume;
            layer.m_loop.volume = volume;
            layer.m_intro.PlayScheduled(start);
            layer.m_loop.PlayScheduled(start + (double)intro.samples / intro.frequency);
            m_endLength = Mathf.Max(m_endLength, tail.length);
            m_layers.Add(layer);
        }

        private AudioSource Source(AudioClip clip, bool loop, AudioSource template)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            if (template)
            {
                source.outputAudioMixerGroup = template.outputAudioMixerGroup;
                source.spatialBlend = template.spatialBlend;
                source.rolloffMode = template.rolloffMode;
                source.minDistance = template.minDistance;
                source.maxDistance = template.maxDistance;
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
                source.spread = template.spread;
                source.priority = template.priority;
            }
            else
            {
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 5f;
                source.maxDistance = 50f;
            }
            return source;
        }

        // Each frame, from the channel: the staff's head, and the thread's age and time since its last hit
        internal void Follow(Vector3 position, float age, float sinceHit)
        {
            transform.position = position;
            float pulse = 1f - PulseDepth * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * PulseRate * age));
            float swell = sinceHit < 10f * SwellTime ? Swell * Mathf.Exp(-sinceHit / SwellTime) : 0f;
            m_envelope = pulse + swell;
        }

        // The channel's attack is over, though its object stays a moment more for its last tendrils: the sound ends as
        // when the channel is gone
        internal void End()
        {
            m_channel = null;
        }

        private void Update()
        {
            if (!m_ending && !m_channel)
            {
                m_ending = true;
                foreach (Layer layer in m_layers)
                {
                    layer.m_tail.volume = 0f;
                    layer.m_tail.Play();
                }
            }
            if (!m_ending)
            {
                foreach (Layer layer in m_layers)
                {
                    layer.m_intro.volume = layer.m_volume * m_envelope;
                    layer.m_loop.volume = layer.m_volume * m_envelope;
                }
                return;
            }

            m_endAge += Time.deltaTime;
            if (m_endAge >= m_endLength)
            {
                Destroy(gameObject);
                return;
            }
            float k = Mathf.Clamp01(m_endAge / EndFade);
            float fadeOut = Mathf.Cos(k * Mathf.PI / 2f);
            float fadeIn = Mathf.Sin(k * Mathf.PI / 2f);
            foreach (Layer layer in m_layers)
            {
                layer.m_intro.volume = layer.m_volume * m_envelope * fadeOut;
                layer.m_loop.volume = layer.m_volume * m_envelope * fadeOut;
                layer.m_tail.volume = layer.m_volume * fadeIn;
            }
        }

        // The clips, made once from the DLL's file; null if it's missing or unreadable
        private static Dictionary<string, AudioClip> Clips()
        {
            if (s_loaded)
            {
                return s_clips;
            }
            s_loaded = true;
            Stream stream = typeof(Plugin).Assembly.GetManifestResourceStream(Resource);
            if (s_setData == null)
            {
                Plugin.Log.LogWarning("The game's AudioClip has no SetData(float[], int): the channelled beam is silent.");
                return null;
            }
            if (stream == null)
            {
                Plugin.Log.LogWarning($"{Resource} isn't in the DLL: the channelled beam is silent.");
                return null;
            }
            using (BinaryReader reader = new BinaryReader(new DeflateStream(stream, CompressionMode.Decompress)))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(Magic.Length)) != Magic || reader.ReadUInt16() != Version)
                {
                    Plugin.Log.LogWarning($"{Resource} isn't a file of Tools\\make_channel_sound.py's: the channelled beam is silent.");
                    return null;
                }
                int rate = (int)reader.ReadUInt32();
                int count = reader.ReadUInt16();
                s_clips = new Dictionary<string, AudioClip>();
                for (int i = 0; i < count; i++)
                {
                    string name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));
                    int samples = (int)reader.ReadUInt32();
                    byte[] data = reader.ReadBytes(samples * 2);
                    float[] floats = new float[samples];
                    for (int s = 0; s < samples; s++)
                    {
                        floats[s] = (short)(data[2 * s] | (data[2 * s + 1] << 8)) / 32768f;
                    }
                    AudioClip clip = AudioClip.Create("nhx_muspel_channel_" + name, samples, 1, rate, false);
                    s_setData.Invoke(clip, new object[] { floats, 0 });
                    s_clips[name] = clip;
                }
            }
            return s_clips;
        }
    }
}
