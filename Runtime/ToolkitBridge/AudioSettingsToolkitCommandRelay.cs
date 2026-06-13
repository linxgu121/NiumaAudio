using NiumaAudio.Controller;
using NiumaAudio.Data;
using UnityEngine;

namespace NiumaAudio.ToolkitBridge
{
    /// <summary>
    /// 音频设置 Toolkit 面板命令中继。策划把它拖到 AudioSettingsToolkitBindingProvider 的事件上即可。
    /// </summary>
    public sealed class AudioSettingsToolkitCommandRelay : MonoBehaviour
    {
        [Tooltip("拖全局 AudioRoot 上的 NiumaAudioController。为空时自动查找。")]
        [SerializeField] private NiumaAudioController audioController;
        [SerializeField] private bool logWarnings = true;

        public void SetBusVolume(AudioBus bus, float volume)
        {
            if (EnsureController())
                audioController.SetVolume(bus, Mathf.Clamp01(volume));
        }

        public void SetBusMuted(AudioBus bus, bool muted)
        {
            if (EnsureController())
                audioController.SetMuted(bus, muted);
        }

        private bool EnsureController()
        {
            if (audioController != null)
                return true;
#if UNITY_2023_1_OR_NEWER
            audioController = FindFirstObjectByType<NiumaAudioController>();
#else
            audioController = FindObjectOfType<NiumaAudioController>();
#endif
            if (audioController != null)
                return true;

            if (logWarnings)
                Debug.LogWarning("[AudioSettingsToolkitCommandRelay] 未找到 NiumaAudioController，请拖 AudioRoot 上的控制器。", this);
            return false;
        }
    }
}
