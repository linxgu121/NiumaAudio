using System;
using NiumaAudio.Data;
using UnityEngine;
using UnityEngine.Audio;

namespace NiumaAudio.Config
{
    /// <summary>
    /// AudioBus 到 AudioMixerGroup 的绑定配置。未绑定时服务层使用 AudioSource.volume 兜底。
    /// </summary>
    [Serializable]
    public sealed class AudioMixerGroupConfig
    {
        [Tooltip("要绑定的音频总线。")]
        public AudioBus Bus;

        [Tooltip("该总线输出到的 AudioMixerGroup。可以为空，表示不使用 Mixer。")]
        public AudioMixerGroup MixerGroup;
    }
}
