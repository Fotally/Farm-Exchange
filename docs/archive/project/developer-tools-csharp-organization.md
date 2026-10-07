> 历史归档（2026-10-07，整理任务 [#123](https://github.com/Fotally/Farm-Exchange/issues/123)）。关联：#97；已选范围由#100/#101实施并随PR #102合并，剩余候选见现行后续功能计划。现行入口：[当前说明](../../project/development-build.md)。正文中的“当前”“待确认”“待合并”描述写作当时，不作为现行规则或交付状态。

# 开发者工具的 C# 源码组织讨论

历史设计说明：下文保留设计阶段的方案与当时接口事实；#100 / #101 已按[参数化测试主计划](developer-tools-parameterized-tests.md)恢复实施，现行代码与构建接入见主计划及其接口、使用文档。未选用的候选能力仍属于后续设计。

关联 [#97](https://github.com/Fotally/Farm-Exchange/issues/97)。2026-10-05 补充讨论。用户已明确只维护一套代码，先定架构再选功能；dev 本地构建、不进入 CI，Windows 与 macOS 通过预设覆盖。本文是待讨论的源码组织建议，不创建 C# 文件、接口、导出配置或新功能。现有方案见[设计草案](developer-tools-proposal.md)，案例证据见[公开 C# 项目案例](../../research/developer-tools-csharp-cases.md)，项目 SDK 配置见[构建调研](../../research/developer-tools-builds.md)。

本轮进一步区分模块专用能力与跨模块调试流程：前者由原模块维护，后者允许放在独立开发调试目录。可重复检查流程、运行对象、完整执行路径、接口现状和报告约束统一见[跨模块自动调试补充](developer-tools-cross-module-debugging.md)；具体工具功能仍未授权实施。

用户后续确定以测试思路组织首个买入→加工→卖出流程，步骤固化、参数文件可变，考虑现场/独立局共用底层。候选参数与报告布局见[参数化测试设计](developer-tools-parameterized-tests.md)：源码在开发目录、配置样例在测试数据目录、设计文档在docs、运行报告在产物目录，不混放；本轮仍只设计。

最新确认增加公共经营速率：发布版0.5×、1×、2×，开发版可更快。共用时间能力属于正常经营代码，不放进development排除范围；接口及实现由[#100](https://github.com/Fotally/Farm-Exchange/issues/100)单独设计，本文不指定其类名或源码布局。开发额外权限和测试时间方案属于开发附加内容。配置长期保存，报告只引用版本和内容摘要，不再生成参数副本；报告路径已暂定，现场证据不足标准已确认。

现场手动改速中断整个自动流程、报告先只输出JSON已确认。流程中断处理留在开发流程组织层；时间能力只提供必要接入，报告数据不增加Markdown生成器。

## 先区分源码目录和程序集

把工具代码放到独立目录，并不需要建立另一个 `.csproj`。一个项目可以按配置选择编译文件，生成一份 `FarmExchange.dll`；dev 多编入工具文件，release 不编入它们。公共经营模块只维护一份。

另一种做法是独立工具 `.csproj`，引用共同的游戏程序集。这也可以共用一份经营实现，但新增项目引用、跨程序集接口以及导出打包关系。它适合确实要复用工具或独立交付工具的项目；目前 Farm Exchange 尚无这种需要。两个方案不能仅凭“是否分目录”区分。

| 组织方式 | 工具怎样访问经营 | 发布怎样处理 | 当前适用性 |
| --- | --- | --- | --- |
| 同一项目，工具分目录，原模块按需 partial 补充 | 既有公开快照与命令；必要专用操作留在原模块，工具只调用少量入口 | 配置移除开发源码，场景组装不引用被移除类型 | 推荐作为讨论起点 |
| 独立工具程序集引用游戏程序集 | 需明确公开接口，或经审慎设计的友元程序集；不能用 partial 跨程序集补充私有成员 | 条件项目引用并处理工具程序集和资源是否随包 | 暂无实际需求，不先拆 |
| 共用程序集，工具随包，运行时权限控制 | 现有命令或权限检查过的操作 | 保留工具，按配置、权限或用户设置启用 | 部分成熟项目采用；与当前 release 排除专用工具的建议不同 |

C# 的 partial 类型必须位于同一程序集，并且各部分共享成员访问范围。因此 partial 是文件组织方式，不是独立插件机制；各部分合并为同一个类型。[Microsoft partial 说明](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/partial-classes-and-methods)、[语言规范的成员访问规则](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#1527-partial-declarations)

## 对应现有模块的候选目录

建议模块专用开发能力放在所属业务目录内，用统一的 `development/` 子目录标记编译范围；跨模块检查流程放在独立 `scripts/development/` 中。下列树只是组织示意，具体文件只随已确认能力增加：

```text
FarmExchange.csproj                     一个项目
scripts/
  gameplay/
    FarmGame.cs                        公共经营实现
    development/
      FarmGame.cs                      同一类的开发专用 partial 部分，按需存在
  ui/
    Main.cs                            公共场景组装，集中一处接入工具
    development/
      DeveloperToolsWindow.cs          开发界面，按需存在
  world/
    development/
      WorkerDebugOverlay.cs            开发绘制，按需存在
  development/
    scenarios/
      RawProcessingOrderScenario.cs    跨模块可重复检查流程，按需存在
    DebugScenarioReport.cs             流程报告数据，按需存在
  farming/、processing/、workers/等      继续维护同一份正常实现
tests/scenario-configs/                候选JSON测试用例参数，与流程源码分开
build/test-runs/                       仓库内运行产物，与配置和文档分开
```

选择模块内子目录的原因：专用经营入口仍属于 `FarmGame`，UI 属于 UI，诊断绘制属于世界表现；不为了一个编译开关把所有状态访问移到万能的 `DeveloperTools` 类。子目录只是源码分组，不自动改变 namespace。上述两份 `FarmGame.cs` 都应使用 `FarmExchange.Gameplay`、声明同一个 `FarmGame`；文件名与主要类型一致，遵守项目已有 PascalCase 规则。

跨模块场景的步骤和检查不硬塞进业务模块、`FarmGame` 或窗口。独立开发目录仍在同一个项目中；其候选命名空间 `FarmExchange.Development` 在实施时再纳入目录规范。场景只组织调用、请求统一推进和形成报告，不建立另一套业务状态或 tick 循环，也不要求新增 `.tscn`。

同一固定流程同时服务两种对象和现有测试调用，运行上下文只处理准备、驱动接入与证据范围。不同配置改变参数，不重新排列步骤；真正不同的流程才新增经过审查的C#实现，不把JSON发展为通用脚本协议。

依赖方向是工具调用游戏接口；公共经营实现不依赖工具窗口、诊断绘制或开发专用类型。只有场景组装处知道要接入哪种工具；公共经营代码不能调用即将被 release 排除的专用成员。

开发专用 partial 部分只增加必要成员，不重新声明钱包、库存、日历等字段，也不复制构造函数或 `AdvanceTick()`。必须经过原状态拥有者的操作，例如调用库存或钱包的现有方法，而不是直接改内部数组。工具界面仍只持有草稿和显示状态。

partial 不能保证业务一致性；即使调用了库存等原模块方法，仍须明确加工领取、冻结资源、排程、日期与表现等实际关联影响。专用操作的约定应由原模块与必要的 `FarmGame` 协调入口封装；跨模块场景检查这些公开结果，不在步骤代码里拼装本应属于经营入口的副作用。

现有 `FarmGame` 声明为 `public sealed class`；只有确认需要专用成员并选择该方案后，才将声明改为 `public sealed partial class`。只读工具如果已有快照就够用，无须为了目录形式而拆分经营类。该分文件示例针对纯 C# 经营类；Node 脚本 `Main` 保持原有脚本文件与场景引用。

如果已确认的某项工具需要共用执行片段，则在原模块中提取一次，正式入口与专用入口共同调用。不能分别维护普通生长与开发生长、普通结算与开发结算。具体是否需要提取要到能力与语义确定后判断。

## 在项目配置中集中选择源码

下列配置片段说明一种可选方式，尚未写入真实项目文件：

```xml
<ItemGroup Condition="'$(Configuration)' != 'Debug' And '$(Configuration)' != 'ExportDebug'">
  <Compile Remove="scripts/**/development/**/*.cs" />
</ItemGroup>
```

本项目 SDK 的 `Debug` 和 `ExportDebug` 包含 `DEBUG`，分别对应编辑器开发与 dev 导出；`ExportRelease` 和普通 `Release` 不包含，见[实测配置表](../../research/developer-tools-builds.md)。上述排除条件与入口使用的 `#if DEBUG` 对齐，避免普通 Release 编译检查残留开发类型引用。它不是新增一套游戏源码，只是选择当前项目的编译输入。

此目录模式同时覆盖模块内的 `development/` 和独立 `scripts/development/`。跨模块场景、报告类、专用入口和开发资源遵循同一排除策略；公共时间能力保留，release不能通过配置解锁开发倍率。运行报告目录已暂定，不能随正式包误带开发工件。本轮不修改真实构建配置。

MSBuild 支持在 `Compile` 中按条件与目录模式移除源码。这里只说明标准机制；项目指定 Godot 的最终类型生成、完整导出与资源内容仍待实施期验收。[Microsoft 排除编译文件](https://learn.microsoft.com/en-us/visualstudio/msbuild/how-to-exclude-files-from-the-build?view=vs-2022)

公共 `Main` 在场景组装处保留一个集中入口，例如以下示意：

```csharp
#if DEBUG
var developerWindow = new DeveloperToolsWindow();
developerWindow.Bind(_game);
_uiRoot.AddChild(developerWindow);
#endif
```

`DeveloperToolsWindow` 与 `Bind` 都是组织示例，当前并无这些类型或成员；创建位置、容器、窗口初始化和布局将按确认后的功能确定。所有工具事件绑定、字段引用与输入注册也必须纳入同一编译范围，不能只围住创建语句而在其他公共成员中引用已排除的类型。该例不改变经营计时或窗口遮挡语义。

开发文件已经按目录排除时，不必每个文件再包一层 `#if DEBUG`。需要条件编译的是公共源码中的开发专用引用；源码选择由 `.csproj` 负责，资源选择由导出预设负责。共享主场景不序列化引用 release 不存在的工具脚本，工具在 dev 组装时接入。

`[Conditional("DEBUG")]` 只决定调用点是否编译，不能替代类型和实现剔除；并且条件方法要求返回 `void`，不适合作为带成功/失败结果的开发经营命令入口。[Microsoft Conditional 说明](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/general#conditional-attribute)

## 架构讨论与验收边界

案例用于比较做法，不能把任何案例的开发工具保留策略当成本项目已经确认的发布规则。当前建议优先使用同项目、按目录选择源码、按原模块补充少量专用入口；若用户选择其他组织方式，先更新设计，不直接改变实现。

选定该组织方式后的实施验收需核对：两个配置的 `Compile` 列表；release 中没有开发专用类型/成员与资源；dev 实际接入工具；同初始化与相同普通操作下经营结果一致；现有测试、逐模块覆盖率及对应平台启动保持通过。本轮未做代码实验、构建或导出，也没有把候选功能纳入首版。
