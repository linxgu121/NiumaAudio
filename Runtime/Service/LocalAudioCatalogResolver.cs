using System;
using System.Collections.Generic;
using NiumaAudio.Config;
using UnityEngine;

namespace NiumaAudio.Service
{
    /// <summary>
    /// 第一版本地音频解析器。通过 AudioCatalogDefinition 把 AddressKey 同步解析为 AudioClip。
    /// </summary>
    public sealed class LocalAudioCatalogResolver : IAudioClipResolver
    {
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);

        public LocalAudioCatalogResolver(AudioCatalogDefinition catalog = null)
        {
            SetCatalog(catalog);
        }

        /// <summary>
        /// 热更新本地 Catalog。重复 AddressKey 时保留第一次出现的条目并输出警告。
        /// </summary>
        public void SetCatalog(AudioCatalogDefinition catalog)
        {
            _clips.Clear();
            if (catalog == null || catalog.Entries == null)
            {
                return;
            }

            for (var i = 0; i < catalog.Entries.Length; i++)
            {
                var entry = catalog.Entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.AddressKey) || entry.Clip == null)
                {
                    continue;
                }

                if (_clips.ContainsKey(entry.AddressKey))
                {
                    Debug.LogWarning($"[NiumaAudio] 检测到重复 AddressKey={entry.AddressKey}，后出现的 Catalog 条目已忽略。", catalog);
                    continue;
                }

                _clips.Add(entry.AddressKey, entry.Clip);
            }
        }

        public bool TryResolveClip(string addressKey, out AudioClip clip)
        {
            clip = null;
            return !string.IsNullOrWhiteSpace(addressKey) && _clips.TryGetValue(addressKey, out clip) && clip != null;
        }
    }
}
