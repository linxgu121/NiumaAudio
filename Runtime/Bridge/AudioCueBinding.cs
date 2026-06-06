using System;
using NiumaAudio.Data;
using UnityEngine;

namespace NiumaAudio.Bridge
{
    /// <summary>
    /// 桥接层通用 Cue 配置。
    /// 策划优先填写 CueId；AddressKey 只用于临时测试或绕过 Cue 配置的特殊场景。
    /// </summary>
    [Serializable]
    public sealed class AudioCueBinding
    {
        [Tooltip("音频 CueId。填写 AudioCueDefinition.CueId，例如 ui_click、dialogue_choice、player_jump。")]
        public string CueId;

        [Tooltip("直接资源 Key。对应 AudioCatalogDefinition 中的 AddressKey；一般留空，优先使用 CueId。")]
        public string AddressKey;

        [Tooltip("是否强制覆盖 AudioCueDefinition 中配置的 Bus。普通按钮音效通常不勾选。")]
        public bool OverrideBus;

        [Tooltip("覆盖使用的音频总线。只有勾选 OverrideBus 时生效。")]
        public AudioBus Bus = AudioBus.Sfx;

        [Min(0f)]
        [Tooltip("播放音量倍率。1 表示使用 Cue 原始音量；0 表示静音播放。")]
        public float VolumeScale = 1f;

        [Min(0f)]
        [Tooltip("淡入淡出时间。普通 SFX 通常填 0；BGM 和环境音可填 0.5 到 2 秒。")]
        public float FadeSeconds;

        [Tooltip("来源模块名。用于调试日志，例如 NiumaUI、NiumaGal、NiumaTPC。")]
        public string SourceModule = "NiumaAudioBridge";

        public bool HasPlayableKey =>
            !string.IsNullOrWhiteSpace(CueId) || !string.IsNullOrWhiteSpace(AddressKey);

        public AudioCueBinding Clone()
        {
            return (AudioCueBinding)MemberwiseClone();
        }

        public AudioPlayRequest ToPlayRequest(string fallbackSourceModule = null)
        {
            return ToPlayRequest(Vector3.zero, false, fallbackSourceModule);
        }

        public AudioPlayRequest ToPlayRequest(Vector3 position, bool hasPosition, string fallbackSourceModule = null)
        {
            return new AudioPlayRequest
            {
                CueId = CueId,
                AddressKey = AddressKey,
                HasOverrideBus = OverrideBus,
                OverrideBus = Bus,
                Position = position,
                HasPosition = hasPosition,
                VolumeScale = Mathf.Max(0f, VolumeScale),
                FadeSeconds = Mathf.Max(0f, FadeSeconds),
                SourceModule = ResolveSourceModule(fallbackSourceModule)
            };
        }

        public AudioBgmRequest ToBgmRequest(bool restartIfSame, string fallbackSourceModule = null)
        {
            return new AudioBgmRequest
            {
                CueId = CueId,
                AddressKey = AddressKey,
                FadeSeconds = Mathf.Max(0f, FadeSeconds),
                RestartIfSame = restartIfSame,
                SourceModule = ResolveSourceModule(fallbackSourceModule)
            };
        }

        public AudioAmbientRequest ToAmbientRequest(string channelId, string fallbackSourceModule = null)
        {
            return new AudioAmbientRequest
            {
                ChannelId = channelId,
                CueId = CueId,
                AddressKey = AddressKey,
                FadeSeconds = Mathf.Max(0f, FadeSeconds),
                SourceModule = ResolveSourceModule(fallbackSourceModule)
            };
        }

        private string ResolveSourceModule(string fallbackSourceModule)
        {
            if (!string.IsNullOrWhiteSpace(SourceModule))
            {
                return SourceModule;
            }

            return !string.IsNullOrWhiteSpace(fallbackSourceModule)
                ? fallbackSourceModule
                : "NiumaAudioBridge";
        }
    }
}
