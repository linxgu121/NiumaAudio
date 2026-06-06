using System;
using System.Collections.Generic;
using NiumaAudio.Config;
using NiumaAudio.Data;
using NiumaAudio.Service;
using UnityEngine;

namespace NiumaAudio.Debugging
{
    /// <summary>
    /// NiumaAudio 基础测试入口。
    /// 该组件只用于开发阶段在 Unity 场景内手动验证音频核心服务，不参与正式业务。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioBasicTestRunner : MonoBehaviour
    {
        private const string BgmCueId = "test_bgm";
        private const string BgmAddressKey = "audio/test_bgm";
        private const string SfxCueId = "test_sfx";
        private const string SfxAddressKey = "audio/test_sfx";
        private const string VoiceCueId = "test_voice";
        private const string VoiceAddressKey = "audio/test_voice";
        private const string AmbientCueId = "test_ambient";
        private const string AmbientAddressKey = "audio/test_ambient";
        private const string AmbientChannelId = "test_channel";
        private const string ThreeDCueId = "test_sfx_3d";
        private const string NoOverlapCueId = "test_sfx_no_overlap";
        private const string RandomPitchCueId = "test_sfx_random_pitch";

        [Header("测试行为")]
        [Tooltip("运行测试后是否在 Console 输出每一条通过信息。关闭后只输出最终结果和失败原因。")]
        [SerializeField] private bool verboseLog = true;

        [Header("最近一次结果")]
        [Tooltip("最近一次基础测试是否全部通过。")]
        [SerializeField] private bool lastRunSucceeded;

        [Tooltip("最近一次通过的检查数量。")]
        [SerializeField] private int passedCheckCount;

        [Tooltip("最近一次失败的检查数量。")]
        [SerializeField] private int failedCheckCount;

        [Tooltip("最近一次测试报告。")]
        [TextArea(8, 24)]
        [SerializeField] private string lastReport;

        private readonly List<string> _reportLines = new List<string>();
        private readonly List<UnityEngine.Object> _createdObjects = new List<UnityEngine.Object>();

        /// <summary>
        /// 运行第 6 阶段基础测试。
        /// </summary>
        [ContextMenu("NiumaAudioTest/运行基础测试")]
        public void RunBasicTests()
        {
            ResetReport();

            RunCase("Catalog Key 能解析并播放 Cue", TestCatalogResolveAndPlayCue);
            RunCase("3D Cue 播放路径可用", TestPlayCue3D);
            RunCase("AllowOverlap 关闭时复用当前播放", TestAllowOverlapDisabledReusesActivePlayback);
            RunCase("随机音高 Cue 可稳定播放", TestRandomPitchCuePlayback);
            RunCase("缺失 Key 返回结构化失败", TestMissingKeyFails);
            RunCase("BGM 播放、重复播放和停止的状态稳定", TestBgmLifecycle);
            RunCase("Bus 音量与静音设置可保存", TestVolumeAndMute);
            RunCase("Voice 播放后可停止", TestVoiceStop);
            RunCase("Ambient 按 Channel 替换和停止", TestAmbientChannel);
            RunCase("导出导入后设置与当前 BGM 一致", TestExportImportSettings);
            RunCase("非法快照导入失败且不污染当前设置", TestInvalidImportRejected);
            RunCase("配置热更新可替换 Cue 与 Catalog", TestConfigurationHotUpdate);

            lastRunSucceeded = failedCheckCount == 0;
            lastReport = string.Join(Environment.NewLine, _reportLines);

            var summary = $"[NiumaAudioTest] 基础测试结束：Passed={passedCheckCount}, Failed={failedCheckCount}";
            if (lastRunSucceeded)
            {
                UnityEngine.Debug.Log(summary, this);
            }
            else
            {
                UnityEngine.Debug.LogError(summary + Environment.NewLine + lastReport, this);
            }

            ReleaseCreatedObjects();
        }

        /// <summary>
        /// 清空最近一次测试报告。
        /// </summary>
        [ContextMenu("NiumaAudioTest/清空测试报告")]
        public void ClearReport()
        {
            lastRunSucceeded = false;
            passedCheckCount = 0;
            failedCheckCount = 0;
            lastReport = string.Empty;
            _reportLines.Clear();
        }

        private void TestCatalogResolveAndPlayCue()
        {
            var service = CreateService();

            var result = service.PlayCue(new AudioPlayRequest
            {
                CueId = SfxCueId,
                SourceModule = nameof(AudioBasicTestRunner)
            });

            ExpectSuccess("通过 CueId 播放 SFX 成功", result);
            Expect(result.Handle.IsValid, "播放成功返回有效 Handle");
            ExpectEqual(AudioBus.Sfx, result.Handle.Bus, "SFX Handle Bus 正确");
            ExpectEqual(0L, service.Revision, "瞬时 SFX 不递增 Revision");

            service.Dispose();
        }

        private void TestMissingKeyFails()
        {
            var service = CreateService();
            var result = service.PlayCue(new AudioPlayRequest
            {
                AddressKey = "audio/missing",
                SourceModule = nameof(AudioBasicTestRunner)
            });

            ExpectFailure("缺失 AddressKey 播放失败", result, AudioFailureReason.ClipMissing);
            ExpectEqual(0L, service.Revision, "播放失败不递增 Revision");

            service.Dispose();
        }

        private void TestPlayCue3D()
        {
            var service = CreateService();

            var result = service.PlayCue3D(new AudioPlayRequest
            {
                CueId = ThreeDCueId,
                Position = new Vector3(1f, 2f, 3f),
                HasPosition = true,
                SourceModule = nameof(AudioBasicTestRunner)
            });

            ExpectSuccess("通过 CueId 播放 3D SFX 成功", result);
            Expect(result.Handle.IsValid, "3D SFX 返回有效 Handle");
            ExpectEqual(AudioBus.Sfx, result.Handle.Bus, "3D SFX Handle Bus 正确");

            service.Dispose();
        }

        private void TestAllowOverlapDisabledReusesActivePlayback()
        {
            var service = CreateService();

            var first = service.PlayCue(new AudioPlayRequest
            {
                CueId = NoOverlapCueId,
                SourceModule = nameof(AudioBasicTestRunner)
            });
            ExpectSuccess("AllowOverlap=false 的 Cue 第一次播放成功", first);

            var second = service.PlayCue(new AudioPlayRequest
            {
                CueId = NoOverlapCueId,
                SourceModule = nameof(AudioBasicTestRunner)
            });
            ExpectSuccess("AllowOverlap=false 的 Cue 重复播放返回成功", second);
            ExpectEqual(first.Handle.HandleId, second.Handle.HandleId, "AllowOverlap=false 时复用正在播放的 Handle");

            service.Dispose();
        }

        private void TestRandomPitchCuePlayback()
        {
            var service = CreateService();

            var result = service.PlayCue(new AudioPlayRequest
            {
                CueId = RandomPitchCueId,
                SourceModule = nameof(AudioBasicTestRunner)
            });

            ExpectSuccess("随机音高 Cue 播放成功", result);
            Expect(result.Handle.IsValid, "随机音高 Cue 返回有效 Handle");

            service.Dispose();
        }

        private void TestBgmLifecycle()
        {
            var service = CreateService();

            var play = service.PlayBgm(new AudioBgmRequest
            {
                CueId = BgmCueId,
                FadeSeconds = 0f,
                SourceModule = nameof(AudioBasicTestRunner)
            });
            ExpectSuccess("播放 BGM 成功", play);
            ExpectEqual(BgmCueId, service.CurrentBgmCueId, "当前 BGM CueId 正确");
            ExpectEqual(BgmAddressKey, service.CurrentBgmAddressKey, "当前 BGM AddressKey 正确");
            ExpectEqual(1L, service.Revision, "播放 BGM 递增 Revision");

            var duplicate = service.PlayBgm(new AudioBgmRequest
            {
                CueId = BgmCueId,
                FadeSeconds = 0f,
                RestartIfSame = false,
                SourceModule = nameof(AudioBasicTestRunner)
            });
            ExpectSuccess("重复播放同一 BGM 返回成功", duplicate);
            ExpectEqual(1L, service.Revision, "不重启同一 BGM 不重复递增 Revision");

            var stop = service.StopBgm(0f);
            ExpectSuccess("停止 BGM 成功", stop);
            Expect(string.IsNullOrEmpty(service.CurrentBgmCueId), "停止 BGM 后 CueId 清空");
            Expect(string.IsNullOrEmpty(service.CurrentBgmAddressKey), "停止 BGM 后 AddressKey 清空");
            ExpectEqual(2L, service.Revision, "停止 BGM 递增 Revision");

            service.Dispose();
        }

        private void TestVolumeAndMute()
        {
            var service = CreateService();

            ExpectSuccess("设置 BGM 音量成功", service.SetVolume(AudioBus.Bgm, 0.35f));
            ExpectApproximately(0.35f, service.GetVolume(AudioBus.Bgm), "BGM 音量写入成功");
            ExpectEqual(1L, service.Revision, "音量变化递增 Revision");

            ExpectSuccess("重复设置相同音量成功", service.SetVolume(AudioBus.Bgm, 0.35f));
            ExpectEqual(1L, service.Revision, "音量未变化不递增 Revision");

            ExpectSuccess("设置 Master 静音成功", service.SetMuted(AudioBus.Master, true));
            Expect(service.IsMuted(AudioBus.Master), "Master 静音状态写入成功");
            ExpectEqual(2L, service.Revision, "静音变化递增 Revision");

            service.Dispose();
        }

        private void TestVoiceStop()
        {
            var service = CreateService();

            var play = service.PlayVoice(new AudioPlayRequest
            {
                CueId = VoiceCueId,
                SourceModule = nameof(AudioBasicTestRunner)
            });
            ExpectSuccess("播放 Voice 成功", play);
            Expect(play.Handle.IsValid, "Voice 返回有效 Handle");

            var stop = service.StopVoice(0f);
            ExpectSuccess("停止 Voice 成功", stop);
            Expect(!service.IsPlaying(play.Handle), "停止 Voice 后 Handle 不再播放");
            ExpectEqual(0L, service.Revision, "Voice 是瞬时表现，不递增 Revision");

            service.Dispose();
        }

        private void TestAmbientChannel()
        {
            var service = CreateService();

            var first = service.PlayAmbient(new AudioAmbientRequest
            {
                ChannelId = AmbientChannelId,
                CueId = AmbientCueId,
                FadeSeconds = 0f,
                SourceModule = nameof(AudioBasicTestRunner)
            });
            ExpectSuccess("播放 Ambient 成功", first);
            Expect(first.Handle.IsValid, "Ambient 返回有效 Handle");

            var second = service.PlayAmbient(new AudioAmbientRequest
            {
                ChannelId = AmbientChannelId,
                CueId = AmbientCueId,
                FadeSeconds = 0f,
                SourceModule = nameof(AudioBasicTestRunner)
            });
            ExpectSuccess("同 Channel 替换 Ambient 成功", second);
            Expect(!service.IsPlaying(first.Handle), "同 Channel 新环境音替换旧环境音");

            var stop = service.StopAmbient(AmbientChannelId, 0f);
            ExpectSuccess("停止 Ambient 成功", stop);
            Expect(!service.IsPlaying(second.Handle), "停止 Ambient 后 Handle 不再播放");
            ExpectEqual(0L, service.Revision, "Ambient 是场景表现，不递增 Revision");

            service.Dispose();
        }

        private void TestExportImportSettings()
        {
            var source = CreateService();
            ExpectSuccess("设置 Voice 音量", source.SetVolume(AudioBus.Voice, 0.2f));
            ExpectSuccess("设置 UI 静音", source.SetMuted(AudioBus.UI, true));
            ExpectSuccess("播放 BGM", source.PlayBgm(new AudioBgmRequest
            {
                CueId = BgmCueId,
                FadeSeconds = 0f,
                SourceModule = nameof(AudioBasicTestRunner)
            }));

            var snapshot = source.ExportSnapshot();
            var restored = CreateService();
            ExpectSuccess("导入音频设置快照", restored.ImportSnapshot(snapshot));

            ExpectEqual(snapshot.Revision, restored.Revision, "导入后 Revision 继承快照");
            ExpectApproximately(0.2f, restored.GetVolume(AudioBus.Voice), "导入后 Voice 音量一致");
            Expect(restored.IsMuted(AudioBus.UI), "导入后 UI 静音一致");
            ExpectEqual(BgmCueId, restored.CurrentBgmCueId, "导入后 CurrentBgmCueId 一致");
            ExpectEqual(BgmAddressKey, restored.CurrentBgmAddressKey, "导入后 CurrentBgmAddressKey 一致");

            source.Dispose();
            restored.Dispose();
        }

        private void TestInvalidImportRejected()
        {
            var service = CreateService();
            ExpectSuccess("导入前设置 BGM 音量", service.SetVolume(AudioBus.Bgm, 0.7f));
            var revisionBefore = service.Revision;

            var invalid = new AudioSettingsSnapshot
            {
                Version = 1,
                Revision = 9,
                BusVolumes = new[]
                {
                    new AudioBusVolumeSnapshot { Bus = AudioBus.Bgm, Volume = 0.1f },
                    new AudioBusVolumeSnapshot { Bus = AudioBus.Bgm, Volume = 0.2f }
                }
            };

            var result = service.ImportSnapshot(invalid);
            ExpectFailure("重复 Bus 快照导入失败", result, AudioFailureReason.DataCorrupted);
            ExpectApproximately(0.7f, service.GetVolume(AudioBus.Bgm), "非法导入不污染当前音量");
            ExpectEqual(revisionBefore, service.Revision, "非法导入不修改 Revision");

            var unsupported = new AudioSettingsSnapshot
            {
                Version = 99,
                Revision = 1,
                BusVolumes = Array.Empty<AudioBusVolumeSnapshot>()
            };
            ExpectFailure("不支持版本导入失败", service.ImportSnapshot(unsupported), AudioFailureReason.VersionUnsupported);

            service.Dispose();
        }

        private void TestConfigurationHotUpdate()
        {
            var service = CreateService();
            ExpectSuccess("热更新前播放 SFX 成功", service.PlayCue(new AudioPlayRequest { CueId = SfxCueId }));

            var newClip = CreateClip("new_sfx_clip");
            var newCatalog = CreateCatalog(new AudioCatalogEntry { AddressKey = "audio/new_sfx", Clip = newClip });
            var newCue = CreateCue("new_sfx", "audio/new_sfx", AudioBus.Sfx);
            service.SetAudioCatalog(newCatalog);
            service.SetCueDefinitions(new[] { newCue });

            ExpectFailure("旧 Cue 在热更新后不可用", service.PlayCue(new AudioPlayRequest { CueId = SfxCueId }), AudioFailureReason.CueMissing);
            ExpectSuccess("新 Cue 在热更新后可用", service.PlayCue(new AudioPlayRequest { CueId = "new_sfx" }));

            service.Dispose();
        }

        private AudioService CreateService()
        {
            var root = new GameObject("NiumaAudioTestRoot");
            _createdObjects.Add(root);

            return new AudioService(
                root.transform,
                CreateDefaultCues(),
                CreateDefaultCatalog(),
                null,
                null,
                4,
                8);
        }

        private AudioCueDefinition[] CreateDefaultCues()
        {
            return new[]
            {
                CreateCue(BgmCueId, BgmAddressKey, AudioBus.Bgm, AudioPlayMode.Loop),
                CreateCue(SfxCueId, SfxAddressKey, AudioBus.Sfx, AudioPlayMode.OneShot),
                CreateCue(VoiceCueId, VoiceAddressKey, AudioBus.Voice, AudioPlayMode.OneShot),
                CreateCue(AmbientCueId, AmbientAddressKey, AudioBus.Ambient, AudioPlayMode.Loop),
                CreateCue(ThreeDCueId, SfxAddressKey, AudioBus.Sfx, AudioPlayMode.OneShot, AudioSpatialMode.ThreeD),
                CreateCue(NoOverlapCueId, SfxAddressKey, AudioBus.Sfx, AudioPlayMode.OneShot, AudioSpatialMode.TwoD, false),
                CreateCue(RandomPitchCueId, SfxAddressKey, AudioBus.Sfx, AudioPlayMode.OneShot, AudioSpatialMode.TwoD, true, 0.2f)
            };
        }

        private AudioCatalogDefinition CreateDefaultCatalog()
        {
            return CreateCatalog(
                new AudioCatalogEntry { AddressKey = BgmAddressKey, Clip = CreateClip("bgm_clip") },
                new AudioCatalogEntry { AddressKey = SfxAddressKey, Clip = CreateClip("sfx_clip") },
                new AudioCatalogEntry { AddressKey = VoiceAddressKey, Clip = CreateClip("voice_clip") },
                new AudioCatalogEntry { AddressKey = AmbientAddressKey, Clip = CreateClip("ambient_clip") });
        }

        private AudioCueDefinition CreateCue(
            string cueId,
            string addressKey,
            AudioBus bus,
            AudioPlayMode playMode = AudioPlayMode.OneShot,
            AudioSpatialMode spatialMode = AudioSpatialMode.TwoD,
            bool allowOverlap = true,
            float randomPitchRange = 0f)
        {
            var cue = ScriptableObject.CreateInstance<AudioCueDefinition>();
            cue.CueId = cueId;
            cue.DisplayName = cueId;
            cue.AddressKey = addressKey;
            cue.Bus = bus;
            cue.PlayMode = playMode;
            cue.SpatialMode = spatialMode;
            cue.Volume = 1f;
            cue.Pitch = 1f;
            cue.RandomPitchRange = randomPitchRange;
            cue.AllowOverlap = allowOverlap;
            _createdObjects.Add(cue);
            return cue;
        }

        private AudioCatalogDefinition CreateCatalog(params AudioCatalogEntry[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<AudioCatalogDefinition>();
            catalog.Entries = entries ?? Array.Empty<AudioCatalogEntry>();
            _createdObjects.Add(catalog);
            return catalog;
        }

        private AudioClip CreateClip(string name)
        {
            var clip = AudioClip.Create(name, 800, 1, 8000, false);
            _createdObjects.Add(clip);
            return clip;
        }

        private void RunCase(string name, Action test)
        {
            try
            {
                test();
                AddPass($"[PASS] {name}");
            }
            catch (Exception ex)
            {
                failedCheckCount++;
                var line = $"[FAIL] {name}：{ex.Message}";
                _reportLines.Add(line);
                UnityEngine.Debug.LogError($"[NiumaAudioTest] {line}", this);
            }
        }

        private void Expect(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }

            AddPass(message);
        }

        private void ExpectSuccess(string message, AudioOperationResult result)
        {
            if (result == null || !result.Succeeded)
            {
                throw new InvalidOperationException($"{message}：期望成功，实际失败 {result?.FailureReason} / {result?.Message}");
            }

            AddPass(message);
        }

        private void ExpectFailure(string message, AudioOperationResult result, AudioFailureReason expectedReason)
        {
            if (result == null)
            {
                throw new InvalidOperationException($"{message}：结果为空");
            }

            if (result.Succeeded || result.FailureReason != expectedReason)
            {
                throw new InvalidOperationException($"{message}：期望失败 {expectedReason}，实际 Succeeded={result.Succeeded}, Reason={result.FailureReason}");
            }

            AddPass(message);
        }

        private void ExpectEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"{message}：期望 {expected}，实际 {actual}");
            }

            AddPass(message);
        }

        private void ExpectApproximately(float expected, float actual, string message, float epsilon = 0.0001f)
        {
            if (Mathf.Abs(expected - actual) > epsilon)
            {
                throw new InvalidOperationException($"{message}：期望 {expected}，实际 {actual}");
            }

            AddPass(message);
        }

        private void AddPass(string message)
        {
            passedCheckCount++;
            if (verboseLog)
            {
                _reportLines.Add(message);
            }
        }

        private void ResetReport()
        {
            ReleaseCreatedObjects();
            lastRunSucceeded = false;
            passedCheckCount = 0;
            failedCheckCount = 0;
            lastReport = string.Empty;
            _reportLines.Clear();
        }

        private void ReleaseCreatedObjects()
        {
            for (var i = _createdObjects.Count - 1; i >= 0; i--)
            {
                var item = _createdObjects[i];
                if (item == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(item);
                }
                else
                {
                    DestroyImmediate(item);
                }
            }

            _createdObjects.Clear();
        }
    }
}
