using System;
using NiumaAudio.Data;
using UnityEngine;

namespace NiumaAudio.Config
{
    /// <summary>
    /// 声音 Cue 配置资产。业务模块推荐传 CueId，由音频服务解析到具体 AddressKey 和播放规则。
    /// </summary>
    [CreateAssetMenu(menuName = "NiumaAudio/Audio Cue Definition", fileName = "AudioCueDefinition")]
    public sealed class AudioCueDefinition : ScriptableObject
    {
        [Tooltip("声音稳定 ID。用于业务调用、调试和存档恢复。发布后不要随意修改。")]
        public string CueId;

        [Tooltip("显示名称。仅用于调试和编辑器查看，后续可替换为本地化 Key。")]
        public string DisplayName;

        [Tooltip("音频资源地址 Key。第一版由 AudioCatalog 映射到 AudioClip，后续可接 Addressables。")]
        public string AddressKey;

        [Tooltip("声音所属总线。不同总线可独立控制音量和静音。")]
        public AudioBus Bus = AudioBus.Sfx;

        [Tooltip("播放模式。OneShot 适合短音效，Loop 适合 BGM 和环境音。")]
        public AudioPlayMode PlayMode = AudioPlayMode.OneShot;

        [Tooltip("空间模式。TwoD 不受距离影响，ThreeD 会按 AudioSource 空间参数播放。")]
        public AudioSpatialMode SpatialMode = AudioSpatialMode.TwoD;

        [Range(0f, 1f)]
        [Tooltip("Cue 基础音量。最终音量还会乘请求音量、Bus 音量和 Master 音量。")]
        public float Volume = 1f;

        [Range(0.1f, 3f)]
        [Tooltip("基础音高。随机音高会在此基础上浮动。")]
        public float Pitch = 1f;

        [Range(0f, 1f)]
        [Tooltip("随机音高范围。例如 0.05 表示在 Pitch 上下浮动 0.05。")]
        public float RandomPitchRange;

        [Tooltip("播放优先级。池满时低优先级声音更容易被拒绝或回收。")]
        public int Priority = 128;

        [Tooltip("是否允许同一个 Cue 重叠播放。关闭后服务层可复用或拒绝重复播放。")]
        public bool AllowOverlap = true;

        private void OnValidate()
        {
            Volume = Mathf.Clamp01(Volume);
            Pitch = Mathf.Clamp(Pitch, 0.1f, 3f);
            RandomPitchRange = Mathf.Clamp01(RandomPitchRange);
            Priority = Math.Max(0, Priority);
        }
    }
}
