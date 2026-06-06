using NiumaAudio.Controller;
using NiumaAudio.Service;
using NiumaCore.Module;
using UnityEngine;

namespace NiumaAudio.Bridge
{
    /// <summary>
    /// 音频桥接解析工具。
    /// 优先使用外部注入的 IAudioCommand，其次使用 GameContext，最后才查找场景中的 NiumaAudioController。
    /// </summary>
    public static class AudioBridgeResolver
    {
        public static bool TryResolveCommand(
            IAudioCommand injectedCommand,
            GameContext context,
            NiumaAudioController explicitController,
            bool autoFindAudioController,
            out IAudioCommand command,
            out NiumaAudioController resolvedController)
        {
            resolvedController = explicitController;

            if (injectedCommand != null)
            {
                command = injectedCommand;
                return true;
            }

            if (context != null && context.TryGetService(out command) && command != null)
            {
                return true;
            }

            if (explicitController != null && explicitController.AudioCommand != null)
            {
                command = explicitController.AudioCommand;
                return true;
            }

            if (autoFindAudioController)
            {
#if UNITY_2023_1_OR_NEWER
                resolvedController = Object.FindFirstObjectByType<NiumaAudioController>();
#else
                resolvedController = Object.FindObjectOfType<NiumaAudioController>();
#endif
                if (resolvedController != null && resolvedController.AudioCommand != null)
                {
                    command = resolvedController.AudioCommand;
                    return true;
                }
            }

            command = null;
            return false;
        }
    }
}
