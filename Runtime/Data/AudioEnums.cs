namespace NiumaAudio.Data
{
    /// <summary>
    /// 音频总线。业务模块只关心声音属于哪个总线，不直接管理 AudioSource。
    /// </summary>
    public enum AudioBus
    {
        Master = 0,
        Bgm = 1,
        Sfx = 2,
        UI = 3,
        Ambient = 4,
        Voice = 5
    }

    /// <summary>
    /// 声音播放模式。
    /// </summary>
    public enum AudioPlayMode
    {
        OneShot = 0,
        Loop = 1
    }

    /// <summary>
    /// 声音空间模式。
    /// </summary>
    public enum AudioSpatialMode
    {
        TwoD = 0,
        ThreeD = 1
    }

    /// <summary>
    /// 音频操作失败原因。UI 应按枚举做本地化，不要匹配 Message 字符串。
    /// </summary>
    public enum AudioFailureReason
    {
        None = 0,
        InvalidRequest = 1,
        CueMissing = 2,
        ClipMissing = 3,
        ResolverMissing = 4,
        SourceUnavailable = 5,
        ServiceNotReady = 6,
        ImportInvalid = 7,
        VersionUnsupported = 8,
        DataCorrupted = 9
    }
}
