using System;
using UnityEngine;

namespace NiumaAudio.Data
{
    /// <summary>
    /// 通用声音播放请求。CueId 和 AddressKey 至少填写一个。
    /// </summary>
    [Serializable]
    public sealed class AudioPlayRequest
    {
        public string CueId;
        public string AddressKey;
        public bool HasOverrideBus;
        public AudioBus OverrideBus = AudioBus.Sfx;
        public Vector3 Position;
        public bool HasPosition;
        public float VolumeScale = 1f;
        public float FadeSeconds;
        public string SourceModule;

        public AudioPlayRequest Clone()
        {
            return (AudioPlayRequest)MemberwiseClone();
        }
    }

    /// <summary>
    /// BGM 播放请求。BGM 使用独立双 AudioSource 交叉淡入淡出。
    /// </summary>
    [Serializable]
    public sealed class AudioBgmRequest
    {
        public string CueId;
        public string AddressKey;
        public float FadeSeconds = 1f;
        public bool RestartIfSame;
        public string SourceModule;

        public AudioBgmRequest Clone()
        {
            return (AudioBgmRequest)MemberwiseClone();
        }
    }

    /// <summary>
    /// 环境音播放请求。同一个 ChannelId 上的新环境音会替换旧环境音。
    /// </summary>
    [Serializable]
    public sealed class AudioAmbientRequest
    {
        public string ChannelId;
        public string CueId;
        public string AddressKey;
        public float FadeSeconds = 1f;
        public string SourceModule;

        public AudioAmbientRequest Clone()
        {
            return (AudioAmbientRequest)MemberwiseClone();
        }
    }
}
