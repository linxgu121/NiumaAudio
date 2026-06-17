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

## 场景挂载与 Inspector 配置
### NiumaAudioController
建议挂载位置：`CoreScene/BootstrapRoot/AudioRoot`。

用途：全局唯一音频服务，管理 BGM、SFX、UI、环境音、语音、音量和静音设置。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| `Cue Definitions` | 拖所有 `AudioCueDefinition` 配置 | 不建议 | CueId 无法解析，播放请求会失败 |
| `Catalog Definition` | 拖本地音频目录资产 | 不建议 | AddressKey 找不到 AudioClip |
| `Mixer Groups` | 有 AudioMixer 时绑定各 Bus 的 MixerGroup | 可以 | 留空时用 AudioSource 音量兜底 |
| `Pool Root` | 拖 AudioRoot 下的池节点 | 可以 | 留空时运行时自动创建 |
| `Pool Initial Size / Max Size` | 按项目音效并发量设置 | 不可以 | 太小会 SourceUnavailable，太大会浪费 |
| `Register Service To Context` | 核心场景开启 | 可以关闭 | 关闭后其他桥接脚本无法从 GameContext 获取音频服务 |
| `Drive Tick In Update` | 没有统一模块启动器时开启 | 按项目决定 | 外部已 Tick 时再开启会造成淡入淡出速度异常 |

### NiumaAudioSaveAdapter
建议挂载位置：`CoreScene/BootstrapRoot/SaveRoot/SaveAdapters`。

用途：保存音量、静音、当前 BGM 等音频设置。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| `Audio Controller` | 拖 `NiumaAudioController` | 不建议 | 自动查找失败时音频设置不进存档 |
| `Save Controller` | 拖 `NiumaSaveController` | 不建议 | 无法注册存档 Provider |

### AudioCuePlayer
建议挂载位置：需要主动播放音效的按钮、机关、动画事件物体。

用途：把 Inspector 上配置的 CueId 播放请求发给 NiumaAudio。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| `Audio Controller` | 拖 `NiumaAudioController`，或留空自动查找 | 可以 | 自动查找失败时不播放 |
| `Cue` | 填 `AudioCueDefinition.CueId` 或 AddressKey | 不可以 | 没有可播放音效 |
| `Override Bus` | 普通按钮通常关闭，特殊需求才覆盖 Bus | 可以 | 开启后会覆盖 CueDefinition 的 Bus |

### UIButtonAudioBinder
建议挂载位置：需要点击音效的 Button 物体。

用途：自动监听 Button 点击并播放 UI 音效。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| `Button` | 拖当前物体上的 Button | 可以 | 留空时自动获取当前物体 Button |
| `Click Cue` | 填按钮点击 CueId | 不可以 | 点击不播放音效 |
| `Audio Controller` | 拖全局 `NiumaAudioController` | 可以 | 自动查找失败时不播放 |



## 配置资产粒度基准

NiumaAudio 的资产分为“播放逻辑”和“资源解析目录”。

- `AudioCueDefinition`：一个可播放声音逻辑一个资产，例如 `ui_click`、`bgm_village_day`、`sfx_reward_success`。
- `AudioCatalogDefinition`：一份 AddressKey 到 AudioClip 的目录资产。小项目可全局一个；大项目可按章节、地区、DLC 或资源包拆分。

业务模块只填写 CueId / AddressKey，不直接引用 AudioClip。瞬时播放状态、当前正在播放的 SFX 不做资产，也不进入存档。
