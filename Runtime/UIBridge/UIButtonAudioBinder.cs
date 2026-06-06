using NiumaAudio.Bridge;
using NiumaAudio.Controller;
using NiumaAudio.Data;
using NiumaAudio.Service;
using NiumaCore.Module;
using UnityEngine;
using UnityEngine.UI;

namespace NiumaAudio.UIBridge
{
    /// <summary>
    /// Button 点击音效绑定器。
    /// 把该脚本挂在 Button 同一个物体上，Click Cue.CueId 填 AudioCueDefinition.CueId。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class UIButtonAudioBinder : MonoBehaviour
    {
        [Header("按钮")]
        [Tooltip("要监听点击的 Button。通常留空，脚本会自动使用当前物体上的 Button。")]
        [SerializeField] private Button targetButton;

        [Tooltip("按钮不可交互时是否不播放点击音效。推荐开启，避免禁用按钮仍发声。")]
        [SerializeField] private bool playWhenInteractableOnly = true;

        [Header("音频服务")]
        [Tooltip("拖入 AudioRoot 上的 NiumaAudioController。为空且启用自动查找时，会在场景中查找 NiumaAudioController。")]
        [SerializeField] private NiumaAudioController audioController;

        [Tooltip("Audio Controller 为空时，是否自动查找场景中的 NiumaAudioController。正式场景推荐手动绑定。")]
        [SerializeField] private bool autoFindAudioController = true;

        [Tooltip("播放失败或 CueId 缺失时是否输出警告日志。")]
        [SerializeField] private bool logWarnings = true;

        [Header("点击音效")]
        [Tooltip("按钮点击音效。CueId 填 AudioCueDefinition.CueId，例如 ui_click。普通按钮建议在 CueDefinition 中配置 Bus；只有直接填 AddressKey 或特殊需求时才勾选 OverrideBus。")]
        [SerializeField] private AudioCueBinding clickCue = new AudioCueBinding
        {
            SourceModule = "NiumaUI",
            OverrideBus = false,
            Bus = AudioBus.UI
        };

        private IAudioCommand _runtimeCommand;
        private GameContext _runtimeContext;
        private bool _isBound;

        public AudioOperationResult LastResult { get; private set; }

        public void SetAudioCommand(IAudioCommand command)
        {
            _runtimeCommand = command;
        }

        public void SetGameContext(GameContext context)
        {
            _runtimeContext = context;
        }

        public void SetAudioController(NiumaAudioController controller)
        {
            audioController = controller;
        }

        private void OnEnable()
        {
            ResolveButton();
            if (targetButton == null || _isBound)
            {
                return;
            }

            targetButton.onClick.AddListener(HandleClick);
            _isBound = true;
        }

        private void OnDisable()
        {
            if (targetButton != null && _isBound)
            {
                targetButton.onClick.RemoveListener(HandleClick);
            }

            _isBound = false;
        }

        private void OnValidate()
        {
            ResolveButton();
            if (clickCue != null && string.IsNullOrWhiteSpace(clickCue.SourceModule))
            {
                clickCue.SourceModule = "NiumaUI";
            }
        }

        /// <summary>
        /// UnityEvent 调用入口：手动播放点击音效。
        /// </summary>
        public void PlayClickCue()
        {
            HandleClick();
        }

        private void HandleClick()
        {
            if (playWhenInteractableOnly && targetButton != null && !targetButton.interactable)
            {
                return;
            }

            if (clickCue == null || !clickCue.HasPlayableKey)
            {
                LastResult = AudioOperationResult.Failed(
                    AudioFailureReason.InvalidRequest,
                    "按钮点击音效未填写 CueId 或 AddressKey。");
                WarnFailure(LastResult);
                return;
            }

            if (!AudioBridgeResolver.TryResolveCommand(
                    _runtimeCommand,
                    _runtimeContext,
                    audioController,
                    autoFindAudioController,
                    out var command,
                    out var resolvedController))
            {
                LastResult = AudioOperationResult.Failed(
                    AudioFailureReason.ServiceNotReady,
                    "未找到可用的 NiumaAudioController 或 IAudioCommand。");
                WarnFailure(LastResult);
                return;
            }

            if (resolvedController != null)
            {
                audioController = resolvedController;
            }

            LastResult = command.PlayCue(clickCue.ToPlayRequest("NiumaUI"));
            WarnFailure(LastResult);
        }

        private void ResolveButton()
        {
            if (targetButton == null)
            {
                targetButton = GetComponent<Button>();
            }
        }

        private void WarnFailure(AudioOperationResult result)
        {
            if (!logWarnings || result == null || result.Succeeded)
            {
                return;
            }

            Debug.LogWarning($"[NiumaAudio] 按钮点击音效播放失败：{result.FailureReason}，{result.Message}", this);
        }
    }
}
