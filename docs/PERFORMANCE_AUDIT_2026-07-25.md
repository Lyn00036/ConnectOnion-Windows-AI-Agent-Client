# ConnectOnion Windows Desktop Client 发布前性能审计

审计日期：2026-07-25  
代码基线：`00363e4` 加工作区中的待发布改动  
测试配置：Release / x64 / .NET SDK 10.0.301 / Unpackaged，另生成未签名 x64 MSIX  
测试机器：JUZI，Intel Core i9-13980HX，32 logical processors，31.6 GB RAM  
操作系统：Windows 11 Pro 25H2，10.0.26200.8894

> 结论中的数值只适用于本机、该数据集和该构建。没有采到的数据明确标为
> `NEEDS MANUAL VERIFICATION`，不会用任务管理器 Working Set 代替泄漏结论。

## 1. Executive Summary

当前构建的常用交互、空闲功耗、主聊天列表虚拟化和 50 次导航/会话切换均达到发布候选标准。
审计发现并修复了一个真实的 Release 发布问题：反射式 JSON 恢复在裁剪发布中丢失
`ToolActivityViewModel` 元数据，历史 Tool Activity 会被降级成
“Tool execution history could not be restored”。修复后，真实 trimmed 发布目录和最终未裁剪
ReadyToRun 发布目录都能恢复同一份 15-step 数据，Tool Activity 展开为 7.2–12.8 ms。

最严重的三个问题是：

1. **P1：Release trimming 破坏持久化 Tool Activity，并且协议/审批路径仍有 IL2026。**
   已为会话结构化 payload 增加 source-generated JSON metadata，并暂时关闭 Release/CI trimming。
2. **P2：真正冷启动证据不足且波动很大。** 单样本从 1.03 s 到 8.82 s；最终新发布目录
   首次启动为 4.65 s。无法清 standby list，WPR 又因系统策略失败，不能宣称已解决。
3. **P2：语音、真实 WebSocket/Stop/重连、系统通知和 packaged 安装运行未完成自动压力覆盖。**
   这些依赖真实 Agent、音频设备、通知激活或签名安装，保留为人工发布门禁。

**是否达到发布标准：** 核心性能与内存门禁通过；完整 GA 发布仍需完成下面的人工门禁。  
**推荐结论：Ready with Known Risks。** 可以进入 Release Candidate；在 packaged 冷启动、
真实 Agent 交互、语音和通知场景签字前，不建议直接推送广泛生产发布。

原始证据：

- [原始真实配置启动基线](../TestResults/perf-audit/baseline-real-profile/report.md)
- [最终未裁剪 ReadyToRun 启动复测](../TestResults/perf-audit/baseline-after-untrimmed-real-profile/report.md)
- [5 分钟空闲 summary](../TestResults/perf-audit/idle-5min-real-profile/summary.json)
- [原始 50 次五场景 TRX](../TestResults/perf-audit/memory-50-release/memory-leaks.trx)
- [最终 50 次缓存会话 TRX](../TestResults/perf-audit/memory-final-untrimmed-alternating/memory-leaks.trx)
- [最终大型会话 UI 数据](../TestResults/perf-audit/ui-performance-final-untrimmed.json)

## 2. Baseline Table

| 场景 | 原始结果 | 优化后结果 | 改善 | 是否通过 |
|---|---:|---:|---:|---|
| 冷启动到首帧 | 1,032 ms，n=1 | 4,655 ms，n=1 | 不可比较；新发布目录首次加载 | NEEDS MANUAL VERIFICATION |
| 热启动到首帧 | 974.3 ms，n=4 | 717.6 ms，n=4 | 26.3% | PASS |
| 30 s Idle Working Set | 206.4 MB | 204.7 MB | 0.8% | PASS（略高于 200 MB target，远低于 350 MB fail） |
| 30 s Idle Private Bytes | 127.1 MB | 133.6 MB | -5.1% | PASS |
| 首帧 Managed Heap | 5.9 MB | 5.7 MB | 3.4% | PASS |
| 5 min 尾段 CPU | 0.010% mean | 未重复 | — | PASS（修复不触及空闲循环） |
| 打开 Settings | 68.5 ms median，52.5–203.4 | 59.7 ms，51.5–194.2 | 12.8% | PASS |
| 打开 Add Agent | 67.4 ms median，55.2–91.0 | 62.2 ms，49.4–92.0 | 7.7% | PASS |
| 最小化后恢复 | 13.5 ms median | 13.8 ms | -2.2% | PASS |
| 隐藏/托盘恢复 | 109.0 ms median | 75.2 ms | 31.0% | PASS |
| 100 条会话首开 | 498.6 ms | 474.4 ms | 4.9% | PASS |
| 500 条会话首开 | 521.3 ms | 625.6 ms | -20.0%（各 n=1） | NEEDS MANUAL VERIFICATION |
| 2,000 条会话首开 | 555.0 ms | 442.2 ms | 20.3% | PASS |
| 100/500/2,000 条缓存重开 | 371.8 / 388.6 / 361.8 ms | 336.1 / 387.9 / 383.9 ms | 9.6% / 0.2% / -6.1% | PASS |
| Tool Activity 展开 | Release 中卡片恢复失败 | 8.6 / 10.2 / 9.5 ms | 功能恢复 | PASS |
| 50 次缓存会话切换尾段 | 0.56 MB/cycle，19.2 MB span | 0.03 MB/cycle，1.2 MB span | 明显更稳定 | PASS |
| 发送第一条真实消息 | 未连接真实 Agent | 未连接真实 Agent | — | NEEDS MANUAL VERIFICATION |
| 打开 live 审批卡片 | 数据集中无 live approval | 未测 | — | NEEDS MANUAL VERIFICATION |

冷启动两组只有一个“首次”样本，且都未清 OS standby list，因此改善百分比没有统计意义。
大型会话的原始 Release 会丢失 Tool Activity，最终数据包含真实恢复后的视觉树；两组也不是完全等价的
渲染负载，不能把所有差异归因于代码速度。

## 3. Issues

### PERF-001 — P1 — Release trimming 破坏结构化会话 payload

- **问题描述：** Release 窗口把合法的历史 Tool Activity 降级为恢复失败卡片。
- **复现步骤：**
  1. 以 Release x64 打开包含持久化 Tool Activity 的会话。
  2. 使用相同数据库生成 100/500/2,000 条混合消息会话。
  3. 观察 sidebar preview 和消息卡片。
- **性能/功能证据：**
  - Debug 测试宿主对同一 payload 成功恢复 15 steps。
  - 修复前 Release UI 显示 “Tool execution history could not be restored”，展开按钮不存在。
  - `dotnet publish` 报告协议、审批、问答、计划回复等 15 条应用代码 IL2026 trim warning。
- **根因：** `ConversationRepository.Mapping` 使用 reflection-based
  `JsonSerializer.Serialize/Deserialize<ToolActivityViewModel>`；linker 无法证明运行时所需成员。
- **热点/保留调用链：**
  `ConversationRepository.LoadMessagesAsync` →
  `RowToMessage` → reflection JSON metadata → trimmed member unavailable → catch → activity fallback。
- **修改文件：**
  - `ConnectOnion.WinUIClient.Core/Data/ConversationJsonContext.cs`
  - `ConnectOnion.WinUIClient.Core/Data/ConversationRepository.Mapping.cs`
  - `ConnectOnion.WinUIClient/ConnectOnion.WinUIClient.csproj`
  - `.github/workflows/ci.yml`
  - `AGENTS.md` / `CLAUDE.md`
- **修复方案：**
  - 为 `ToolActivityViewModel` 和 thought-step list 增加 source-generated JSON metadata。
  - repository 的读写均使用 `JsonTypeInfo` overload。
  - 保留 ReadyToRun，暂时关闭 Release 和 CI trimming；恢复 trimming 前必须清完警告并跑真实 Agent。
- **风险：** 未压缩发布目录增大；见 Package Analysis。功能风险低，存储 JSON 形状保持兼容。
- **修复前后：** 恢复失败 → 15-step payload 恢复成功；Tool Activity 展开 7.2–12.8 ms。

### PERF-002 — P2 — 冷启动样本不稳定，缺少 ETW 根因证据

- **问题描述：** 单次首次启动在不同输出目录出现 1.03 s、4.65 s 和 8.82 s。
- **复现步骤：** 新构建/发布目录第一次启动，等待 `StartupTelemetry.TrackFirstFrame`。
- **证据：**
  - 隔离空配置首次：8,815 ms；`managedEntry=3,719`、`hostStarted=8,314`。
  - 最终发布目录首次：4,655 ms；`managedEntry=1,723`、`hostStarted=4,208`。
  - 随后的热启动稳定在 695–752 ms。
- **根因：** 时间主要消耗在 managed entry 之前和 Host 启动阶段，但 WPR 无法启动，
  无法再区分磁盘冷页、病毒扫描、R2R 首次映射或服务初始化。
- **修改文件：** 无生产代码修改。
- **风险：** 商店/新安装机器的真正冷启动可能越过 5 s 响应线。
- **前后结果：** 热启动改善 26.3%；冷启动没有足够证据声称改善。

### PERF-003 — P2 — Settings 导航出现 native handle 高水位变化，但未证实泄漏

- **问题描述：** Settings 50 次循环尾半段 handle span 达 58–60。
- **复现步骤：** 10 次预热后循环 Settings 50 次，每次离开后采样。
- **证据：**
  - Private Bytes tail slope `-0.09 MB/cycle`，span `2.0 MB`。
  - `SettingsPage`、`SettingsViewModel`、`SettingsOverlay` 在 0/25/50 快照中均为 1。
  - `gcroot` 只有预期的 `MainWindow → SettingsOverlay → SettingsPage` 和 framework root。
  - 一个“移除 microphone enumeration Task.Run”的假设修复复测无改善，已撤销。
- **根因：** 未发现托管 event/static/timer root；更符合 WinUI/UIA/device enumeration 的 native
  high-water mark。没有 handle-type ETW/WinDbg 证据，不把它定性为泄漏。
- **修改文件：** 无生产修改。
- **风险：** 极长时间导航仍需 100–200 次及 handle type 分解确认。
- **前后结果：** 最终缓存会话 50 次 handle tail span 27、线程 span 5，门禁通过。

### PERF-004 — P3 — 2,000 条消息加载全部数据对象，但视觉元素保持虚拟化

- **问题描述：** repository 和 `ChatViewModel.RestoreConversationAsync` 当前读取全部历史，
  并在 UI collection 上 `Clear`/逐项加入；没有数据库分页。
- **复现步骤：** 打开 100、500、2,000 条混合消息会话并缓存重开。
- **证据：**
  - 2,000 条最终首开 442.2 ms，缓存重开 median 383.9 ms。
  - 只实现 14 个 `ListViewItem`；100/500 条分别为 24/24。
  - 2,000 条会话采样为 WS 346.0 MB、Private 270.6 MB。
- **根因：** 数据层 O(history)，视觉层仍是虚拟化 `ListView`。
- **修改文件：** 无。现有结果未证明需要冒险引入分页/滚动语义变化。
- **风险：** 远大于 2,000 条或超长 Markdown/图片元数据会继续增加数据对象内存。
- **前后结果：** 达到“不能一次创建全部视觉元素”的标准；分页列为后续 P3 backlog。

### PERF-005 — P3 — 启动基准的 shutdown harness 已过时

- **问题描述：** `Measure-Performance.ps1` 查找已删除的
  `ConnectOnionTrayHelper` native window；当前托盘基于 H.NotifyIcon。
- **证据：** 原始和最终共 15/15 次都报告 “tray helper window not found”，随后由 harness kill。
- **根因：** 测量脚本仍描述旧托盘实现。
- **修改文件：** 未修改生产代码；shutdown 不属于本次要求的核心指标。
- **风险：** 报告不能提供 graceful shutdown 时间，但启动和 idle 样本在 kill 前已经完成。
- **结果：** Shutdown 一律排除，不把 0/1 ms harness 值当产品数据。

### PERF-006 — P2 — 设备/外部状态场景覆盖不足

- **范围：** live WebSocket/Stop/重连、流式 token 峰值、live approval、SpeechRecognizer、
  音频设备、系统通知点击、packaged tray/activation。
- **静态检查：**
  - run snapshot 使用单 pending slot 和单 dispatcher drain，避免每 token 排队。
  - `ChatComposer` 的 waveform timer 只在录音期间运行，并在 dispose 中解除。
  - 图片 converter 设置 `DecodePixelWidth=280`，只保留 weak `BitmapImage`。
  - Thinking Win2D control 由 `x:Load` 延迟创建。
- **风险：** 静态生命周期设计合理，但不能替代设备和 ETW 压力测试。
- **状态：** NEEDS MANUAL VERIFICATION。

## 4. Memory Test Results

### 4.1 原始五场景，每 5 轮采样

格式为 `Private MB / Working Set MB / Handles / Threads`。正式采样前预热 10 次。

| 场景 | 轮次 | Private | Working Set | Handles | Threads |
|---|---:|---:|---:|---:|---:|
| Settings | 0 | 148.5 | 234.4 | 1616 | 83 |
| Settings | 5 | 151.7 | 237.3 | 1633 | 84 |
| Settings | 10 | 152.4 | 238.3 | 1641 | 84 |
| Settings | 15 | 154.7 | 240.6 | 1631 | 79 |
| Settings | 20 | 156.2 | 242.4 | 1638 | 78 |
| Settings | 25 | 156.4 | 242.9 | 1627 | 77 |
| Settings | 30 | 156.8 | 243.3 | 1634 | 76 |
| Settings | 35 | 155.2 | 240.5 | 1654 | 76 |
| Settings | 40 | 155.5 | 241.5 | 1670 | 77 |
| Settings | 45 | 155.0 | 241.4 | 1675 | 77 |
| Settings | 49 | 155.0 | 241.4 | 1685 | 77 |
| Add Agent | 0 | 152.4 | 238.4 | 1570 | 83 |
| Add Agent | 5 | 158.2 | 244.3 | 1570 | 83 |
| Add Agent | 10 | 159.3 | 245.8 | 1572 | 83 |
| Add Agent | 15 | 155.1 | 241.8 | 1552 | 78 |
| Add Agent | 20 | 151.2 | 237.4 | 1548 | 77 |
| Add Agent | 25 | 151.1 | 237.6 | 1525 | 75 |
| Add Agent | 30 | 153.7 | 237.9 | 1523 | 75 |
| Add Agent | 35 | 152.6 | 237.4 | 1530 | 75 |
| Add Agent | 40 | 152.9 | 238.1 | 1541 | 76 |
| Add Agent | 45 | 152.9 | 238.4 | 1540 | 76 |
| Add Agent | 49 | 152.9 | 239.2 | 1541 | 76 |
| Agent Detail | 0 | 167.5 | 256.7 | 1602 | 85 |
| Agent Detail | 5 | 171.5 | 261.1 | 1602 | 85 |
| Agent Detail | 10 | 171.5 | 261.4 | 1602 | 85 |
| Agent Detail | 15 | 166.6 | 256.1 | 1573 | 77 |
| Agent Detail | 20 | 168.8 | 259.9 | 1549 | 77 |
| Agent Detail | 25 | 169.8 | 261.0 | 1541 | 75 |
| Agent Detail | 30 | 167.1 | 256.6 | 1541 | 75 |
| Agent Detail | 35 | 167.1 | 256.7 | 1547 | 75 |
| Agent Detail | 40 | 167.2 | 257.1 | 1555 | 76 |
| Agent Detail | 45 | 167.4 | 257.4 | 1554 | 76 |
| Agent Detail | 49 | 171.0 | 262.6 | 1554 | 76 |
| Conversation | 0 | 194.7 | 288.8 | 1625 | 84 |
| Conversation | 5 | 194.6 | 289.9 | 1622 | 84 |
| Conversation | 10 | 190.8 | 287.0 | 1624 | 84 |
| Conversation | 15 | 198.4 | 294.5 | 1584 | 78 |
| Conversation | 20 | 203.0 | 301.6 | 1580 | 77 |
| Conversation | 25 | 202.9 | 301.7 | 1583 | 77 |
| Conversation | 30 | 203.1 | 301.9 | 1590 | 77 |
| Conversation | 35 | 204.7 | 303.8 | 1601 | 78 |
| Conversation | 40 | 204.9 | 304.2 | 1599 | 78 |
| Conversation | 45 | 203.6 | 302.9 | 1599 | 78 |
| Conversation | 49 | 203.6 | 303.0 | 1600 | 78 |
| 4-session alternating | 0 | 209.7 | 305.6 | 1629 | 83 |
| 4-session alternating | 5 | 212.8 | 310.1 | 1631 | 83 |
| 4-session alternating | 10 | 214.9 | 313.2 | 1621 | 79 |
| 4-session alternating | 15 | 222.5 | 320.9 | 1602 | 77 |
| 4-session alternating | 20 | 220.9 | 320.9 | 1599 | 76 |
| 4-session alternating | 25 | 215.7 | 314.4 | 1604 | 76 |
| 4-session alternating | 30 | 220.1 | 318.8 | 1617 | 76 |
| 4-session alternating | 35 | 231.7 | 332.6 | 1632 | 77 |
| 4-session alternating | 40 | 234.8 | 335.8 | 1631 | 77 |
| 4-session alternating | 45 | 227.6 | 327.8 | 1636 | 77 |
| 4-session alternating | 49 | 230.8 | 331.6 | 1639 | 77 |

尾半段趋势：

| 场景 | Private slope MB/cycle | Private span MB | Handle span | Thread span | 结果 |
|---|---:|---:|---:|---:|---|
| Settings | -0.09 | 2.0 | 58 | 2 | PASS |
| Add Agent | -0.01 | 2.6 | 20 | 1 | PASS |
| Agent Detail | 0.03 | 4.0 | 15 | 1 | PASS |
| Conversation | 0.02 | 2.1 | 18 | 1 | PASS |
| 4-session alternating | 0.56 | 19.2 | 35 | 1 | PASS |

### 4.2 最终发布候选，4-session alternating

| 轮次 | Private MB | Working Set MB | Handles | Threads |
|---:|---:|---:|---:|---:|
| 0 | 213.9 | 299.7 | 1605 | 85 |
| 5 | 219.4 | 306.5 | 1608 | 85 |
| 10 | 220.3 | 308.5 | 1570 | 78 |
| 15 | 221.0 | 310.0 | 1569 | 77 |
| 20 | 220.6 | 311.5 | 1567 | 76 |
| 25 | 221.6 | 313.0 | 1576 | 77 |
| 30 | 222.0 | 313.9 | 1596 | 78 |
| 35 | 222.1 | 314.2 | 1597 | 78 |
| 40 | 222.2 | 314.4 | 1600 | 78 |
| 45 | 222.2 | 314.6 | 1592 | 74 |
| 49 | 222.6 | 315.5 | 1599 | 73 |

尾段 `0.03 MB/cycle`、Private span `1.2 MB`、handle span `27`、thread span `5`，均通过。

### 4.3 三阶段 managed snapshots 与保留路径

| 快照 | GC Heap bytes | Objects | SettingsPage | SettingsViewModel | CancellationTokenSource |
|---|---:|---:|---:|---:|---:|
| 预热后 | 5,177,940 | 60,966 | 1 | 1 | 26 |
| 25 次 | 5,508,120 | 60,184 | 1 | 1 | 26 |
| 50 次 | 5,710,072 | 63,331 | 1 | 1 | 25 |

50 次比基线多出的主要对象是 profiler 触发的 reflection metadata/string，不是页面实例。
`SettingsPage` 的实际 root 为 app framework/static service → `MainWindow` →
`SettingsOverlay` → `SettingsPage`；没有第二棵离开的 Settings visual tree。

快照：

- [baseline gcdump](../TestResults/perf-audit/snapshots-settings/settings-00-baseline.gcdump)
- [25 cycles gcdump](../TestResults/perf-audit/snapshots-settings/settings-25-cycles.gcdump)
- [50 cycles gcdump](../TestResults/perf-audit/snapshots-settings/settings-50-cycles.gcdump)
- [50 cycles heap dump](../TestResults/perf-audit/snapshots-settings/settings-50-cycles.dmp)

Native Heap、XAML native retained size 和 COM dominator 没有可用的逐快照分解；Private Bytes、
handle/thread 趋势是进程级证据，不能替代 native heap snapshot。

## 5. UI Responsiveness Results

| 数据集 | 首开 ms | 缓存重开 median ms | 已实现 ListViewItem | Tool 展开 ms |
|---:|---:|---:|---:|---:|
| 100 messages | 474.4 | 336.1 | 24 | 8.6 |
| 500 messages | 625.6 | 387.9 | 24 | 10.2 |
| 2,000 messages | 442.2 | 383.9 | 14 | 9.5 |

- **虚拟化：PASS。** 主列表是标准 `ListView`，没有替换成 `StackPanel`，2,000 条消息只实现 14 个容器。
- **模板选择：PASS。** `ChatMessageTemplateSelector` 按 bubble kind 选择独立模板。
- **Tool Activity：PASS with design constraint。** 内部 timeline 故意使用 non-virtualizing
  `ItemsControl`，以固定卡片高度并避免滚动跳动；step binding 使用 classic `{Binding}` 防止
  compiled binding 保留。真实 fixture 15 steps 展开仍低于 11 ms。
- **延迟创建：PASS。** Thinking Win2D 和 approval details 使用 `x:Load`。
- **图片：PASS（静态 + 有限数据）。** converter 解码宽度 280 px，按路径存 weak reference。
- **高频流式更新：静态 PASS / 动态未验证。** `ChatViewModel` 使用一个 pending snapshot slot
  和一个 dispatcher drain；没有真实高速 Agent trace 来量化 slow frames。
- **慢帧、Measure/Arrange、UI thread call stacks：NEEDS MANUAL VERIFICATION。**
  WPR 返回 `0xc5585011 Failed to enable policy to profile system performance`；
  本会话也无法自动控制 Visual Studio XAML UI Responsiveness。
- **无障碍/键盘：** 现有 690 个 Core tests 和 6 个 real-window shell smoke 通过；
  Narrator 完整走查仍需人工。

## 6. Startup Results

热启动 phase median：

| 阶段 | 原始 ms | 最终 ms | 改善 |
|---|---:|---:|---:|
| managedEntry | 114.5 | 99.0 | 13.5% |
| hostStarted | 458.5 | 347.5 | 24.2% |
| windowCreated | 806.5 | 590.0 | 26.8% |
| windowActivated | 827.5 | 605.5 | 26.8% |
| firstFrame | 974.3 | 717.6 | 26.3% |

最终首次新目录启动的阶段为：

`managedEntry 1,723 → hostStarted 4,208 → windowCreated 4,496 →
windowActivated 4,511 → firstFrame 4,655 ms`。

首帧前调用链静态检查：

- `App` 构造 Generic Host，`OnLaunched` 启动 hosted services 后创建窗口。
- notification settings/registration 和 keyboard settings 在 host start 中执行。
- image cache prune 已在 `Task.Run` 中。
- agent presence/network probe 不阻塞首帧。
- SQLite 初始化只保证 schema/migration，不在首帧前加载所有消息。

“可实际交互”没有单独的产品 telemetry mark；real-window smoke 在最终发布目录中可找到并操作
Settings/Add Agent。首个默认会话加载完成时间也没有独立 mark；合成会话首开 442–626 ms
作为替代证据。建议后续增加 `InteractiveReady` 和 `InitialConversationLoaded` 两个 mark。

## 7. Package Analysis

| 产物 | 文件数 | 大小 |
|---|---:|---:|
| 原 trimmed/ReadyToRun publish | 186 | 107.51 MB |
| 最终 untrimmed/ReadyToRun publish | 344 | 246.89 MB |
| x64 MSIX（压缩） | 352 entries | 96.65 MB |
| MSIX 解压/近似安装占用 | 352 entries | 246.84 MB |
| 独立 Installer | 不存在 | N/A |

最终 MSIX 最大 30 项：

| # | 文件 | 未压缩 MB | MSIX 内压缩 MB |
|---:|---|---:|---:|
| 1 | Microsoft.Windows.SDK.NET.dll | 52.75 | 14.22 |
| 2 | onnxruntime.dll | 20.67 | 7.02 |
| 3 | DirectML.dll | 17.84 | 9.08 |
| 4 | Microsoft.WinUI.dll | 15.64 | 4.15 |
| 5 | System.Private.CoreLib.dll | 15.28 | 6.41 |
| 6 | BouncyCastle.Cryptography.dll | 11.06 | 5.55 |
| 7 | System.Private.Xml.dll | 7.43 | 3.32 |
| 8 | coreclr.dll | 4.40 | 2.24 |
| 9 | System.Linq.Expressions.dll | 3.47 | 1.18 |
| 10 | Microsoft.InteractiveExperiences.Projection.dll | 3.36 | 0.95 |
| 11 | ConnectOnion.WinUIClient.dll | 3.14 | 1.10 |
| 12 | System.Data.Common.dll | 2.64 | 1.24 |
| 13 | Microsoft.Graphics.Canvas.Interop.dll | 2.59 | 0.76 |
| 14 | System.Security.Cryptography.dll | 2.43 | 1.05 |
| 15 | JetBrainsMono Nerd Font BoldItalic | 2.36 | 1.34 |
| 16 | JetBrainsMono Nerd Font Bold | 2.36 | 1.34 |
| 17 | JetBrainsMono Nerd Font Italic | 2.36 | 1.34 |
| 18 | JetBrainsMono Nerd Font Regular | 2.36 | 1.33 |
| 19 | Microsoft.DiaSymReader.Native.amd64.dll | 2.09 | 0.97 |
| 20 | clrjit.dll | 1.99 | 1.09 |
| 21 | System.Private.DataContractSerialization.dll | 1.97 | 0.86 |
| 22 | System.Text.Json.dll | 1.80 | 0.78 |
| 23 | System.Private.Windows.Core.dll | 1.76 | 0.61 |
| 24 | System.Net.Http.dll | 1.67 | 0.78 |
| 25 | Microsoft.Graphics.Canvas.dll | 1.65 | 0.62 |
| 26 | Microsoft.Web.WebView2.Core.Projection.dll | 1.38 | 0.39 |
| 27 | WinRT.Runtime.dll | 1.33 | 0.51 |
| 28 | Markdig.dll | 1.33 | 0.56 |
| 29 | mscordaccore.dll | 1.29 | 0.58 |
| 30 | mscordaccore versioned copy | 1.29 | 0.58 |

检查结果：

- MSIX 不包含 PDB、测试程序集、日志、测试数据或旧 Electron/frontend 资源。
- unpackaged publish 含 3 个 PDB，共 0.82 MB；如果直接分发文件夹，可安全从用户 payload
  排除并单独作为 symbols artifact 保存。MSIX 已自动排除。
- 只有 x64 运行时，未发现 x86/ARM64 重复。
- 四套 Nerd Font 合计 9.44 MB 未压缩；它们对应正常/斜体/粗体/粗斜体，不能在无字体回归证据时删除。
- ONNX Runtime + DirectML 合计 38.51 MB；来自 Windows AI/SDK 依赖。未证明无运行时用途，
  不建议手工删除 DLL。
- 未生成 `.msixupload` symbols package，因为本机缺 `mspdbcmf.exe`。
- 未签名 MSIX 已成功构建，但没有安装，因此首次安装解压、注册、packaged 冷启动仍需人工测量。

## 8. Remaining Risks

1. 真正 cold-start 需要管理员权限清 standby list，并用 WPR/WPA 或 VS Startup profiler 重跑
   至少 5 轮；当前 n=1。
2. Packaged/MSIX 没有签名安装和启动，无法报告实际安装后磁盘占用与首次注册开销。
3. 真实 Agent 的 first send、流式 token burst、Stop、断线重连、后台完成与 live approval 未压测。
4. SpeechRecognizer/AudioGraph/Win2D waveform 需要真实麦克风和设备生命周期循环。
5. 系统通知点击、tray notification activation、AppWindow/Window 激活事件需要 50 次真实循环。
6. Native heap、COM/WinRT retained graph、XAML slow frame/Measure/Arrange call stacks 未采到。
7. 2,000 条消息仍一次加载全部数据对象；主视觉虚拟化通过，但更长历史可能需要分页。
8. Release trimming 已关闭。恢复前需要清除所有应用 IL2026、运行 trimmed live-agent suite，
   并验证 WinUI、Windows App SDK、SQLite、Win2D、序列化和通知路径。
9. 性能脚本的 graceful shutdown 路径过时，shutdown metric 暂不可用。

## 9. Final Release Checklist

| 检查项 | 状态 | 证据/备注 |
|---|---|---|
| Release x64 build | PASS | solution build 成功，1 个非性能 CA1305 warning |
| Protocol unit tests | PASS | 161/161 |
| Core + architecture tests | PASS | 690/690 |
| SQLite integration tests | PASS | 113/113，含 trim-safe Tool Activity round-trip |
| Protocol conformance | PASS | address/canonical/signature/verify |
| Final real-window shell smoke | PASS | 6/6 |
| 热启动预算 | PASS | median 717.6 ms |
| 真正冷启动统计 | NEEDS MANUAL VERIFICATION | 无 standby purge，n=1 |
| 5 分钟 idle CPU/IO | PASS | tail CPU 0.010%，tail IO 0 |
| 50 次 Settings/Add Agent/Agent Detail/Conversation | PASS | 所有 tail thresholds 通过 |
| 最终 50 次缓存会话切换 | PASS | 0.03 MB/cycle，1.2 MB span |
| Managed retention path | PASS | 页面/VM 均 1，无重复 visual tree root |
| Native/XAML retention path | NEEDS MANUAL VERIFICATION | WPR policy 阻止采集 |
| 2,000 条主列表虚拟化 | PASS | 14 realized items |
| Tool Activity 行为/展开 | PASS | 恢复成功，7.2–12.8 ms |
| 高速真实流式消息 | NEEDS MANUAL VERIFICATION | 无 live Agent trace |
| Speech/Win2D/audio lifecycle | NEEDS MANUAL VERIFICATION | 无设备自动化 |
| 通知/托盘 50 次 | NEEDS MANUAL VERIFICATION | tray restore 10 次通过 |
| x64 MSIX 构建 | PASS | 96.65 MB，未签名 |
| Packaged 安装/首次启动 | NEEDS MANUAL VERIFICATION | 未安装 |
| Release trimming 安全性 | PASS（通过禁用） | ReadyToRun 保留；CI override 已改为 false |
| 屏幕阅读器完整走查 | NEEDS MANUAL VERIFICATION | Automation names/keyboard tests未替代 Narrator |

## 最终结论

**Ready with Known Risks**

当前构建可以作为 Release Candidate：已确认的 Release-only 数据恢复问题已修复，常用路径没有
持续线性内存增长，空闲 CPU/IO 稳定，2,000 条消息保持视觉虚拟化，完整自动测试通过。
在广泛生产发布前，必须完成人工门禁中的 packaged cold start、真实 Agent、语音、通知和
WPR/XAML trace；这些项目没有证据时不能被默认为通过。
