using System;
using System.Collections.Generic;
using System.Text;
using NiumaAudio.Controller;
using NiumaAudio.Data;
using NiumaSave.Controller;
using NiumaSave.Data;
using NiumaSave.Provider;
using UnityEngine;

namespace NiumaAudio.SaveBridge
{
    /// <summary>
    /// NiumaAudio 存档适配器。
    /// 只保存音频设置和可恢复的当前 BGM，不保存一次性音效、语音播放进度、环境音句柄或 AudioSource 池状态。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NiumaAudioSaveAdapter : MonoBehaviour, ISaveDataProvider
    {
        private const string AudioSectionId = "audio";
        private const string AudioSectionVersionV1 = "1";
        private const string CurrentAudioSectionVersion = AudioSectionVersionV1;
        private const string AudioSectionFormat = "json";

        [Header("模块引用")]
        [Tooltip("音频模块根控制器。请拖入场景中的 NiumaAudioController，导出和导入音频设置都通过它完成。")]
        [SerializeField] private NiumaAudioController audioController;

        [Tooltip("存档模块根控制器。开启自动注册时，请拖入场景中的 NiumaSaveController。")]
        [SerializeField] private NiumaSaveController saveController;

        [Header("注册行为")]
        [Tooltip("启用组件时是否自动注册到 NiumaSaveController。正式场景建议开启，并确保 NiumaSaveController 更早初始化。")]
        [SerializeField] private bool registerOnEnable = true;

        [Tooltip("引用为空时是否自动在场景中查找控制器。仅建议调试阶段开启；正式多场景或全局场景应手动绑定，避免找到错误实例。")]
        [SerializeField] private bool autoFindReferences = true;

        private bool _registeredToSaveController;

        /// <summary>音频模块稳定存档段 ID。</summary>
        public string SectionId => AudioSectionId;

        /// <summary>音频模块存档段结构版本。</summary>
        public string SectionVersion => CurrentAudioSectionVersion;

        /// <summary>音频设置修订号。瞬时 SFX 不会推动该值。</summary>
        public long Revision => audioController != null ? audioController.AudioRevision : 0L;

        private void Awake()
        {
            ResolveReferences(false);
        }

        private void OnEnable()
        {
            if (registerOnEnable)
            {
                RegisterToSaveController();
            }
        }

        private void OnDisable()
        {
            UnregisterFromSaveController();
        }

        /// <summary>
        /// 导出音频设置为 NiumaSave Section。
        /// SaveDataProviderRegistry 会捕获该方法抛出的异常并转为结构化导出失败；直接调用时必须自行处理 InvalidOperationException。
        /// </summary>
        public SaveSectionData ExportSection()
        {
            ResolveReferences(false);
            if (audioController == null)
            {
                throw new InvalidOperationException("NiumaAudioSaveAdapter 缺少 NiumaAudioController，无法导出音频设置。");
            }

            if (!audioController.IsInitialized)
            {
                throw new InvalidOperationException("NiumaAudioController 尚未初始化，拒绝导出空音频设置以避免覆盖有效数据。");
            }

            var snapshot = audioController.ExportSnapshot();
            ValidateSnapshotForExport(snapshot);

            var json = JsonUtility.ToJson(snapshot);
            var bytes = Encoding.UTF8.GetBytes(json);

            return new SaveSectionData
            {
                SectionId = SectionId,
                SectionVersion = SectionVersion,
                Format = AudioSectionFormat,
                DataEncoding = SaveDataEncoding.Base64,
                EncodedData = Convert.ToBase64String(bytes)
            };
        }

        /// <summary>
        /// 从 NiumaSave Section 导入音频设置。
        /// 损坏、版本不兼容或结构非法的数据不会清空当前音频设置。
        /// </summary>
        public SaveSectionImportResult ImportSection(SaveSectionData section)
        {
            ResolveReferences(false);
            if (audioController == null)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.ConfigMissing,
                    "NiumaAudioSaveAdapter 缺少 NiumaAudioController，无法导入音频设置。");
            }

            if (section == null)
            {
                return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.NullSection, "音频存档段为空。");
            }

            if (!string.Equals(section.SectionId, SectionId, StringComparison.Ordinal))
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.SectionIdMismatch,
                    $"音频存档段 ID 不匹配：expected={SectionId}, actual={section.SectionId}");
            }

            if (!string.Equals(section.Format, AudioSectionFormat, StringComparison.Ordinal))
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"音频存档段格式不支持：{section.Format}");
            }

            if (!string.Equals(section.DataEncoding, SaveDataEncoding.Base64, StringComparison.Ordinal))
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"音频存档段编码不支持：{section.DataEncoding}");
            }

            if (string.IsNullOrWhiteSpace(section.EncodedData))
            {
                return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, "音频存档段数据为空。");
            }

            try
            {
                var readResult = TryReadAudioSettings(section, out var snapshot);
                if (!readResult.Succeeded)
                {
                    return readResult;
                }

                var importResult = audioController.ImportSnapshot(snapshot);
                if (importResult == null || !importResult.Succeeded)
                {
                    return SaveSectionImportResult.Fail(
                        SaveSectionImportErrorCode.ImportFailed,
                        importResult != null ? importResult.Message : "音频控制器导入结果为空。");
                }

                return SaveSectionImportResult.Success();
            }
            catch (Exception ex)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.Unknown,
                    $"音频存档段导入异常：{ex.Message}");
            }
        }

        private static SaveSectionImportResult TryReadAudioSettings(SaveSectionData section, out AudioSettingsSnapshot snapshot)
        {
            snapshot = null;
            switch (section.SectionVersion)
            {
                case AudioSectionVersionV1:
                    return TryReadVersion1(section, out snapshot);
                default:
                    return SaveSectionImportResult.Fail(
                        SaveSectionImportErrorCode.VersionUnsupported,
                        $"音频存档段版本不支持：{section.SectionVersion}");
            }
        }

        private static SaveSectionImportResult TryReadVersion1(SaveSectionData section, out AudioSettingsSnapshot snapshot)
        {
            snapshot = null;
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(section.EncodedData);
            }
            catch (FormatException ex)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"音频存档段 Base64 解码失败：{ex.Message}");
            }

            string json;
            try
            {
                json = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"音频存档段 UTF8 解码失败：{ex.Message}");
            }

            try
            {
                snapshot = JsonUtility.FromJson<AudioSettingsSnapshot>(json);
            }
            catch (ArgumentException ex)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"音频存档段 Json 解析失败：{ex.Message}");
            }

            return ValidateImportedSnapshot(snapshot);
        }

        [ContextMenu("NiumaAudioSave/注册到存档模块")]
        private void RegisterToSaveController()
        {
            if (_registeredToSaveController)
            {
                return;
            }

            ResolveReferences(true);
            if (saveController == null)
            {
                return;
            }

            var registered = saveController.RegisterProvider(this);
            _registeredToSaveController = registered;
            if (!registered)
            {
                Debug.LogWarning("[NiumaAudioSaveAdapter] 注册音频存档 Provider 失败。", this);
            }
        }

        [ContextMenu("NiumaAudioSave/从存档模块取消注册")]
        private void UnregisterFromSaveController()
        {
            ResolveReferences(false);
            if (_registeredToSaveController && saveController != null)
            {
                saveController.UnregisterProvider(SectionId);
            }

            _registeredToSaveController = false;
        }

        private void ResolveReferences(bool logMissing)
        {
            if (!autoFindReferences)
            {
                return;
            }

            if (audioController == null)
            {
#if UNITY_2023_1_OR_NEWER
                audioController = FindFirstObjectByType<NiumaAudioController>();
#else
                audioController = FindObjectOfType<NiumaAudioController>();
#endif
            }

            if (saveController == null)
            {
#if UNITY_2023_1_OR_NEWER
                saveController = FindFirstObjectByType<NiumaSaveController>();
#else
                saveController = FindObjectOfType<NiumaSaveController>();
#endif
            }

            if (logMissing)
            {
                if (audioController == null)
                {
                    Debug.LogWarning("[NiumaAudioSaveAdapter] 未找到 NiumaAudioController，请手动绑定。", this);
                }

                if (saveController == null)
                {
                    Debug.LogWarning("[NiumaAudioSaveAdapter] 未找到 NiumaSaveController，请手动绑定。", this);
                }
            }
        }

        private static void ValidateSnapshotForExport(AudioSettingsSnapshot snapshot)
        {
            var error = ValidateSnapshot(snapshot);
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException($"音频存档导出数据无效：{error}");
            }
        }

        private static SaveSectionImportResult ValidateImportedSnapshot(AudioSettingsSnapshot snapshot)
        {
            var error = ValidateSnapshot(snapshot);
            return string.IsNullOrWhiteSpace(error)
                ? SaveSectionImportResult.Success()
                : SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, error);
        }

        private static string ValidateSnapshot(AudioSettingsSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "AudioSettingsSnapshot 为空。";
            }

            if (snapshot.Version != 1)
            {
                return $"Version 不支持：{snapshot.Version}";
            }

            if (snapshot.Revision < 0)
            {
                return "Revision 不能为负数。";
            }

            if (snapshot.BusVolumes == null)
            {
                return "BusVolumes 为空。";
            }

            var seen = new HashSet<AudioBus>();
            for (var i = 0; i < snapshot.BusVolumes.Length; i++)
            {
                var item = snapshot.BusVolumes[i];
                if (item == null)
                {
                    return $"BusVolumes[{i}] 为空。";
                }

                if (!seen.Add(item.Bus))
                {
                    return $"BusVolumes 包含重复 Bus：{item.Bus}";
                }

                if (float.IsNaN(item.Volume) || float.IsInfinity(item.Volume) || item.Volume < 0f || item.Volume > 1f)
                {
                    return $"Bus={item.Bus} 的音量非法：{item.Volume}";
                }
            }

            return null;
        }
    }
}
