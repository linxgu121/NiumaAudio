using System;
using UnityEngine;

namespace NiumaAudio.Config
{
    /// <summary>
    /// 本地音频资源目录。第一版用 AddressKey 映射 AudioClip，后续可替换为 Addressables Resolver。
    /// </summary>
    [CreateAssetMenu(menuName = "NiumaAudio/Audio Catalog", fileName = "AudioCatalog")]
    public sealed class AudioCatalogDefinition : ScriptableObject
    {
        [Tooltip("本地音频条目列表。AddressKey 必须稳定唯一。")]
        public AudioCatalogEntry[] Entries = Array.Empty<AudioCatalogEntry>();
    }

    /// <summary>
    /// 本地音频资源条目。
    /// </summary>
    [Serializable]
    public sealed class AudioCatalogEntry
    {
        [Tooltip("资源地址 Key。业务和 CueDefinition 都通过这个 Key 查找声音。")]
        public string AddressKey;

        [Tooltip("第一版直接引用的 AudioClip。后续接 Addressables 后可以由异步加载器替代。")]
        public AudioClip Clip;
    }
}
