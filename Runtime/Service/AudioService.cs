using System;
using System.Collections.Generic;
using NiumaAudio.Config;
using NiumaAudio.Data;
using UnityEngine;
using UnityEngine.Audio;

namespace NiumaAudio.Service
{
    /// <summary>
    /// NiumaAudio 核心服务。
    /// 第一版负责 Cue 解析、AudioSource 播放调度、音量静音、BGM 淡入淡出和音频设置快照。
    /// </summary>
    public sealed class AudioService : IAudioService, IAudioConfigurationService, IDisposable
    {
        private const int DefaultPoolInitialSize = 8;
        private const int DefaultPoolMaxSize = 32;

        private readonly Dictionary<string, AudioCueDefinition> _cueDefinitions = new Dictionary<string, AudioCueDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<AudioBus, AudioBusVolumeSnapshot> _busSettings = new Dictionary<AudioBus, AudioBusVolumeSnapshot>();
        private readonly Dictionary<AudioBus, AudioMixerGroup> _mixerGroups = new Dictionary<AudioBus, AudioMixerGroup>();
        private readonly Dictionary<int, AudioRuntimePlayback> _playbacksByHandle = new Dictionary<int, AudioRuntimePlayback>();
        private readonly Dictionary<string, AudioRuntimePlayback> _ambientByChannel = new Dictionary<string, AudioRuntimePlayback>(StringComparer.Ordinal);
        private readonly List<AudioRuntimePlayback> _playbacks = new List<AudioRuntimePlayback>();
        private readonly List<AudioRuntimePlayback> _removeBuffer = new List<AudioRuntimePlayback>();
        private readonly LocalAudioCatalogResolver _localCatalogResolver = new LocalAudioCatalogResolver();

        private Transform _root;
        private AudioSourcePool _sourcePool;
        private IAudioClipResolver _customClipResolver;
        private AudioSource _bgmSourceA;
        private AudioSource _bgmSourceB;
        private AudioSource _voiceSource;
        private AudioRuntimePlayback _currentBgm;
        private AudioRuntimePlayback _currentVoice;
        private int _nextHandleId = 1;
        private long _revision;
        private string _currentBgmCueId;
        private string _currentBgmAddressKey;

        public long Revision => _revision;
        public string CurrentBgmCueId => _currentBgmCueId;
        public string CurrentBgmAddressKey => _currentBgmAddressKey;

        public AudioService(
            Transform root = null,
            AudioCueDefinition[] cueDefinitions = null,
            AudioCatalogDefinition audioCatalog = null,
            AudioMixerGroupConfig[] mixerGroups = null,
            IAudioClipResolver clipResolver = null,
            int poolInitialSize = DefaultPoolInitialSize,
            int poolMaxSize = DefaultPoolMaxSize)
        {
            _root = root;
            InitializeBusSettings();
            SetCueDefinitions(cueDefinitions);
            SetAudioCatalog(audioCatalog);
            SetMixerGroups(mixerGroups);
            SetClipResolver(clipResolver);
            BuildUnitySources(poolInitialSize, poolMaxSize);
        }

        /// <summary>
        /// Tick 由 Controller 驱动，用于推进淡入淡出和回收播放结束的 Source。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f)
            {
                deltaTime = 0f;
            }

            _removeBuffer.Clear();
            for (var i = 0; i < _playbacks.Count; i++)
            {
                var playback = _playbacks[i];
                if (playback == null || playback.Source == null)
                {
                    _removeBuffer.Add(playback);
                    continue;
                }

                UpdatePlaybackVolume(playback, deltaTime);
                if (playback.StopWhenSilent && playback.CurrentVolume <= 0.0001f)
                {
                    _removeBuffer.Add(playback);
                    continue;
                }

                if (!playback.Source.loop && !playback.Source.isPlaying)
                {
                    _removeBuffer.Add(playback);
                }
            }

            for (var i = 0; i < _removeBuffer.Count; i++)
            {
                ReleasePlayback(_removeBuffer[i]);
            }
        }

        public void SetCueDefinitions(AudioCueDefinition[] definitions)
        {
            _cueDefinitions.Clear();
            if (definitions == null)
            {
                return;
            }

            for (var i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.CueId))
                {
                    continue;
                }

                if (_cueDefinitions.ContainsKey(definition.CueId))
                {
                    Debug.LogWarning($"[NiumaAudio] 检测到重复 CueId={definition.CueId}，后出现的配置已忽略。", definition);
                    continue;
                }

                _cueDefinitions.Add(definition.CueId, definition);
            }
        }

        public void SetAudioCatalog(AudioCatalogDefinition catalog)
        {
            _localCatalogResolver.SetCatalog(catalog);
        }

        public void SetMixerGroups(AudioMixerGroupConfig[] mixerGroups)
        {
            _mixerGroups.Clear();
            if (mixerGroups == null)
            {
                ApplyAllMixerGroups();
                return;
            }

            for (var i = 0; i < mixerGroups.Length; i++)
            {
                var config = mixerGroups[i];
                if (config == null || config.MixerGroup == null)
                {
                    continue;
                }

                if (!_mixerGroups.ContainsKey(config.Bus))
                {
                    _mixerGroups.Add(config.Bus, config.MixerGroup);
                }
                else
                {
                    Debug.LogWarning($"[NiumaAudio] 检测到重复 AudioBus Mixer 配置：{config.Bus}，后出现的配置已忽略。");
                }
            }

            ApplyAllMixerGroups();
        }

        public void SetClipResolver(IAudioClipResolver resolver)
        {
            _customClipResolver = resolver;
        }

        public float GetVolume(AudioBus bus)
        {
            return GetOrCreateBusSetting(bus).Volume;
        }

        public bool IsMuted(AudioBus bus)
        {
            return GetOrCreateBusSetting(bus).Muted;
        }

        public bool IsPlaying(AudioHandle handle)
        {
            return handle.IsValid
                   && _playbacksByHandle.TryGetValue(handle.HandleId, out var playback)
                   && playback?.Source != null
                   && playback.Source.isPlaying;
        }

        public AudioOperationResult PlayBgm(AudioBgmRequest request)
        {
            if (request == null)
            {
                return AudioOperationResult.Failed(AudioFailureReason.InvalidRequest, "BGM 播放请求为空。");
            }

            if (!TryResolveAudio(request.CueId, request.AddressKey, AudioBus.Bgm, out var resolved, out var error))
            {
                return error;
            }

            resolved.Bus = AudioBus.Bgm;

            if (_bgmSourceA == null || _bgmSourceB == null)
            {
                return AudioOperationResult.Failed(AudioFailureReason.SourceUnavailable, "BGM AudioSource 未初始化。");
            }

            if (_currentBgm != null
                && !request.RestartIfSame
                && IsSameBgm(_currentBgm, resolved)
                && _currentBgm.Source != null
                && _currentBgm.Source.isPlaying)
            {
                return AudioOperationResult.Success(_currentBgm.Handle, "当前 BGM 已在播放。");
            }

            if (_currentBgm != null)
            {
                FadeOutPlayback(_currentBgm, Mathf.Max(0f, request.FadeSeconds));
            }

            var source = _currentBgm == null || _currentBgm.Source == _bgmSourceB ? _bgmSourceA : _bgmSourceB;
            PreparePlaybackSource(source, resolved, Vector3.zero, false, true);
            var handle = CreateHandle(resolved.CueId, AudioBus.Bgm);
            var playback = new AudioRuntimePlayback(handle, resolved.CueId, resolved.AddressKey, AudioBus.Bgm, source, true, false, 1f, resolved.Volume)
            {
                FadeSeconds = Mathf.Max(0f, request.FadeSeconds),
                CurrentVolume = request.FadeSeconds > 0f ? 0f : 1f,
                TargetVolume = 1f
            };

            source.volume = CalculateFinalVolume(playback);
            source.Play();
            RegisterPlayback(playback);
            _currentBgm = playback;
            _currentBgmCueId = resolved.CueId;
            _currentBgmAddressKey = resolved.AddressKey;
            BumpRevision();
            return AudioOperationResult.Success(handle);
        }

        public AudioOperationResult StopBgm(float fadeSeconds = 1f)
        {
            if (_currentBgm == null)
            {
                _currentBgmCueId = null;
                _currentBgmAddressKey = null;
                return AudioOperationResult.Success("当前没有 BGM。");
            }

            FadeOutPlayback(_currentBgm, Mathf.Max(0f, fadeSeconds));
            _currentBgm = null;
            _currentBgmCueId = null;
            _currentBgmAddressKey = null;
            BumpRevision();
            return AudioOperationResult.Success();
        }

        public AudioOperationResult PlayCue(AudioPlayRequest request)
        {
            return PlayCueInternal(request, false);
        }

        public AudioOperationResult PlayCue3D(AudioPlayRequest request)
        {
            return PlayCueInternal(request, true);
        }

        public AudioOperationResult PlayVoice(AudioPlayRequest request)
        {
            if (request == null)
            {
                return AudioOperationResult.Failed(AudioFailureReason.InvalidRequest, "语音播放请求为空。");
            }

            if (!TryResolveAudio(request.CueId, request.AddressKey, AudioBus.Voice, out var resolved, out var error))
            {
                return error;
            }

            resolved.Bus = AudioBus.Voice;

            if (_voiceSource == null)
            {
                return AudioOperationResult.Failed(AudioFailureReason.SourceUnavailable, "Voice AudioSource 未初始化。");
            }

            if (_currentVoice != null)
            {
                ReleasePlayback(_currentVoice);
            }

            PreparePlaybackSource(_voiceSource, resolved, request.Position, request.HasPosition, false);
            var handle = CreateHandle(resolved.CueId, AudioBus.Voice);
            var playback = new AudioRuntimePlayback(handle, resolved.CueId, resolved.AddressKey, AudioBus.Voice, _voiceSource, false, false, Mathf.Max(0f, request.VolumeScale), resolved.Volume)
            {
                CurrentVolume = 1f,
                TargetVolume = 1f
            };

            _voiceSource.volume = CalculateFinalVolume(playback);
            _voiceSource.Play();
            RegisterPlayback(playback);
            _currentVoice = playback;
            return AudioOperationResult.Success(handle);
        }

        public AudioOperationResult StopVoice(float fadeSeconds = 0f)
        {
            if (_currentVoice == null)
            {
                return AudioOperationResult.Success("当前没有语音。");
            }

            FadeOutPlayback(_currentVoice, Mathf.Max(0f, fadeSeconds));
            _currentVoice = null;
            return AudioOperationResult.Success();
        }

        public AudioOperationResult PlayAmbient(AudioAmbientRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ChannelId))
            {
                return AudioOperationResult.Failed(AudioFailureReason.InvalidRequest, "环境音请求为空或 ChannelId 为空。");
            }

            if (!TryResolveAudio(request.CueId, request.AddressKey, AudioBus.Ambient, out var resolved, out var error))
            {
                return error;
            }

            resolved.Bus = AudioBus.Ambient;

            if (_ambientByChannel.TryGetValue(request.ChannelId, out var oldPlayback))
            {
                FadeOutPlayback(oldPlayback, Mathf.Max(0f, request.FadeSeconds));
                _ambientByChannel.Remove(request.ChannelId);
            }

            var source = RentSource(AudioBus.Ambient);
            if (source == null)
            {
                return AudioOperationResult.Failed(AudioFailureReason.SourceUnavailable, "环境音 Source 池已满或未初始化。");
            }

            PreparePlaybackSource(source, resolved, Vector3.zero, false, true);
            var handle = CreateHandle(resolved.CueId, AudioBus.Ambient);
            var playback = new AudioRuntimePlayback(handle, resolved.CueId, resolved.AddressKey, AudioBus.Ambient, source, true, true, 1f, resolved.Volume)
            {
                AmbientChannelId = request.ChannelId,
                FadeSeconds = Mathf.Max(0f, request.FadeSeconds),
                CurrentVolume = request.FadeSeconds > 0f ? 0f : 1f,
                TargetVolume = 1f
            };

            source.volume = CalculateFinalVolume(playback);
            source.Play();
            RegisterPlayback(playback);
            _ambientByChannel[request.ChannelId] = playback;
            return AudioOperationResult.Success(handle);
        }

        public AudioOperationResult StopAmbient(string channelId, float fadeSeconds = 1f)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return AudioOperationResult.Failed(AudioFailureReason.InvalidRequest, "ChannelId 为空。");
            }

            if (!_ambientByChannel.TryGetValue(channelId, out var playback))
            {
                return AudioOperationResult.Success("指定环境音未播放。");
            }

            _ambientByChannel.Remove(channelId);
            FadeOutPlayback(playback, Mathf.Max(0f, fadeSeconds));
            return AudioOperationResult.Success();
        }

        public AudioOperationResult SetVolume(AudioBus bus, float volume)
        {
            var setting = GetOrCreateBusSetting(bus);
            var clamped = Mathf.Clamp01(volume);
            if (Mathf.Abs(setting.Volume - clamped) <= 0.0001f)
            {
                return AudioOperationResult.Success("音量未变化。");
            }

            setting.Volume = clamped;
            ApplyAllVolumes();
            BumpRevision();
            return AudioOperationResult.Success();
        }

        public AudioOperationResult SetMuted(AudioBus bus, bool muted)
        {
            var setting = GetOrCreateBusSetting(bus);
            if (setting.Muted == muted)
            {
                return AudioOperationResult.Success("静音状态未变化。");
            }

            setting.Muted = muted;
            ApplyAllVolumes();
            BumpRevision();
            return AudioOperationResult.Success();
        }

        public AudioSettingsSnapshot ExportSnapshot()
        {
            var volumes = new List<AudioBusVolumeSnapshot>();
            foreach (var pair in _busSettings)
            {
                volumes.Add(pair.Value.Clone());
            }

            volumes.Sort((a, b) => a.Bus.CompareTo(b.Bus));
            return new AudioSettingsSnapshot
            {
                Version = 1,
                Revision = _revision,
                BusVolumes = volumes.ToArray(),
                CurrentBgmCueId = _currentBgmCueId,
                CurrentBgmAddressKey = _currentBgmAddressKey
            };
        }

        public AudioOperationResult ImportSnapshot(AudioSettingsSnapshot snapshot)
        {
            var validation = ValidateSnapshot(snapshot);
            if (!validation.Succeeded)
            {
                return validation;
            }

            InitializeBusSettings();
            for (var i = 0; i < snapshot.BusVolumes.Length; i++)
            {
                var item = snapshot.BusVolumes[i];
                var setting = GetOrCreateBusSetting(item.Bus);
                setting.Volume = Mathf.Clamp01(item.Volume);
                setting.Muted = item.Muted;
            }

            _revision = Math.Max(0, snapshot.Revision);
            _currentBgmCueId = snapshot.CurrentBgmCueId;
            _currentBgmAddressKey = snapshot.CurrentBgmAddressKey;
            ApplyAllVolumes();
            return AudioOperationResult.Success("音频设置已导入。");
        }

        public void Dispose()
        {
            ReleaseAllRuntimePlaybacks();
            DestroySource(_bgmSourceA);
            DestroySource(_bgmSourceB);
            DestroySource(_voiceSource);
            _bgmSourceA = null;
            _bgmSourceB = null;
            _voiceSource = null;
            _sourcePool?.Dispose();
            _sourcePool = null;
        }

        private AudioOperationResult PlayCueInternal(AudioPlayRequest request, bool force3D)
        {
            if (request == null)
            {
                return AudioOperationResult.Failed(AudioFailureReason.InvalidRequest, "声音播放请求为空。");
            }

            var defaultBus = request.HasOverrideBus ? request.OverrideBus : AudioBus.Sfx;
            if (!TryResolveAudio(request.CueId, request.AddressKey, defaultBus, out var resolved, out var error))
            {
                return error;
            }

            var bus = request.HasOverrideBus ? request.OverrideBus : resolved.Bus;
            if (!resolved.AllowOverlap && TryFindActiveCue(resolved.CueId, bus, out var active))
            {
                return AudioOperationResult.Success(active.Handle, "该 Cue 不允许重叠播放，已复用当前播放。");
            }

            var source = RentSource(bus);
            if (source == null)
            {
                return AudioOperationResult.Failed(AudioFailureReason.SourceUnavailable, "声音 Source 池已满或未初始化。");
            }

            resolved.Bus = bus;
            var use3D = force3D || request.HasPosition || resolved.SpatialMode == AudioSpatialMode.ThreeD;
            PreparePlaybackSource(source, resolved, request.Position, use3D, resolved.PlayMode == AudioPlayMode.Loop);
            var handle = CreateHandle(resolved.CueId, bus);
            var playback = new AudioRuntimePlayback(handle, resolved.CueId, resolved.AddressKey, bus, source, resolved.PlayMode == AudioPlayMode.Loop, true, Mathf.Max(0f, request.VolumeScale), resolved.Volume)
            {
                CurrentVolume = 1f,
                TargetVolume = 1f
            };

            source.volume = CalculateFinalVolume(playback);
            source.Play();
            RegisterPlayback(playback);
            return AudioOperationResult.Success(handle);
        }

        private bool TryResolveAudio(string cueId, string addressKey, AudioBus defaultBus, out ResolvedAudioCue resolved, out AudioOperationResult error)
        {
            resolved = default;
            error = null;

            AudioCueDefinition definition = null;
            if (!string.IsNullOrWhiteSpace(cueId) && _cueDefinitions.TryGetValue(cueId, out definition))
            {
                addressKey = !string.IsNullOrWhiteSpace(definition.AddressKey) ? definition.AddressKey : addressKey;
            }

            if (string.IsNullOrWhiteSpace(addressKey))
            {
                error = !string.IsNullOrWhiteSpace(cueId)
                    ? AudioOperationResult.Failed(AudioFailureReason.CueMissing, $"未找到 Cue 或 Cue 未配置 AddressKey：{cueId}")
                    : AudioOperationResult.Failed(AudioFailureReason.InvalidRequest, "CueId 和 AddressKey 不能同时为空。");
                return false;
            }

            var resolver = _customClipResolver ?? _localCatalogResolver;
            if (resolver == null)
            {
                error = AudioOperationResult.Failed(AudioFailureReason.ResolverMissing, "未配置音频资源解析器。");
                return false;
            }

            if (!resolver.TryResolveClip(addressKey, out var clip) || clip == null)
            {
                error = AudioOperationResult.Failed(AudioFailureReason.ClipMissing, $"未找到音频资源：{addressKey}");
                return false;
            }

            resolved = new ResolvedAudioCue
            {
                CueId = definition != null && !string.IsNullOrWhiteSpace(definition.CueId) ? definition.CueId : cueId,
                AddressKey = addressKey,
                Clip = clip,
                Bus = definition != null ? definition.Bus : defaultBus,
                PlayMode = definition != null ? definition.PlayMode : AudioPlayMode.OneShot,
                SpatialMode = definition != null ? definition.SpatialMode : AudioSpatialMode.TwoD,
                Volume = definition != null ? Mathf.Clamp01(definition.Volume) : 1f,
                Pitch = definition != null ? Mathf.Clamp(definition.Pitch, 0.1f, 3f) : 1f,
                RandomPitchRange = definition != null ? Mathf.Clamp01(definition.RandomPitchRange) : 0f,
                Priority = definition != null ? Math.Max(0, definition.Priority) : 128,
                AllowOverlap = definition == null || definition.AllowOverlap
            };

            if (string.IsNullOrWhiteSpace(resolved.CueId))
            {
                resolved.CueId = addressKey;
            }

            return true;
        }

        private static bool IsSameBgm(AudioRuntimePlayback current, ResolvedAudioCue resolved)
        {
            return current != null
                   && string.Equals(current.CueId, resolved.CueId, StringComparison.Ordinal)
                   && string.Equals(current.AddressKey, resolved.AddressKey, StringComparison.Ordinal);
        }

        private void BuildUnitySources(int poolInitialSize, int poolMaxSize)
        {
            if (_root == null)
            {
                return;
            }

            _sourcePool = new AudioSourcePool(_root, poolInitialSize, poolMaxSize);
            _bgmSourceA = CreateDedicatedSource("BgmSource_A", AudioBus.Bgm);
            _bgmSourceB = CreateDedicatedSource("BgmSource_B", AudioBus.Bgm);
            _voiceSource = CreateDedicatedSource("VoiceSource", AudioBus.Voice);
        }

        private AudioSource CreateDedicatedSource(string name, AudioBus bus)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.volume = 0f;
            source.outputAudioMixerGroup = GetMixerGroup(bus);
            return source;
        }

        private AudioSource RentSource(AudioBus bus)
        {
            return _sourcePool?.Rent(bus, GetMixerGroup(bus));
        }

        private void PreparePlaybackSource(AudioSource source, ResolvedAudioCue resolved, Vector3 position, bool use3D, bool loop)
        {
            source.Stop();
            source.clip = resolved.Clip;
            source.loop = loop;
            source.pitch = ResolvePitch(resolved);
            source.spatialBlend = use3D ? 1f : 0f;
            source.transform.position = position;
            source.outputAudioMixerGroup = GetMixerGroup(resolved.Bus);
        }

        private float ResolvePitch(ResolvedAudioCue resolved)
        {
            if (resolved.RandomPitchRange <= 0f)
            {
                return resolved.Pitch;
            }

            return Mathf.Clamp(resolved.Pitch + UnityEngine.Random.Range(-resolved.RandomPitchRange, resolved.RandomPitchRange), 0.1f, 3f);
        }

        private void RegisterPlayback(AudioRuntimePlayback playback)
        {
            if (playback == null)
            {
                return;
            }

            _playbacks.Add(playback);
            if (playback.Handle.IsValid)
            {
                _playbacksByHandle[playback.Handle.HandleId] = playback;
            }
        }

        private void ReleasePlayback(AudioRuntimePlayback playback)
        {
            if (playback == null)
            {
                return;
            }

            _playbacks.Remove(playback);
            if (playback.Handle.IsValid)
            {
                _playbacksByHandle.Remove(playback.Handle.HandleId);
            }

            if (_currentBgm == playback)
            {
                _currentBgm = null;
            }

            if (_currentVoice == playback)
            {
                _currentVoice = null;
            }

            if (!string.IsNullOrWhiteSpace(playback.AmbientChannelId)
                && _ambientByChannel.TryGetValue(playback.AmbientChannelId, out var ambient)
                && ambient == playback)
            {
                _ambientByChannel.Remove(playback.AmbientChannelId);
            }

            if (playback.Source == _bgmSourceA || playback.Source == _bgmSourceB || playback.Source == _voiceSource)
            {
                playback.Source.Stop();
                playback.Source.clip = null;
                playback.Source.volume = 0f;
            }
            else
            {
                _sourcePool?.Release(playback.Source);
            }
        }

        private void ReleaseAllRuntimePlaybacks()
        {
            _removeBuffer.Clear();
            _removeBuffer.AddRange(_playbacks);
            for (var i = 0; i < _removeBuffer.Count; i++)
            {
                ReleasePlayback(_removeBuffer[i]);
            }

            _removeBuffer.Clear();
            _ambientByChannel.Clear();
            _playbacksByHandle.Clear();
            _currentBgm = null;
            _currentVoice = null;
        }

        private void FadeOutPlayback(AudioRuntimePlayback playback, float fadeSeconds)
        {
            if (playback == null)
            {
                return;
            }

            if (fadeSeconds <= 0f)
            {
                ReleasePlayback(playback);
                return;
            }

            playback.FadeSeconds = fadeSeconds;
            playback.TargetVolume = 0f;
            playback.StopWhenSilent = true;
        }

        private void UpdatePlaybackVolume(AudioRuntimePlayback playback, float deltaTime)
        {
            if (playback.FadeSeconds > 0f)
            {
                var step = deltaTime / playback.FadeSeconds;
                playback.CurrentVolume = Mathf.MoveTowards(playback.CurrentVolume, playback.TargetVolume, step);
            }
            else
            {
                playback.CurrentVolume = playback.TargetVolume;
            }

            playback.Source.volume = CalculateFinalVolume(playback);
        }

        private float CalculateFinalVolume(AudioRuntimePlayback playback)
        {
            if (playback == null)
            {
                return 0f;
            }

            var master = GetOrCreateBusSetting(AudioBus.Master);
            var bus = GetOrCreateBusSetting(playback.Bus);
            if (master.Muted || bus.Muted)
            {
                return 0f;
            }

            return Mathf.Clamp01(playback.CueVolume * playback.VolumeScale * playback.CurrentVolume * master.Volume * bus.Volume);
        }

        private void ApplyAllVolumes()
        {
            for (var i = 0; i < _playbacks.Count; i++)
            {
                var playback = _playbacks[i];
                if (playback?.Source != null)
                {
                    playback.Source.volume = CalculateFinalVolume(playback);
                }
            }
        }

        private void ApplyAllMixerGroups()
        {
            for (var i = 0; i < _playbacks.Count; i++)
            {
                var playback = _playbacks[i];
                if (playback?.Source != null)
                {
                    playback.Source.outputAudioMixerGroup = GetMixerGroup(playback.Bus);
                }
            }

            if (_bgmSourceA != null) _bgmSourceA.outputAudioMixerGroup = GetMixerGroup(AudioBus.Bgm);
            if (_bgmSourceB != null) _bgmSourceB.outputAudioMixerGroup = GetMixerGroup(AudioBus.Bgm);
            if (_voiceSource != null) _voiceSource.outputAudioMixerGroup = GetMixerGroup(AudioBus.Voice);
        }

        private bool TryFindActiveCue(string cueId, AudioBus bus, out AudioRuntimePlayback playback)
        {
            for (var i = 0; i < _playbacks.Count; i++)
            {
                playback = _playbacks[i];
                if (playback != null
                    && playback.Bus == bus
                    && string.Equals(playback.CueId, cueId, StringComparison.Ordinal)
                    && playback.Source != null
                    && playback.Source.isPlaying)
                {
                    return true;
                }
            }

            playback = null;
            return false;
        }

        private AudioMixerGroup GetMixerGroup(AudioBus bus)
        {
            return _mixerGroups.TryGetValue(bus, out var group) ? group : null;
        }

        private AudioBusVolumeSnapshot GetOrCreateBusSetting(AudioBus bus)
        {
            if (_busSettings.TryGetValue(bus, out var setting))
            {
                return setting;
            }

            setting = new AudioBusVolumeSnapshot
            {
                Bus = bus,
                Volume = 1f,
                Muted = false
            };
            _busSettings.Add(bus, setting);
            return setting;
        }

        private void InitializeBusSettings()
        {
            _busSettings.Clear();
            var values = (AudioBus[])Enum.GetValues(typeof(AudioBus));
            for (var i = 0; i < values.Length; i++)
            {
                GetOrCreateBusSetting(values[i]);
            }
        }

        private AudioOperationResult ValidateSnapshot(AudioSettingsSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return AudioOperationResult.Failed(AudioFailureReason.ImportInvalid, "音频设置快照为空。");
            }

            if (snapshot.Version != 1)
            {
                return AudioOperationResult.Failed(AudioFailureReason.VersionUnsupported, $"不支持的音频存档版本：{snapshot.Version}");
            }

            if (snapshot.Revision < 0)
            {
                return AudioOperationResult.Failed(AudioFailureReason.DataCorrupted, "音频存档 Revision 不能为负数。");
            }

            if (snapshot.BusVolumes == null)
            {
                return AudioOperationResult.Failed(AudioFailureReason.DataCorrupted, "BusVolumes 为空。");
            }

            var seen = new HashSet<AudioBus>();
            for (var i = 0; i < snapshot.BusVolumes.Length; i++)
            {
                var item = snapshot.BusVolumes[i];
                if (item == null)
                {
                    return AudioOperationResult.Failed(AudioFailureReason.DataCorrupted, "BusVolumes 包含空元素。");
                }

                if (!seen.Add(item.Bus))
                {
                    return AudioOperationResult.Failed(AudioFailureReason.DataCorrupted, $"BusVolumes 包含重复 Bus：{item.Bus}");
                }

                if (item.Volume < 0f || item.Volume > 1f || float.IsNaN(item.Volume))
                {
                    return AudioOperationResult.Failed(AudioFailureReason.DataCorrupted, $"Bus={item.Bus} 的音量非法：{item.Volume}");
                }
            }

            return AudioOperationResult.Success();
        }

        private AudioHandle CreateHandle(string cueId, AudioBus bus)
        {
            if (_nextHandleId == int.MaxValue)
            {
                _nextHandleId = 1;
            }

            return new AudioHandle(_nextHandleId++, cueId, bus);
        }

        private void BumpRevision()
        {
            if (_revision < long.MaxValue)
            {
                _revision++;
            }
        }

        private static void DestroySource(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(source.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(source.gameObject);
            }
        }

        private struct ResolvedAudioCue
        {
            public string CueId;
            public string AddressKey;
            public AudioClip Clip;
            public AudioBus Bus;
            public AudioPlayMode PlayMode;
            public AudioSpatialMode SpatialMode;
            public float Volume;
            public float Pitch;
            public float RandomPitchRange;
            public int Priority;
            public bool AllowOverlap;
        }

        private sealed class AudioRuntimePlayback
        {
            public readonly AudioHandle Handle;
            public readonly string CueId;
            public readonly string AddressKey;
            public readonly AudioBus Bus;
            public readonly AudioSource Source;
            public readonly bool UsesLoop;
            public readonly bool UsesPool;
            public readonly float VolumeScale;
            public readonly float CueVolume;
            public string AmbientChannelId;
            public float CurrentVolume = 1f;
            public float TargetVolume = 1f;
            public float FadeSeconds;
            public bool StopWhenSilent;

            public AudioRuntimePlayback(AudioHandle handle, string cueId, string addressKey, AudioBus bus, AudioSource source, bool usesLoop, bool usesPool, float volumeScale, float cueVolume)
            {
                Handle = handle;
                CueId = cueId;
                AddressKey = addressKey;
                Bus = bus;
                Source = source;
                UsesLoop = usesLoop;
                UsesPool = usesPool;
                VolumeScale = volumeScale;
                CueVolume = Mathf.Clamp01(cueVolume);
            }
        }
    }
}
