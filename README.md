# NiumaAudio

## 模块定位
NiumaAudio 是全局音频服务模块，负责 BGM、SFX、UI、环境音、语音、音量设置、静音、淡入淡出、AudioSource 池和音频设置存档。

## 框架设计思路
- 业务模块只传 CueId 或 AddressKey，不直接持有播放用 AudioClip。
- 第一版用 AudioCatalogDefinition 映射本地 AudioClip，后续可替换 Addressables Resolver。
- AudioBus 分为 Master / Bgm / Sfx / UI / Ambient / Voice。
- 存档只保存设置和当前 BGM，不保存瞬时 SFX。

## 核心流程
1. AudioController 加载 CueDefinition、Catalog 和 Mixer 配置。
2. PlayBgm 使用双 AudioSource 做切换和 CrossFade。
3. PlayCue / PlayCue3D 从 Catalog 解析 AudioClip 并从池中取 Source 播放。
4. SetVolume / SetMuted 修改 Bus 设置并刷新输出音量。
5. Tick 推进淡入淡出和 Source 回收。
6. SaveAdapter 导出 Bus 音量、静音状态和当前 BGM Key。

## 模块用法
- 新音效先配置 AudioCueDefinition，再由业务传 CueId 播放。
- 缺失 Key 返回结构化失败，不应该让业务层直接抛异常。
- 全局 AudioRoot 建议放在 Bootstrap 场景并 DontDestroyOnLoad。

## 场景使用方法
推荐放置方式：`AudioRoot` 一个全局常驻物体承载音频服务和存档。

- `AudioRoot`：挂 `NiumaAudioController`，绑定 AudioCatalogDefinition、AudioCueDefinition、可选 AudioMixerGroup。
- `AudioRoot/SaveAdapter` 或全局 `SaveRoot/Providers`：挂 `NiumaAudioSaveAdapter`，保存音量、静音、当前 BGM。
- `AudioRoot/Debug`：开发阶段挂 `AudioBasicTestRunner`。
- `SceneRoot/BgmTrigger`：场景入口或触发器脚本调用 PlayBgm 切换背景音乐。
- `UIRoot`：按钮点击音效通过 UI 按钮脚本或统一 UI 音效桥接调用 PlayCue。
- `DialogueRoot`：语音播放后续可从 Gal 迁移到 PlayVoice。
- 不建议在每个按钮或 NPC 上挂独立 AudioSource；统一走 AudioRoot 的 Source 池，方便音量和静音控制。

## 协作边界
Audio 只管声音播放和设置，不决定剧情、交互、技能等业务何时触发音效。业务模块只发播放请求。


