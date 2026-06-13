using System;
using NiumaAudio.Data;
using NiumaUI.Toolkit;
using NiumaUI.Toolkit.Common;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace NiumaAudio.ToolkitBridge
{
    public sealed class AudioSettingsToolkitBindingProvider : ToolkitViewBindingProviderBase
    {
        [Serializable] public sealed class BusFloatEvent : UnityEvent<AudioBus, float> { }
        [Serializable] public sealed class BusBoolEvent : UnityEvent<AudioBus, bool> { }

        [Header("元素 name")]
        [SerializeField, Tooltip("标题 Label 的 name。")]
        private string titleLabelName = "TitleText";
        [SerializeField, Tooltip("状态 Label 的 name。")]
        private string statusLabelName = "StatusText";
        [SerializeField, Tooltip("当前 BGM Label 的 name。")]
        private string bgmLabelName = "CurrentBgmText";

        [Header("Bus 控件命名")]
        [SerializeField, Tooltip("音量 Slider 名称后缀。实际 name = Bus名 + 后缀，例如 MasterVolumeSlider。")]
        private string volumeSliderSuffix = "VolumeSlider";
        [SerializeField, Tooltip("静音 Toggle 名称后缀。实际 name = Bus名 + 后缀，例如 MasterMuteToggle。")]
        private string muteToggleSuffix = "MuteToggle";
        [SerializeField, Tooltip("Bus 显示 Label 名称后缀。实际 name = Bus名 + 后缀，例如 MasterValueText。")]
        private string valueLabelSuffix = "ValueText";

        [Header("交互事件")]
        [SerializeField, Tooltip("拖 AudioSettingsToolkitCommandRelay.SetBusVolume。")]
        private BusFloatEvent onVolumeChanged = new BusFloatEvent();
        [SerializeField, Tooltip("拖 AudioSettingsToolkitCommandRelay.SetBusMuted。")]
        private BusBoolEvent onMutedChanged = new BusBoolEvent();
        [SerializeField, Tooltip("拖 AudioSettingsToolkitBridge.RequestRefresh。")]
        private UnityEvent onRefreshRequested = new UnityEvent();

        protected override string DefaultProviderId => "AudioSettingsPanel";

        public override IToolkitViewBinding CreateBinding()
        {
            return new AudioSettingsToolkitBinding(
                titleLabelName,
                statusLabelName,
                bgmLabelName,
                volumeSliderSuffix,
                muteToggleSuffix,
                valueLabelSuffix,
                (bus, value) => onVolumeChanged?.Invoke(bus, value),
                (bus, muted) => onMutedChanged?.Invoke(bus, muted),
                () => onRefreshRequested?.Invoke());
        }
    }

    public sealed class AudioSettingsToolkitBinding : ToolkitViewBindingBase<AudioSettingsUIUpdate, AudioSettingsToolkitViewModel>
    {
        private readonly string _titleName;
        private readonly string _statusName;
        private readonly string _bgmName;
        private readonly string _volumeSuffix;
        private readonly string _muteSuffix;
        private readonly string _valueSuffix;
        private readonly Action<AudioBus, float> _volumeChanged;
        private readonly Action<AudioBus, bool> _mutedChanged;
        private readonly Action _refreshRequested;
        private Label _title;
        private Label _status;
        private Label _bgm;
        private bool _applying;

        public AudioSettingsToolkitBinding(
            string titleName,
            string statusName,
            string bgmName,
            string volumeSuffix,
            string muteSuffix,
            string valueSuffix,
            Action<AudioBus, float> volumeChanged,
            Action<AudioBus, bool> mutedChanged,
            Action refreshRequested)
        {
            _titleName = titleName;
            _statusName = statusName;
            _bgmName = bgmName;
            _volumeSuffix = string.IsNullOrWhiteSpace(volumeSuffix) ? "VolumeSlider" : volumeSuffix.Trim();
            _muteSuffix = string.IsNullOrWhiteSpace(muteSuffix) ? "MuteToggle" : muteSuffix.Trim();
            _valueSuffix = string.IsNullOrWhiteSpace(valueSuffix) ? "ValueText" : valueSuffix.Trim();
            _volumeChanged = volumeChanged;
            _mutedChanged = mutedChanged;
            _refreshRequested = refreshRequested;
        }

        protected override void OnInitializeTyped()
        {
            _title = QLabel(_titleName);
            _status = QLabel(_statusName);
            _bgm = QLabel(_bgmName);
            SetText(_title, "音频设置");

            var buses = (AudioBus[])Enum.GetValues(typeof(AudioBus));
            for (var i = 0; i < buses.Length; i++)
            {
                var bus = buses[i];
                var slider = Query<Slider>(ControlName(bus, _volumeSuffix));
                if (slider != null)
                {
                    slider.lowValue = 0f;
                    slider.highValue = 1f;
                    Callbacks.RegisterValueChanged(slider, value => HandleVolumeChanged(bus, value));
                }

                var toggle = Query<Toggle>(ControlName(bus, _muteSuffix));
                if (toggle != null)
                    Callbacks.RegisterValueChanged(toggle, value => HandleMutedChanged(bus, value));
            }
        }

        protected override void OnRefreshTyped(AudioSettingsUIUpdate viewData, AudioSettingsToolkitViewModel viewModel)
        {
            viewModel.Apply(viewData);
            ApplyVisualState(viewModel.Panel, viewData.UpdateType);
        }

        protected override void OnClearTyped(UIViewModelClearReason reason)
        {
            ApplyVisualState(null, AudioSettingsUIUpdateType.Cleared);
        }

        private void ApplyVisualState(AudioSettingsPanelViewData panel, AudioSettingsUIUpdateType updateType)
        {
            _applying = true;
            try
            {
                SetText(_status, updateType == AudioSettingsUIUpdateType.Cleared ? "音频服务不可用" : $"Revision: {panel?.Revision ?? 0}");
                SetText(_bgm, BuildBgmText(panel));

                var buses = panel?.Buses ?? Array.Empty<AudioBusSettingsViewData>();
                for (var i = 0; i < buses.Length; i++)
                    ApplyBus(buses[i]);
            }
            finally
            {
                _applying = false;
            }
        }

        private void ApplyBus(AudioBusSettingsViewData data)
        {
            if (data == null)
                return;

            var slider = Query<Slider>(ControlName(data.Bus, _volumeSuffix));
            if (slider != null)
                slider.SetValueWithoutNotify(Mathf.Clamp01(data.Volume));

            var toggle = Query<Toggle>(ControlName(data.Bus, _muteSuffix));
            if (toggle != null)
                toggle.SetValueWithoutNotify(data.Muted);

            var label = QLabel(ControlName(data.Bus, _valueSuffix));
            SetText(label, $"{Mathf.RoundToInt(Mathf.Clamp01(data.Volume) * 100f)}%{(data.Muted ? " 静音" : string.Empty)}");
        }

        private void HandleVolumeChanged(AudioBus bus, float value)
        {
            if (_applying)
                return;

            _volumeChanged?.Invoke(bus, Mathf.Clamp01(value));
            _refreshRequested?.Invoke();
        }

        private void HandleMutedChanged(AudioBus bus, bool muted)
        {
            if (_applying)
                return;

            _mutedChanged?.Invoke(bus, muted);
            _refreshRequested?.Invoke();
        }

        private static string BuildBgmText(AudioSettingsPanelViewData panel)
        {
            if (panel == null)
                return "当前 BGM：-";

            if (!string.IsNullOrWhiteSpace(panel.CurrentBgmCueId))
                return $"当前 BGM：{panel.CurrentBgmCueId}";

            if (!string.IsNullOrWhiteSpace(panel.CurrentBgmAddressKey))
                return $"当前 BGM：{panel.CurrentBgmAddressKey}";

            return "当前 BGM：-";
        }

        private static string ControlName(AudioBus bus, string suffix)
        {
            return bus + suffix;
        }
    }
}
