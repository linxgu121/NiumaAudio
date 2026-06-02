using System;

namespace NiumaAudio.Data
{
    /// <summary>
    /// 音频播放句柄。句柄只用于查询或停止，不应该写入存档。
    /// </summary>
    [Serializable]
    public readonly struct AudioHandle
    {
        public readonly int HandleId;
        public readonly string CueId;
        public readonly AudioBus Bus;
        public readonly bool IsValid;

        public AudioHandle(int handleId, string cueId, AudioBus bus)
        {
            HandleId = handleId;
            CueId = cueId;
            Bus = bus;
            IsValid = handleId > 0;
        }

        public static AudioHandle Invalid => default;
    }

    /// <summary>
    /// 音频操作结果。业务失败不抛异常，用结构化结果返回。
    /// </summary>
    [Serializable]
    public sealed class AudioOperationResult
    {
        public bool Succeeded;
        public AudioFailureReason FailureReason;
        public string Message;
        public AudioHandle Handle;

        public static AudioOperationResult Success(AudioHandle handle, string message = null)
        {
            return new AudioOperationResult
            {
                Succeeded = true,
                FailureReason = AudioFailureReason.None,
                Message = message,
                Handle = handle
            };
        }

        public static AudioOperationResult Success(string message = null)
        {
            return Success(AudioHandle.Invalid, message);
        }

        public static AudioOperationResult Failed(AudioFailureReason reason, string message = null)
        {
            return new AudioOperationResult
            {
                Succeeded = false,
                FailureReason = reason,
                Message = message,
                Handle = AudioHandle.Invalid
            };
        }
    }
}
