using System;

namespace NiumaAudio.Data
{
    /// <summary>
    /// 单个音频总线的设置快照。
    /// </summary>
    [Serializable]
    public sealed class AudioBusVolumeSnapshot
    {
        public AudioBus Bus;
        public float Volume = 1f;
        public bool Muted;

        public AudioBusVolumeSnapshot Clone()
        {
            return (AudioBusVolumeSnapshot)MemberwiseClone();
        }

        public static AudioBusVolumeSnapshot[] CloneArray(AudioBusVolumeSnapshot[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<AudioBusVolumeSnapshot>();
            }

            var result = new AudioBusVolumeSnapshot[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = source[i]?.Clone();
            }

            return result;
        }
    }

    /// <summary>
    /// 音频设置存档快照。只保存设置和可恢复 BGM，不保存瞬时音效或 AudioSource 状态。
    /// </summary>
    [Serializable]
    public sealed class AudioSettingsSnapshot
    {
        public int Version = 1;
        public long Revision;
        public AudioBusVolumeSnapshot[] BusVolumes = Array.Empty<AudioBusVolumeSnapshot>();
        public string CurrentBgmCueId;
        public string CurrentBgmAddressKey;

        public AudioSettingsSnapshot Clone()
        {
            return new AudioSettingsSnapshot
            {
                Version = Version,
                Revision = Revision,
                BusVolumes = AudioBusVolumeSnapshot.CloneArray(BusVolumes),
                CurrentBgmCueId = CurrentBgmCueId,
                CurrentBgmAddressKey = CurrentBgmAddressKey
            };
        }
    }
}
