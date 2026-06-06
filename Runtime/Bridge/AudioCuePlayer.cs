using NiumaAudio.Controller;
using NiumaAudio.Data;
using NiumaAudio.Service;
using NiumaCore.Module;
using UnityEngine;

namespace NiumaAudio.Bridge
{
    /// <summary>
    /// 通用 Cue 播放器。
    /// 可挂在按钮、触发器、场景音效点或调试物体上，通过 UnityEvent 直接调用播放方法。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioCuePlayer : MonoBehaviour
    {
        private const string DefaultSourceModule = "NiumaAudioBridge";

        [Header("音频服务")]
        [Tooltip("拖入 AudioRoot 上的 NiumaAudioController。为空且启用自动查找时，会在场景中查找 NiumaAudioController。")]
        [SerializeField] private NiumaAudioController audioController;

        [Tooltip("Audio Controller 为空时，是否自动查找场景中的 NiumaAudioController。正式场景推荐手动绑定。")]
        [SerializeField] private bool autoFindAudioController = true;

        [Tooltip("播放失败或 CueId 缺失时是否输出警告日志。")]
        [SerializeField] private bool logWarnings = true;

        [Header("默认 Cue")]
        [Tooltip("默认播放的 Cue。CueId 填 AudioCueDefinition.CueId；AddressKey 一般留空。")]
        [SerializeField] private AudioCueBinding defaultCue = new AudioCueBinding();

        [Tooltip("3D 播放时使用的位置来源。为空时使用当前物体位置。")]
        [SerializeField] private Transform positionSource;

        [Tooltip("环境音 ChannelId。同一 ChannelId 的新环境音会替换旧环境音。")]
        [SerializeField] private string ambientChannelId = "default";

        [Tooltip("重复播放同一 BGM 时是否从头开始。")]
        [SerializeField] private bool restartBgmIfSame;

        private IAudioCommand _runtimeCommand;
        private GameContext _runtimeContext;

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

        /// <summary>
        /// UnityEvent 调用入口：播放默认 2D Cue。
        /// </summary>
        public void PlayCue()
        {
            LastResult = PlayCue(defaultCue);
        }

        /// <summary>
        /// UnityEvent 调用入口：在 positionSource 或当前物体位置播放默认 3D Cue。
        /// </summary>
        public void PlayCue3D()
        {
            var position = positionSource != null ? positionSource.position : transform.position;
            LastResult = PlayCueAtPosition(defaultCue, position);
        }

        /// <summary>
        /// UnityEvent 调用入口：播放默认 Cue 作为 BGM。
        /// </summary>
        public void PlayBgm()
        {
            LastResult = PlayBgm(defaultCue, restartBgmIfSame);
        }

        /// <summary>
        /// UnityEvent 调用入口：停止当前 BGM。
        /// </summary>
        public void StopBgm()
        {
            if (!TryResolveCommand(out var command))
            {
                LastResult = BuildServiceNotReadyResult();
                WarnFailure(LastResult);
                return;
            }

            LastResult = command.StopBgm(defaultCue != null ? defaultCue.FadeSeconds : 1f);
            WarnFailure(LastResult);
        }

        /// <summary>
        /// UnityEvent 调用入口：播放默认 Cue 作为语音。
        /// </summary>
        public void PlayVoice()
        {
            LastResult = PlayVoice(defaultCue);
        }

        /// <summary>
        /// UnityEvent 调用入口：停止当前语音。
        /// </summary>
        public void StopVoice()
        {
            if (!TryResolveCommand(out var command))
            {
                LastResult = BuildServiceNotReadyResult();
                WarnFailure(LastResult);
                return;
            }

            LastResult = command.StopVoice(defaultCue != null ? defaultCue.FadeSeconds : 0f);
            WarnFailure(LastResult);
        }

        /// <summary>
        /// UnityEvent 调用入口：在 ambientChannelId 上播放默认环境音。
        /// </summary>
        public void PlayAmbient()
        {
            LastResult = PlayAmbient(defaultCue, ambientChannelId);
        }

        /// <summary>
        /// UnityEvent 调用入口：停止 ambientChannelId 上的环境音。
        /// </summary>
        public void StopAmbient()
        {
            if (!TryResolveCommand(out var command))
            {
                LastResult = BuildServiceNotReadyResult();
                WarnFailure(LastResult);
                return;
            }

            LastResult = command.StopAmbient(ambientChannelId, defaultCue != null ? defaultCue.FadeSeconds : 1f);
            WarnFailure(LastResult);
        }

        public AudioOperationResult PlayCue(AudioCueBinding cue)
        {
            if (!TryPrepare(cue, out var command))
            {
                return LastResult;
            }

            LastResult = command.PlayCue(cue.ToPlayRequest(DefaultSourceModule));
            WarnFailure(LastResult);
            return LastResult;
        }

        public AudioOperationResult PlayCueAtPosition(AudioCueBinding cue, Vector3 position)
        {
            if (!TryPrepare(cue, out var command))
            {
                return LastResult;
            }

            LastResult = command.PlayCue3D(cue.ToPlayRequest(position, true, DefaultSourceModule));
            WarnFailure(LastResult);
            return LastResult;
        }

        public AudioOperationResult PlayBgm(AudioCueBinding cue, bool restartIfSame = false)
        {
            if (!TryPrepare(cue, out var command))
            {
                return LastResult;
            }

            LastResult = command.PlayBgm(cue.ToBgmRequest(restartIfSame, DefaultSourceModule));
            WarnFailure(LastResult);
            return LastResult;
        }

        public AudioOperationResult PlayVoice(AudioCueBinding cue)
        {
            if (!TryPrepare(cue, out var command))
            {
                return LastResult;
            }

            LastResult = command.PlayVoice(cue.ToPlayRequest(DefaultSourceModule));
            WarnFailure(LastResult);
            return LastResult;
        }

        public AudioOperationResult PlayAmbient(AudioCueBinding cue, string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                LastResult = AudioOperationResult.Failed(
                    AudioFailureReason.InvalidRequest,
                    "环境音 ChannelId 为空，无法播放环境音。");
                WarnFailure(LastResult);
                return LastResult;
            }

            if (!TryPrepare(cue, out var command))
            {
                return LastResult;
            }

            LastResult = command.PlayAmbient(cue.ToAmbientRequest(channelId, DefaultSourceModule));
            WarnFailure(LastResult);
            return LastResult;
        }

        private bool TryPrepare(AudioCueBinding cue, out IAudioCommand command)
        {
            if (cue == null || !cue.HasPlayableKey)
            {
                command = null;
                LastResult = AudioOperationResult.Failed(
                    AudioFailureReason.InvalidRequest,
                    "AudioCueBinding 未填写 CueId 或 AddressKey。");
                WarnFailure(LastResult);
                return false;
            }

            if (!TryResolveCommand(out command))
            {
                LastResult = BuildServiceNotReadyResult();
                WarnFailure(LastResult);
                return false;
            }

            return true;
        }

        private bool TryResolveCommand(out IAudioCommand command)
        {
            var resolved = AudioBridgeResolver.TryResolveCommand(
                _runtimeCommand,
                _runtimeContext,
                audioController,
                autoFindAudioController,
                out command,
                out var resolvedController);

            if (resolvedController != null)
            {
                audioController = resolvedController;
            }

            return resolved;
        }

        private static AudioOperationResult BuildServiceNotReadyResult()
        {
            return AudioOperationResult.Failed(
                AudioFailureReason.ServiceNotReady,
                "未找到可用的 NiumaAudioController 或 IAudioCommand。");
        }

        private void WarnFailure(AudioOperationResult result)
        {
            if (!logWarnings || result == null || result.Succeeded)
            {
                return;
            }

            Debug.LogWarning($"[NiumaAudio] Cue 播放失败：{result.FailureReason}，{result.Message}", this);
        }
    }
}
