using System;
using NiumaAudio.Data;
using NiumaUI.Toolkit.Common;

namespace NiumaAudio.ToolkitBridge
{
    public enum AudioSettingsUIUpdateType
    {
        Refresh = 0,
        Cleared = 1
    }

    [Serializable]
    public sealed class AudioBusSettingsViewData
    {
        public AudioBus Bus;
        public string DisplayName;
        public float Volume;
        public bool Muted;
    }

    [Serializable]
    public sealed class AudioSettingsPanelViewData
    {
        public long Revision;
        public string CurrentBgmCueId;
        public string CurrentBgmAddressKey;
        public AudioBusSettingsViewData[] Buses = Array.Empty<AudioBusSettingsViewData>();
    }

    public readonly struct AudioSettingsUIUpdate
    {
        public readonly AudioSettingsUIUpdateType UpdateType;
        public readonly long Revision;
        public readonly AudioSettingsPanelViewData PanelData;
        public readonly AudioSettingsPanelViewData PreviousPanelData;

        public AudioSettingsUIUpdate(AudioSettingsUIUpdateType updateType, long revision, AudioSettingsPanelViewData panelData, AudioSettingsPanelViewData previousPanelData)
        {
            UpdateType = updateType;
            Revision = revision;
            PanelData = panelData;
            PreviousPanelData = previousPanelData;
        }
    }

    public sealed class AudioSettingsToolkitViewModel : UIPanelViewModelBase
    {
        public AudioSettingsPanelViewData Panel { get; private set; }
        public AudioSettingsUIUpdateType UpdateType { get; private set; }

        public void Apply(AudioSettingsUIUpdate update)
        {
            SetContext("audio_settings");
            Panel = update.PanelData;
            UpdateType = update.UpdateType;
            MarkDirty();
        }

        protected override void OnClear(UIViewModelClearReason reason)
        {
            Panel = null;
            UpdateType = AudioSettingsUIUpdateType.Cleared;
        }
    }
}
