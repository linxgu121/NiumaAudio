using System.Collections.Generic;
using NiumaAudio.Data;
using UnityEngine;
using UnityEngine.Audio;

namespace NiumaAudio.Service
{
    /// <summary>
    /// 简单 AudioSource 池。只负责 Source 的创建、租借和回收，不做业务播放决策。
    /// </summary>
    internal sealed class AudioSourcePool
    {
        private readonly Transform _root;
        private readonly int _maxSize;
        private readonly List<AudioSource> _sources = new List<AudioSource>();

        public AudioSourcePool(Transform root, int initialSize, int maxSize)
        {
            _root = root;
            _maxSize = Mathf.Max(1, maxSize);
            var count = Mathf.Clamp(initialSize, 0, _maxSize);
            for (var i = 0; i < count; i++)
            {
                _sources.Add(CreateSource($"PooledAudioSource_{i + 1}"));
            }
        }

        public AudioSource Rent(AudioBus bus, AudioMixerGroup mixerGroup)
        {
            if (_root == null)
            {
                return null;
            }

            for (var i = 0; i < _sources.Count; i++)
            {
                var source = _sources[i];
                if (source != null && !source.gameObject.activeSelf)
                {
                    PrepareSource(source, bus, mixerGroup);
                    return source;
                }
            }

            if (_sources.Count >= _maxSize)
            {
                return null;
            }

            var created = CreateSource($"PooledAudioSource_{_sources.Count + 1}");
            _sources.Add(created);
            PrepareSource(created, bus, mixerGroup);
            return created;
        }

        public void Release(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            source.Stop();
            source.clip = null;
            source.loop = false;
            source.volume = 0f;
            source.pitch = 1f;
            source.spatialBlend = 0f;
            source.transform.localPosition = Vector3.zero;
            source.outputAudioMixerGroup = null;
            source.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            for (var i = 0; i < _sources.Count; i++)
            {
                var source = _sources[i];
                if (source == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Object.Destroy(source.gameObject);
                }
                else
                {
                    Object.DestroyImmediate(source.gameObject);
                }
            }

            _sources.Clear();
        }

        private AudioSource CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.SetActive(false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.volume = 0f;
            return source;
        }

        private static void PrepareSource(AudioSource source, AudioBus bus, AudioMixerGroup mixerGroup)
        {
            source.gameObject.SetActive(true);
            source.outputAudioMixerGroup = mixerGroup;
            source.playOnAwake = false;
            source.loop = false;
            source.pitch = 1f;
            source.volume = 0f;
            source.spatialBlend = 0f;
        }
    }
}
