using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace NhxMagicWeapons
{
    // Magic Supremacy's sounds play through the bundle's own copy of the game's audio mixer, which the game's volume
    // settings don't reach. Magic Supremacy moves them to the game's mixer group of the same name when a world loads,
    // and plays them 1.5 times louder; this does the same for the effects we take from it
    internal static class AudioRouting
    {
        private const float VolumeMultiplier = 1.5f;

        private static readonly List<GameObject> s_prefabs = new List<GameObject>();
        private static readonly HashSet<AudioSource> s_louder = new HashSet<AudioSource>();

        internal static void Add(GameObject prefab)
        {
            s_prefabs.Add(prefab);
        }

        // After Jotunn registers our prefabs with a new ZNetScene, on each world load
        internal static void Apply()
        {
            AudioMixer mixer = AudioMan.instance ? AudioMan.instance.m_masterMixer : null;
            if (!mixer)
            {
                Plugin.Log.LogWarning("The game's audio mixer isn't there: Magic Supremacy's sounds ignore the volume settings.");
                return;
            }

            foreach (GameObject prefab in s_prefabs)
            {
                foreach (AudioSource source in prefab.GetComponentsInChildren<AudioSource>(true))
                {
                    // Once per game start: the prefabs stay the same from one world to the next
                    if (s_louder.Add(source))
                    {
                        ZSFX sfx = source.GetComponent<ZSFX>();
                        if (sfx)
                        {
                            sfx.m_minVol *= VolumeMultiplier;
                            sfx.m_maxVol *= VolumeMultiplier;
                        }
                        else
                        {
                            source.volume *= VolumeMultiplier;
                        }
                    }

                    AudioMixerGroup group = source.outputAudioMixerGroup;
                    if (!group || group.audioMixer == mixer)
                    {
                        continue;
                    }
                    AudioMixerGroup match = FindGroup(mixer, group.name);
                    if (match)
                    {
                        source.outputAudioMixerGroup = match;
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"The game's audio mixer has no group {group.name} for {prefab.name}.");
                    }
                }
            }
        }

        private static AudioMixerGroup FindGroup(AudioMixer mixer, string name)
        {
            foreach (AudioMixerGroup group in mixer.FindMatchingGroups(name))
            {
                if (group.name == name)
                {
                    return group;
                }
            }
            return null;
        }
    }
}
