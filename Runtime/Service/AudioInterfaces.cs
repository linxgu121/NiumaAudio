using NiumaAudio.Config;
using NiumaAudio.Data;
using UnityEngine;

namespace NiumaAudio.Service
{
    /// <summary>
    /// 音频查询接口。只提供读取能力，不暴露存档导出。
    /// </summary>
    public interface IAudioQuery
    {
        long Revision { get; }
        string CurrentBgmCueId { get; }
        string CurrentBgmAddressKey { get; }
        float GetVolume(AudioBus bus);
        bool IsMuted(AudioBus bus);
        bool IsPlaying(AudioHandle handle);
    }

    /// <summary>
    /// 音频命令接口。所有会改变音频状态或设置的入口都从这里调用。
    /// </summary>
    public interface IAudioCommand
    {
        AudioOperationResult PlayBgm(AudioBgmRequest request);
        AudioOperationResult StopBgm(float fadeSeconds = 1f);
        AudioOperationResult PlayCue(AudioPlayRequest request);
        AudioOperationResult PlayCue3D(AudioPlayRequest request);
        AudioOperationResult PlayVoice(AudioPlayRequest request);
        AudioOperationResult StopVoice(float fadeSeconds = 0f);
        AudioOperationResult PlayAmbient(AudioAmbientRequest request);
        AudioOperationResult StopAmbient(string channelId, float fadeSeconds = 1f);
        AudioOperationResult SetVolume(AudioBus bus, float volume);
        AudioOperationResult SetMuted(AudioBus bus, bool muted);
        AudioOperationResult ImportSnapshot(AudioSettingsSnapshot snapshot);
    }

    /// <summary>
    /// 音频服务门面。存档导出放在组合服务上，避免污染纯查询接口。
    /// </summary>
    public interface IAudioService : IAudioQuery, IAudioCommand
    {
        AudioSettingsSnapshot ExportSnapshot();
    }

    /// <summary>
    /// 音频配置能力接口。Controller 热更新配置时使用，普通业务模块不应依赖它。
    /// </summary>
    public interface IAudioConfigurationService
    {
        void SetCueDefinitions(AudioCueDefinition[] definitions);
        void SetAudioCatalog(AudioCatalogDefinition catalog);
        void SetMixerGroups(AudioMixerGroupConfig[] mixerGroups);
        void SetClipResolver(IAudioClipResolver resolver);
    }

    /// <summary>
    /// 音频资源解析器。第一版由本地 Catalog 实现，后续可替换为 Addressables 或热更新加载器。
    /// </summary>
    public interface IAudioClipResolver
    {
        bool TryResolveClip(string addressKey, out AudioClip clip);
    }
}
