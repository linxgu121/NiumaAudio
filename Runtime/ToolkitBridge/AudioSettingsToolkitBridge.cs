using System;
using NiumaAudio.Controller;
using NiumaAudio.Data;
using NiumaUI.Toolkit;
using UnityEngine;

namespace NiumaAudio.ToolkitBridge
{
    public sealed class AudioSettingsToolkitBridge : MonoBehaviour
    {
        [Header("模块引用")]
        [Tooltip("拖全局 AudioRoot 上的 NiumaAudioController。为空时会自动查找。")]
        [SerializeField] private NiumaAudioController audioController;

        [Tooltip("拖核心场景或当前场景中的 UIToolkitUIManager。为空时会自动查找。")]
        [SerializeField] private UIToolkitUIManager uiManager;

        [Header("View")]
        [Tooltip("音频设置面板 ViewId，需要在 UIToolkitViewRegistrySO 中注册。")]
        [SerializeField] private string audioSettingsViewId = "AudioSettingsPanel";

        [Tooltip("启用时是否立即刷新并打开面板。")]
        [SerializeField] private bool refreshOnEnable;

        [Tooltip("是否在 LateUpdate 中按 AudioRevision 自动刷新已打开面板。")]
        [SerializeField] private bool refreshInLateUpdate = true;

        [Tooltip("服务不可用时是否关闭面板。")]
        [SerializeField] private bool closeWhenCleared = true;

        [Tooltip("RefreshView 失败时是否自动 OpenView。")]
        [SerializeField] private bool autoOpenView = true;

        [Header("日志")]
        [SerializeField] private bool logWarnings = true;

        private long _observedRevision = -1L;
        private AudioSettingsPanelViewData _lastPanelData;
        private bool _refreshRequested;

        private void OnEnable()
        {
            ResolveReferences(false);
            _observedRevision = -1L;
            if (refreshOnEnable)
                RefreshAudioSettings();
        }

        private void OnDisable()
        {
            _refreshRequested = false;
        }

        private void LateUpdate()
        {
            if (_refreshRequested)
            {
                _refreshRequested = false;
                RefreshAudioSettings();
                return;
            }

            if (!refreshInLateUpdate || !EnsureController(false))
                return;

            if (_observedRevision == audioController.AudioRevision)
                return;

            RefreshAudioSettings();
        }

        public void RequestRefresh()
        {
            _refreshRequested = true;
        }

        public void CloseAudioSettings()
        {
            _refreshRequested = false;
            if (EnsureUIManager(true))
                uiManager.CloseView(audioSettingsViewId);
        }

        public void RefreshAudioSettings()
        {
            if (!EnsureController(true) || !EnsureUIManager(true))
            {
                ApplyClearUpdate();
                return;
            }

            var revision = audioController.AudioRevision;
            var panel = BuildPanelViewData(revision);
            _observedRevision = revision;
            var update = new AudioSettingsUIUpdate(AudioSettingsUIUpdateType.Refresh, revision, panel, _lastPanelData);
            if (!uiManager.RefreshView(audioSettingsViewId, update) && autoOpenView)
                uiManager.OpenView(audioSettingsViewId, update);
            _lastPanelData = panel;
        }

        private AudioSettingsPanelViewData BuildPanelViewData(long revision)
        {
            var values = (AudioBus[])Enum.GetValues(typeof(AudioBus));
            var buses = new AudioBusSettingsViewData[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var bus = values[i];
                buses[i] = new AudioBusSettingsViewData
                {
                    Bus = bus,
                    DisplayName = bus.ToString(),
                    Volume = Mathf.Clamp01(audioController.GetVolume(bus)),
                    Muted = audioController.IsMuted(bus)
                };
            }

            return new AudioSettingsPanelViewData
            {
                Revision = revision,
                CurrentBgmCueId = audioController.CurrentBgmCueId,
                CurrentBgmAddressKey = audioController.CurrentBgmAddressKey,
                Buses = buses
            };
        }

        private void ApplyClearUpdate()
        {
            _observedRevision = -1L;
            if (closeWhenCleared && uiManager != null)
            {
                uiManager.CloseView(audioSettingsViewId);
                return;
            }

            if (uiManager != null)
            {
                var update = new AudioSettingsUIUpdate(AudioSettingsUIUpdateType.Cleared, 0L, null, _lastPanelData);
                if (!uiManager.RefreshView(audioSettingsViewId, update) && autoOpenView)
                    uiManager.OpenView(audioSettingsViewId, update);
            }
        }

        private bool EnsureController(bool logMissing)
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
            Warn("未找到 NiumaAudioController。请把 AudioRoot 上的 NiumaAudioController 拖进来。", logMissing);
            return false;
        }

        private bool EnsureUIManager(bool logMissing)
        {
            if (uiManager != null)
                return true;
#if UNITY_2023_1_OR_NEWER
            uiManager = FindFirstObjectByType<UIToolkitUIManager>();
#else
            uiManager = FindObjectOfType<UIToolkitUIManager>();
#endif
            if (uiManager != null)
                return true;
            Warn("未找到 UIToolkitUIManager。请把核心场景 UIRoot 上的 UIToolkitUIManager 拖进来。", logMissing);
            return false;
        }

        private void ResolveReferences(bool logMissing)
        {
            EnsureController(logMissing);
            EnsureUIManager(logMissing);
        }

        private void Warn(string message, bool log)
        {
            if (logWarnings && log)
                Debug.LogWarning($"[AudioSettingsToolkitBridge] {message}", this);
        }
    }
}
