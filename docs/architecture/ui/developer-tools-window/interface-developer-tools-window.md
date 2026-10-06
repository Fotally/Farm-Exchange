# DeveloperToolsWindow 对外接口

关联 [#101](https://github.com/Fotally/Farm-Exchange/issues/101) 与 [#104](https://github.com/Fotally/Farm-Exchange/issues/104)，代码位于 `scripts/ui/development/DeveloperToolsWindow.cs`，命名空间保持 `FarmExchange.UI`。该类型只编入开发构建，由 `Main` 的集中 `DEBUG` 入口创建；主场景不序列化开发脚本引用。分步实现见[实现说明](implementation-step-flow-editor.md)。

本地 dev 包使用 `Windows Dev` / `macOS Dev` 预设，保留 Godot 创建此节点所需的 C# 脚本资源。正式两预设继续排除开发源码、节点资源及专用验收图；编译程序集与资源选择分别验收，不能把 DLL 中存在开发类型等同于节点资源已经可加载。

窗口沿用 `DraggableWindow` 的纸面木框及标题拖动，1920×1080 基准尺寸1360×740，避让顶部143与底部180像素。各步骤正文独立滚动，保存和运行入口固定底部。左侧「开发测试」打开窗口，关闭按钮只隐藏窗口；运行继续，明确「中止流程」才停止。确认弹层打开时Esc取消弹层，保持原选择与草稿。

原[#101的1080P实测窗口](godot-developer-tools-1080.png)为文件选择版历史验收，不能作为当前分步布局的截图。新界面图形检查由`test_developer_flow_editor.tscn`生成，验收结果随本次测试记录维护。

| 界面或宿主入口 | 约定 |
| --- | --- |
| 构造 | `DeveloperToolsWindow(currentGame, currentDriver, refreshCurrent, library=null)`接入当前局与唯一驱动，可注入已明确目录的配置库；默认读取白名单内置资源，用户配置按仓库或开发包位置发现。不存在资源或目录错误明确反馈。 |
| 第一步目录 | 分类与中文名称、简介搜索；每行两张卡片，名称15px、简介12px。卡片选择与垃圾桶独立；选定流程才能继续。真实目录只登记已实现流程，空目录禁用继续。 |
| 第二步配置 | `ScenarioProfileChoice`只显示配置名称、修订及只读标记；`ScenarioConfigurationForm.BindDraft`按扫描描述生成控件，整数、日期、选项、条件字段和有序数组统一处理。步骤切换不重载草稿。 |
| 保存 | `ScenarioSaveAsButton`询问新名称，另存为修订1；`ScenarioOverwriteButton`显式覆盖并递增修订。内置示例可以编辑草稿但禁用覆盖、删除与直接运行。未保存修改启动时明确拒绝。 |
| 删除 | `ScenarioDeleteFlow_<标识>`确认后移除目录及全部可写配置；`ScenarioDeleteConfigurationButton`确认后删除当前可写配置文件。弹窗显示对象名、范围与取消/关闭/确认入口。取消或Esc保持，成功刷新失效选择，失败反馈实际原因；内置示例、代码及历史报告保留。 |
| 启动 / 中止 | 第三步`ScenarioStartButton`从已保存原文件严格加载并预检报告目录可写后才准备和执行正式经营命令；运行时锁定导航、选择、编辑、保存和删除。`ScenarioAbortButton`中断流程，保留已经发生的经营结果并输出报告。 |
| 进度 / 反馈 / 报告 | 显示运行对象、固定步骤、实际 tick、等待计数、倍率与暂停。配置错误与输出错误明确区分；保存成功显示真实 `report.json` 绝对路径。保存失败保留执行结果，不显示已落盘，不切换目录。 |
| 当前局倍率 | 0.5、1、2、5、10、20 快捷项与有限正整数输入交给同一时间驱动。玩家选择立即发出 `Player` 来源，运行中的现场流程中断，不自动恢复旧倍率。 |
| `ObserveCurrentCheckpoint` / `GetCurrentMaxTicks` | `Main` 唯一驱动借用的完整检查点和限额入口。窗口只转交固定流程的观察结果和下一边界，不计算生产、费用或日历。 |
| `AdvanceIndependent(delta)` | 当前局仅观察暂停变化；独立局用同一 `SimulationDriver` 推进自己的 `FarmGame` 数据对象，完成后停止。不会替换当前地图、人物或现场经营对象。 |

每次自动操作后沿用 `Main` 的统一经营刷新。流程结束在稳定检查点捕获报告快照，落盘不重读现场。宿主退出时中止仍在运行的流程并保存已有结果，释放倍率通知订阅。

同一配置来回切换步骤保留结果；成功保存新修订或换到另一配置后清空窗口旧进度与报告路径，历史报告文件保持。删除失败重新扫描实际条目，仍存在同文件的草稿、非法输入和焦点保留，已实际删除的选择清理；反馈原删除错误，不将部分完成冒充成功或回滚。

输出目录为仓库/编辑器的`build/test-runs/<run-id>/`，本地dev包的包外`reports/test-runs/<run-id>/`；Windows从exe位置解析，macOS从`.app`父目录解析，不依赖启动工作目录。用户配置保存在仓库`tests/scenario-configs/user/`或开发包旁`configs/scenario-configs/`；仓库配置可以长期纳入Git，保存不自动提交。报告不保存配置副本，用户配置和报告不进入导出包。

`test_developer_tools_window.tscn`通过真实开发入口及隔离配置库验证运行重载、独立Q=3报告、现场暂停、玩家改速中断及报告输出失败；`test_developer_flow_editor.tscn`验证新目录、字段兼容、保存与确认删除。图形运行等待处理帧及`FramePostDraw`保存真实截图，headless不等待图形信号。配置及检查规则见[参数化设计](../../../project/developer-tools-parameterized-tests.md)，操作见[使用说明](../../../project/parameterized-tests-usage.md)，构建排除见[本地开发构建](../../../project/development-build.md)。
