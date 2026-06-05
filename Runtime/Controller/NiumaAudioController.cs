using System;
using NiumaAudio.Config;
using NiumaAudio.Data;
using NiumaAudio.Service;
using NiumaCore.Module;
using UnityEngine;

namespace NiumaAudio.Controller
{
    /// <summary>
    /// NiumaAudio 根控制器。
    /// 负责把纯 C# AudioService 接入 Unity 生命周期、Inspector 配置和 GameContext。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NiumaAudioController : MonoBehaviour, IGameModule
    {
        [Header("音频配置")]
        [Tooltip("声音 Cue 定义列表。CueId 必须稳定且不可重复，业务模块推荐只传 CueId。")]
        [SerializeField] private AudioCueDefinition[] cueDefinitions = Array.Empty<AudioCueDefinition>();

        [Tooltip("第一版本地音频目录。通过 AddressKey 映射到 AudioClip，后续可替换为 Addressables Resolver。")]
        [SerializeField] private AudioCatalogDefinition audioCatalog;

        [Tooltip("AudioBus 到 AudioMixerGroup 的绑定。未绑定时使用 AudioSource.volume 兜底。")]
        [SerializeField] private AudioMixerGroupConfig[] mixerGroups = Array.Empty<AudioMixerGroupConfig>();

        [Tooltip("音频资源解析脚本。接 Addressables 或热更新时拖对应的 AudioClipResolver；第一版使用 AudioCatalog 时可留空。")]
        [SerializeField] private MonoBehaviour clipResolverBehaviour;

        [Header("AudioSource 池")]
        [Tooltip("音频 Source 根节点。为空时使用当前物体 Transform。建议把控制器放在全局 AudioRoot 下。")]
        [SerializeField] private Transform audioRoot;

        [Tooltip("普通 SFX/UI/Ambient Source 初始数量。")]
        [SerializeField] private int poolInitialSize = 8;

        [Tooltip("普通 SFX/UI/Ambient Source 最大数量。池满时播放请求会返回 SourceUnavailable。")]
        [SerializeField] private int poolMaxSize = 32;

        [Header("模块启动")]
        [Tooltip("Awake 时是否自动初始化音频服务。没有统一模块启动器时建议开启。")]
        [SerializeField] private bool initializeOnAwake = true;

        [Tooltip("OnEnable 时是否自动启动模块 Tick。没有统一模块启动器时建议开启。")]
        [SerializeField] private bool startOnEnable = true;

        [Tooltip("初始化时是否把 IAudioService / IAudioQuery / IAudioCommand 注册到 GameContext。")]
        [SerializeField] private bool registerServiceToContext = true;

        [Tooltip("是否由本控制器 Update 自动驱动 Tick。若已有统一模块启动器调用 IGameModule.Tick，请关闭，避免淡入淡出每帧执行两次。")]
        [SerializeField] private bool driveTickInUpdate = true;

        [Header("调试：BGM")]
        [Tooltip("调试 BGM CueId。优先使用 CueId；为空时使用 AddressKey。")]
        [SerializeField] private string debugBgmCueId;

        [Tooltip("调试 BGM AddressKey。CueId 为空或 Cue 未配置 AddressKey 时使用。")]
        [SerializeField] private string debugBgmAddressKey;

        [Tooltip("调试 BGM 淡入淡出时间。")]
        [SerializeField] private float debugBgmFadeSeconds = 1f;

        [Tooltip("重复播放同一个 BGM 时是否重新开始。")]
        [SerializeField] private bool debugRestartBgmIfSame;

        [Header("调试：Cue")]
        [Tooltip("调试 CueId。用于右键菜单播放普通音效。")]
        [SerializeField] private string debugCueId;

        [Tooltip("调试 AddressKey。CueId 为空或 Cue 未配置 AddressKey 时使用。")]
        [SerializeField] private string debugAddressKey;

        [Tooltip("调试播放时使用的音量倍率。")]
        [SerializeField] private float debugVolumeScale = 1f;

        [Tooltip("调试 3D 播放位置。")]
        [SerializeField] private Vector3 debugWorldPosition;

        [Header("调试：环境音")]
        [Tooltip("调试环境音 ChannelId。同一 ChannelId 会替换旧环境音。")]
        [SerializeField] private string debugAmbientChannelId = "debug_ambient";

        [Header("调试：Bus")]
        [Tooltip("调试总线。用于右键菜单设置音量和静音。")]
        [SerializeField] private AudioBus debugBus = AudioBus.Bgm;

        [Range(0f, 1f)]
        [Tooltip("调试总线音量。")]
        [SerializeField] private float debugBusVolume = 1f;

        [Tooltip("调试总线静音状态。")]
        [SerializeField] private bool debugMuted;

        private AudioService _audioService;
        private IAudioConfigurationService _configurationService;
        private GameContext _context;
        private IAudioClipResolver _clipResolver;
        private bool _clipResolverLocked;
        private bool _warnedInvalidClipResolver;
        private bool _warnedInitializeFailure;
        private bool _warnedServiceNotReady;
        private bool _warnedMissingCueDefinitions;
        private bool _autoInitializeFailed;
        private bool _isDestroyed;

        public string ModuleName => "NiumaAudio";
        public bool IsInitialized { get; private set; }
        public bool IsRunning { get; private set; }
        public long AudioRevision => _audioService != null ? _audioService.Revision : 0L;
        public string CurrentBgmCueId => _audioService != null ? _audioService.CurrentBgmCueId : null;
        public string CurrentBgmAddressKey => _audioService != null ? _audioService.CurrentBgmAddressKey : null;
        public IAudioService AudioService => _audioService;
        public IAudioQuery AudioQuery => _audioService;
        public IAudioCommand AudioCommand => _audioService;
        public AudioOperationResult LastOperationResult { get; private set; }

        private void Awake()
        {
            if (initializeOnAwake && !IsInitialized)
            {
                Initialize(null);
            }
        }

        private void OnEnable()
        {
            if (startOnEnable && IsInitialized && !IsRunning)
            {
                StartModule();
            }
        }

        private void OnDisable()
        {
            if (IsRunning)
            {
                StopModule();
            }
        }

        private void OnDestroy()
        {
            UnregisterServicesFromContext();
            DisposeServiceIfNeeded(_audioService);
            _audioService = null;
            _configurationService = null;
            IsRunning = false;
            IsInitialized = false;
            _isDestroyed = true;
        }

        /// <summary>
        /// 初始化音频模块。
        /// 失败时会恢复旧服务和旧 GameContext 注册，避免外部拿到半初始化服务。
        /// </summary>
        public void Initialize(GameContext context)
        {
            var previousService = _audioService;
            var previousConfig = _configurationService;
            var previousContext = _context;
            var previousClipResolver = _clipResolver;
            var previousInitialized = IsInitialized;
            var wasRunning = IsRunning;
            var targetContext = context ?? _context;
            var previousRegisteredService = targetContext != null ? targetContext.GetService<IAudioService>() : null;
            var previousRegisteredQuery = targetContext != null ? targetContext.GetService<IAudioQuery>() : null;
            var previousRegisteredCommand = targetContext != null ? targetContext.GetService<IAudioCommand>() : null;
            var initializedSuccessfully = false;
            AudioService newService = null;
            IsRunning = false;

            try
            {
                _context = targetContext;
                WarnIfConfigMissing();

                if (!_clipResolverLocked)
                {
                    _clipResolver = ResolveClipResolver();
                }

                var snapshot = previousService != null ? previousService.ExportSnapshot() : null;
                newService = new AudioService(
                    audioRoot != null ? audioRoot : transform,
                    cueDefinitions,
                    audioCatalog,
                    mixerGroups,
                    _clipResolver,
                    poolInitialSize,
                    poolMaxSize);

                if (snapshot != null)
                {
                    LastOperationResult = newService.ImportSnapshot(snapshot);
                }

                _audioService = newService;
                _configurationService = newService;
                RegisterServicesToContext();
                IsInitialized = true;
                _warnedInitializeFailure = false;
                _warnedServiceNotReady = false;
                _autoInitializeFailed = false;
                initializedSuccessfully = true;
            }
            catch (Exception exception)
            {
                if (!_warnedInitializeFailure)
                {
                    Debug.LogError($"[NiumaAudio] 初始化失败：{exception.Message}", this);
                    _warnedInitializeFailure = true;
                }

                RestoreRegisteredAudioServices(targetContext, previousRegisteredService, previousRegisteredQuery, previousRegisteredCommand, newService);
                DisposeServiceIfNeeded(newService);
                _audioService = previousService;
                _configurationService = previousConfig;
                _context = previousContext;
                _clipResolver = previousClipResolver;
                IsInitialized = previousInitialized;
                _autoInitializeFailed = true;
            }
            finally
            {
                IsRunning = initializedSuccessfully
                    ? wasRunning && _audioService != null
                    : wasRunning && previousInitialized && previousService != null;
            }
        }

        /// <summary>
        /// 启动模块 Tick。不会主动播放 BGM，BGM 由场景、剧情或业务模块请求播放。
        /// </summary>
        public void StartModule()
        {
            if (!IsInitialized)
            {
                Initialize(_context);
            }

            IsRunning = _audioService != null;
        }

        /// <summary>
        /// 停止模块 Tick。
        /// 这里不会停止 BGM、环境音或语音，避免场景切换时被误中断；真正释放由 OnDestroy 处理。
        /// </summary>
        public void StopModule()
        {
            IsRunning = false;
        }

        /// <summary>
        /// 推进淡入淡出和 Source 回收。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!IsRunning || _audioService == null)
            {
                return;
            }

            _audioService.Tick(deltaTime);
        }

        private void Update()
        {
            if (driveTickInUpdate)
            {
                Tick(Time.deltaTime);
            }
        }

        public void SetCueDefinitions(AudioCueDefinition[] definitions)
        {
            cueDefinitions = definitions ?? Array.Empty<AudioCueDefinition>();
            _warnedMissingCueDefinitions = false;
            _autoInitializeFailed = false;
            _configurationService?.SetCueDefinitions(cueDefinitions);
        }

        public void SetAudioCatalog(AudioCatalogDefinition catalog)
        {
            audioCatalog = catalog;
            _autoInitializeFailed = false;
            _configurationService?.SetAudioCatalog(audioCatalog);
        }

        public void SetMixerGroups(AudioMixerGroupConfig[] configs)
        {
            mixerGroups = configs ?? Array.Empty<AudioMixerGroupConfig>();
            _autoInitializeFailed = false;
            _configurationService?.SetMixerGroups(mixerGroups);
        }

        /// <summary>
        /// 运行时注入外部资源解析器，并锁定自动解析，避免后续 Initialize 静默覆盖外部注入。
        /// </summary>
        public void SetClipResolver(IAudioClipResolver resolver)
        {
            _clipResolver = resolver;
            _clipResolverLocked = true;
            _autoInitializeFailed = false;
            TryApplyConfiguration(() => _configurationService?.SetClipResolver(_clipResolver), "设置音频资源解析器");
        }

        /// <summary>
        /// 解除外部资源解析器锁定，重新从 Inspector 上的 clipResolverBehaviour 解析。
        /// </summary>
        public void UnlockClipResolver()
        {
            _clipResolverLocked = false;
            _clipResolver = ResolveClipResolver();
            _autoInitializeFailed = false;
            TryApplyConfiguration(() => _configurationService?.SetClipResolver(_clipResolver), "解除音频资源解析器锁定");
        }

        public AudioOperationResult PlayBgm(AudioBgmRequest request)
        {
            if (!EnsureServiceReady(true))
            {
                LastOperationResult = AudioOperationResult.Failed(AudioFailureReason.ServiceNotReady, "音频服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _audioService.PlayBgm(request);
            return LastOperationResult;
        }

        public AudioOperationResult StopBgm(float fadeSeconds = 1f)
        {
            if (!EnsureServiceReady(true))
            {
                LastOperationResult = AudioOperationResult.Failed(AudioFailureReason.ServiceNotReady, "音频服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _audioService.StopBgm(fadeSeconds);
            return LastOperationResult;
        }

        public AudioOperationResult PlayCue(AudioPlayRequest request)
        {
            if (!EnsureServiceReady(true))
            {
                LastOperationResult = AudioOperationResult.Failed(AudioFailureReason.ServiceNotReady, "音频服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _audioService.PlayCue(request);
            return LastOperationResult;
        }

        public AudioOperationResult PlayCue3D(AudioPlayRequest request)
        {
            if (!EnsureServiceReady(true))
            {
                LastOperationResult = AudioOperationResult.Failed(AudioFailureReason.ServiceNotReady, "音频服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _audioService.PlayCue3D(request);
            return LastOperationResult;
        }

        public AudioOperationResult PlayVoice(AudioPlayRequest request)
        {
            if (!EnsureServiceReady(true))
            {
                LastOperationResult = AudioOperationResult.Failed(AudioFailureReason.ServiceNotReady, "音频服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _audioService.PlayVoice(request);
            return LastOperationResult;
        }

        public AudioOperationResult StopVoice(float fadeSeconds = 0f)
        {
            if (!EnsureServiceReady(true))
            {
                LastOperationResult = AudioOperationResult.Failed(AudioFailureReason.ServiceNotReady, "音频服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _audioService.StopVoice(fadeSeconds);
            return LastOperationResult;
        }

        public AudioOperationResult PlayAmbient(AudioAmbientRequest request)
        {
            if (!EnsureServiceReady(true))
            {
                LastOperationResult = AudioOperationResult.Failed(AudioFailureReason.ServiceNotReady, "音频服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _audioService.PlayAmbient(request);
            return LastOperationResult;
        }

        public AudioOperationResult StopAmbient(string channelId, float fadeSeconds = 1f)
        {
            if (!EnsureServiceReady(true))
            {
                LastOperationResult = AudioOperationResult.Failed(AudioFailureReason.ServiceNotReady, "音频服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _audioService.StopAmbient(channelId, fadeSeconds);
            return LastOperationResult;
        }

        public AudioOperationResult SetVolume(AudioBus bus, float volume)
        {
            if (!EnsureServiceReady(true))
            {
                LastOperationResult = AudioOperationResult.Failed(AudioFailureReason.ServiceNotReady, "音频服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _audioService.SetVolume(bus, volume);
            return LastOperationResult;
        }

        public AudioOperationResult SetMuted(AudioBus bus, bool muted)
        {
            if (!EnsureServiceReady(true))
            {
                LastOperationResult = AudioOperationResult.Failed(AudioFailureReason.ServiceNotReady, "音频服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _audioService.SetMuted(bus, muted);
            return LastOperationResult;
        }

        public float GetVolume(AudioBus bus)
        {
            return EnsureServiceReady(false) ? _audioService.GetVolume(bus) : 1f;
        }

        public bool IsMuted(AudioBus bus)
        {
            return EnsureServiceReady(false) && _audioService.IsMuted(bus);
        }

        public bool IsPlaying(AudioHandle handle)
        {
            return EnsureServiceReady(false) && _audioService.IsPlaying(handle);
        }

        public AudioSettingsSnapshot ExportSnapshot()
        {
            return EnsureServiceReady(false) ? _audioService.ExportSnapshot() : new AudioSettingsSnapshot();
        }

        public AudioOperationResult ImportSnapshot(AudioSettingsSnapshot snapshot)
        {
            if (!EnsureServiceReady(true))
            {
                LastOperationResult = AudioOperationResult.Failed(AudioFailureReason.ServiceNotReady, "音频服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _audioService.ImportSnapshot(snapshot);
            return LastOperationResult;
        }

        [ContextMenu("NiumaAudio/重新初始化服务")]
        private void DebugReinitialize()
        {
            Initialize(_context);
            Debug.Log($"[NiumaAudio] 重新初始化完成：Initialized={IsInitialized}, Running={IsRunning}, Revision={AudioRevision}", this);
        }

        [ContextMenu("NiumaAudio/启动模块")]
        private void DebugStartModule()
        {
            StartModule();
            Debug.Log($"[NiumaAudio] 启动模块：Running={IsRunning}", this);
        }

        [ContextMenu("NiumaAudio/停止模块")]
        private void DebugStopModule()
        {
            StopModule();
            Debug.Log("[NiumaAudio] 已停止模块 Tick，当前播放声音不会被主动停止。", this);
        }

        [ContextMenu("NiumaAudio/播放调试 BGM")]
        private void DebugPlayBgm()
        {
            LogResult("播放调试 BGM", PlayBgm(new AudioBgmRequest
            {
                CueId = debugBgmCueId,
                AddressKey = debugBgmAddressKey,
                FadeSeconds = Mathf.Max(0f, debugBgmFadeSeconds),
                RestartIfSame = debugRestartBgmIfSame,
                SourceModule = "debug"
            }));
        }

        [ContextMenu("NiumaAudio/停止 BGM")]
        private void DebugStopBgm()
        {
            LogResult("停止 BGM", StopBgm(Mathf.Max(0f, debugBgmFadeSeconds)));
        }

        [ContextMenu("NiumaAudio/播放调试 Cue")]
        private void DebugPlayCue()
        {
            LogResult("播放调试 Cue", PlayCue(CreateDebugPlayRequest(false)));
        }

        [ContextMenu("NiumaAudio/播放调试 3D Cue")]
        private void DebugPlayCue3D()
        {
            LogResult("播放调试 3D Cue", PlayCue3D(CreateDebugPlayRequest(true)));
        }

        [ContextMenu("NiumaAudio/播放调试 Voice")]
        private void DebugPlayVoice()
        {
            LogResult("播放调试 Voice", PlayVoice(CreateDebugPlayRequest(false)));
        }

        [ContextMenu("NiumaAudio/停止 Voice")]
        private void DebugStopVoice()
        {
            LogResult("停止 Voice", StopVoice(0f));
        }

        [ContextMenu("NiumaAudio/播放调试 Ambient")]
        private void DebugPlayAmbient()
        {
            LogResult("播放调试 Ambient", PlayAmbient(new AudioAmbientRequest
            {
                ChannelId = debugAmbientChannelId,
                CueId = debugCueId,
                AddressKey = debugAddressKey,
                FadeSeconds = Mathf.Max(0f, debugBgmFadeSeconds),
                SourceModule = "debug"
            }));
        }

        [ContextMenu("NiumaAudio/停止调试 Ambient")]
        private void DebugStopAmbient()
        {
            LogResult("停止调试 Ambient", StopAmbient(debugAmbientChannelId, Mathf.Max(0f, debugBgmFadeSeconds)));
        }

        [ContextMenu("NiumaAudio/设置调试 Bus 音量")]
        private void DebugSetBusVolume()
        {
            LogResult("设置 Bus 音量", SetVolume(debugBus, debugBusVolume));
        }

        [ContextMenu("NiumaAudio/设置调试 Bus 静音")]
        private void DebugSetBusMuted()
        {
            LogResult("设置 Bus 静音", SetMuted(debugBus, debugMuted));
        }

        [ContextMenu("NiumaAudio/打印设置快照")]
        private void DebugPrintSnapshot()
        {
            var snapshot = ExportSnapshot();
            Debug.Log($"[NiumaAudio] Snapshot Version={snapshot.Version}, Revision={snapshot.Revision}, CurrentBgmCueId={snapshot.CurrentBgmCueId}, CurrentBgmAddressKey={snapshot.CurrentBgmAddressKey}, BusCount={snapshot.BusVolumes?.Length ?? 0}", this);
            if (snapshot.BusVolumes == null)
            {
                return;
            }

            for (var i = 0; i < snapshot.BusVolumes.Length; i++)
            {
                var item = snapshot.BusVolumes[i];
                if (item == null)
                {
                    continue;
                }

                Debug.Log($"[NiumaAudio] Bus={item.Bus}, Volume={item.Volume:0.00}, Muted={item.Muted}", this);
            }
        }

        private AudioPlayRequest CreateDebugPlayRequest(bool usePosition)
        {
            return new AudioPlayRequest
            {
                CueId = debugCueId,
                AddressKey = debugAddressKey,
                Position = debugWorldPosition,
                HasPosition = usePosition,
                VolumeScale = Mathf.Max(0f, debugVolumeScale),
                SourceModule = "debug"
            };
        }

        private bool EnsureServiceReady(bool allowAutoInitialize)
        {
            if (_audioService != null)
            {
                return true;
            }

            if (_isDestroyed || !allowAutoInitialize || _autoInitializeFailed)
            {
                WarnServiceNotReadyOnce();
                return false;
            }

            Initialize(_context);
            if (_audioService != null)
            {
                return true;
            }

            _autoInitializeFailed = true;
            WarnServiceNotReadyOnce();
            return false;
        }

        private void RegisterServicesToContext()
        {
            if (!registerServiceToContext || _context == null || _audioService == null)
            {
                return;
            }

            _context.RegisterService<IAudioService>(_audioService);
            _context.RegisterService<IAudioQuery>(_audioService);
            _context.RegisterService<IAudioCommand>(_audioService);
        }

        private void RestoreRegisteredAudioServices(GameContext targetContext, IAudioService previousService, IAudioQuery previousQuery, IAudioCommand previousCommand, IAudioService failedService)
        {
            if (targetContext == null)
            {
                return;
            }

            if (ReferenceEquals(targetContext.GetService<IAudioService>(), failedService))
            {
                RestoreService(targetContext, previousService);
            }

            if (ReferenceEquals(targetContext.GetService<IAudioQuery>(), failedService))
            {
                RestoreService(targetContext, previousQuery);
            }

            if (ReferenceEquals(targetContext.GetService<IAudioCommand>(), failedService))
            {
                RestoreService(targetContext, previousCommand);
            }
        }

        private static void RestoreService<T>(GameContext context, T previousService) where T : class
        {
            if (previousService != null)
            {
                context.RegisterService(previousService);
            }
            else
            {
                context.UnregisterService<T>();
            }
        }

        private void UnregisterServicesFromContext()
        {
            if (_context == null)
            {
                return;
            }

            if (ReferenceEquals(_context.GetService<IAudioService>(), _audioService))
            {
                _context.UnregisterService<IAudioService>();
            }

            if (ReferenceEquals(_context.GetService<IAudioQuery>(), _audioService))
            {
                _context.UnregisterService<IAudioQuery>();
            }

            if (ReferenceEquals(_context.GetService<IAudioCommand>(), _audioService))
            {
                _context.UnregisterService<IAudioCommand>();
            }
        }

        private IAudioClipResolver ResolveClipResolver()
        {
            if (clipResolverBehaviour == null)
            {
                return null;
            }

            if (clipResolverBehaviour is IAudioClipResolver resolver)
            {
                _warnedInvalidClipResolver = false;
                return resolver;
            }

            if (!_warnedInvalidClipResolver)
            {
                Debug.LogWarning("[NiumaAudio] ClipResolver 绑定的不是音频资源解析脚本，已忽略；使用 AudioCatalog 时可留空。", clipResolverBehaviour);
                _warnedInvalidClipResolver = true;
            }

            return null;
        }

        private void TryApplyConfiguration(Action action, string actionName)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[NiumaAudio] {actionName} 失败：{exception.Message}", this);
            }
        }

        private void WarnIfConfigMissing()
        {
            if ((cueDefinitions == null || cueDefinitions.Length == 0) && !_warnedMissingCueDefinitions)
            {
                Debug.LogWarning("[NiumaAudio] 未配置任何 AudioCueDefinition。仍可通过 AddressKey 直接播放，但业务推荐使用 CueId。", this);
                _warnedMissingCueDefinitions = true;
            }
        }

        private void WarnServiceNotReadyOnce()
        {
            if (_warnedServiceNotReady)
            {
                return;
            }

            Debug.LogWarning("[NiumaAudio] 音频服务尚未初始化。", this);
            _warnedServiceNotReady = true;
        }

        private static void DisposeServiceIfNeeded(AudioService service)
        {
            service?.Dispose();
        }

        private void LogResult(string actionName, AudioOperationResult result)
        {
            if (result == null)
            {
                Debug.LogWarning($"[NiumaAudio] {actionName} 返回空结果。", this);
                return;
            }

            var message = $"[NiumaAudio] {actionName}：Succeeded={result.Succeeded}, Reason={result.FailureReason}, Handle={result.Handle.HandleId}, Message={result.Message}";
            if (result.Succeeded)
            {
                Debug.Log(message, this);
            }
            else
            {
                Debug.LogWarning(message, this);
            }
        }

        private void OnValidate()
        {
            poolInitialSize = Mathf.Max(0, poolInitialSize);
            poolMaxSize = Mathf.Max(1, poolMaxSize);
            if (poolInitialSize > poolMaxSize)
            {
                poolInitialSize = poolMaxSize;
            }

            debugBgmFadeSeconds = Mathf.Max(0f, debugBgmFadeSeconds);
            debugVolumeScale = Mathf.Max(0f, debugVolumeScale);
        }
    }
}
