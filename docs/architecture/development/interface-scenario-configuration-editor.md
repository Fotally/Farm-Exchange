# 流程目录、配置描述与编辑草稿接口

窗口操作见[分步设计与实现](../ui/developer-tools-window/implementation-step-flow-editor.md)。实现位于 `scripts/development/configuration/`；文件持久化过程见[配置库实现](implementation-scenario-configuration-library.md)。

## Module与状态拥有者

`ScenarioConfigurationSchema`拥有字段描述及基础校验，`ScenarioConfigurationDraft`拥有编辑值、原始非法输入、数组顺序与未保存状态，`ScenarioConfigurationLibrary`拥有来源、修订、目录发现与流程可见性。UI接收这些描述与快照，不读取文件、修改DTO、转换整数或计算修订。加载结果`ScenarioConfiguration`继续拥有单次运行的固定参数、原文件引用与SHA-256。

只维护真实流程「买入 → 加工 → 委托卖出」，分类「生产与交易」。目录示意或第二种字段夹具不登记为可运行流程，不引入插件或执行器继承树。

## 配置描述

`new ScenarioConfigurationSchema(type)`只扫描有`JsonPropertyName`的sealed具体类型属性，并要求其具备`ConfigField`中文元数据。`Fields`返回只读描述树：JSON名称`Name`、中文`Label`、说明、分组、单位、范围、只读状态、控件`Kind`、嵌套`Children`和正式值/中文名`Options`。

- 支持string、int32、uint32、有限double、bool、枚举/有限选项、嵌套具体对象、有序`List<具体对象>`、游戏日期与倍率。
- 枚举与字符串有限选项共用`ConfigField.Values/Labels`显式映射正式值与中文名；枚举的Values须为允许的精确成员名，保存仍为成员名。标签数量须与值对应且非空，值不能重复；枚举缺映射、未知成员或错误元数据在扫描时拒绝，不将代码名当作显示名。编辑和JSON读取同样拒绝未知、大小写不符、数字字符串或中文标签冒充正式枚举值。
- 条件字段元数据引用同一对象的字符串属性。当前`run.seed`仅在`target=independent`时可见、必填及输出；`target=current`禁止JSON含seed。
- 来源、摘要、商品推导及日期换算不在编辑DTO中。格式版本、流程和修订为只读字段。
- 字典、抽象/多态类型、递归对象和缺失元数据的序列化属性明确拒绝，不遗漏字段或退回自由JSON。
- `Read(bytes)`严格处理UTF-8（兼容BOM）、未知、重复、缺失字段、JSON类型和整数容量。`Write(value)`共用同一描述校验与编码，条件隐藏字段不输出。游戏日期仍为01–99年、12月、每月28日；时间段终点严格递增，倍率复用原驱动允许规则。

旧三份schemaVersion=1 JSON保持兼容，没有增加名称键或改变字段层次。原料正式值和中文名直接读取唯一作物目录。

## 草稿

`new ScenarioConfigurationDraft(schema,bytes,entry)`构造独立草稿；第三参数可省略，用于第二配置形状的通用表单测试。字段路径使用`run.target`、`parameters.processorAnchor.x`、`execution.timePlan[0].rate`。

| 成员 | 调用约定 |
| --- | --- |
| `GetValue(path)` | 标量保留原生int/uint/double/bool/string；非法输入返回原文本；数组返回int行数，对象返回null，不泄漏可变对象 |
| `SetValue(path,text)` | 按描述解析，返回本字段是否有效；非法输入保留文本及字段错误，原文件保持；只读、隐藏或对象/数组直接修改明确拒绝 |
| `IsVisible(path)` | 查询当前条件显示与输出约定 |
| `Validate()` / `Errors` | 返回按字段路径定位的独立只读错误快照；空集合表示可保存 |
| `IsDirty` | 与加载时的规范编辑值比较；非法输入或不合法时间顺序也为未保存状态 |
| `AddArrayItem(path)` | 使用数组元素具体类型的默认值加入新行 |
| `RemoveArrayItem(path,index)` | 删除该行，移动其余行的非法输入与错误路径 |
| `MoveArrayItem(path,index,target)` | 移动原行及其错误；不自动排序或修正终点日期 |

切换到当前局时隐藏种子，清除隐藏种子的非法输入错误；已解析的种子值仍保留，切回独立局后可继续编辑。数组为空、终点乱序等整体错误明确呈现，不自动补行或调整日期。

## 配置库

构造器接收`builtinDirectory,userDirectory`或`IReadOnlyDictionary<string,byte[]> builtinConfigurations,userDirectory`。只读包资源字节入口允许Godot场景组装加载资源，配置Module不依赖节点。字典键为原示例文件名，字节复制后保存于库内；没有第二份手工维护的示例数据。

| 成员 | 调用约定 |
| --- | --- |
| `Flows` | 只读可见流程摘要，名称、简介、分类和配置描述；删除后为空 |
| `ListConfigurations(flowId)` | 自动发现合法只读示例及用户配置，按名称返回目录快照；未知/隐藏流程拒绝 |
| `DiscoveryErrors` | 列出本轮扫描遇到的非法用户配置；UI须显示，不将其冒充成功配置 |
| `Open(entry)` | 重验归属与原字节，返回独立草稿；过期凭据仍拒绝 |
| `ReloadSelection(entry)` | 用户显式重选后按稳定文件 `Id` 重新发现并打开；返回 `ScenarioConfigurationSelection`，含最新 `Entries`、新 `Draft` 和 `Error`。条目失效时草稿为空，错误明确说明；不退选其他文件，不修改传入条目或旧草稿 |
| `SaveAs(draft,newCaseId)` | 新名称、新文件、修订1；原文件及原草稿保持；拒绝同名配置 |
| `Overwrite(draft)` | 只针对可写条目，重验加载后文件未改变，成功覆盖才修订加一；修订到int32上限拒绝 |
| `DeleteConfiguration(entry)` | UI确认后调用，仅删除所选可写文件；只读示例拒绝 |
| `DeleteFlow(flowId)` | UI确认后调用，删该流程全部可写配置，再持久隐藏目录项；代码、示例与报告保持 |
| `LoadForRun(draft)` | 只允许已保存可写配置且草稿无修改；一次读原字节，生成固定运行参数与摘要 |

条目的`Id`只供库操作，界面显示`CaseId`、`Revision`与`IsReadOnly`。库在操作时验证条目、文件摘要、目录归属与符号链接；不得拿外部路径伪造删除目标。正常拒绝与文件失败抛`ScenarioConfigurationException`供窗口明确反馈。删除失败后UI重新查询真实列表，不假报成功或声称文件已回滚。覆盖与启动冲突保留原草稿，提示用户显式重选；重选统一调用 `ReloadSelection`，不能以刷新后的条目替换旧脏草稿的凭据再覆盖。存在未保存输入时，UI须先取得放弃草稿的明确确认；取消保持原选择和输入。恢复结果一次给出真实目录及对应草稿，删除或非法配置清空选择并禁用运行；目录扫描错误仍由 `DiscoveryErrors` 显示。显式重选不会改变已启动流程的固定参数。

## 唯一可写位置

`ResolveUserDirectory(projectRoot,executablePath,exported)`返回编辑器仓库的`tests/scenario-configs/user/`，Windows开发包exe旁`configs/scenario-configs/`，macOS开发包`.app`父目录同名路径。用户配置是长期文件，允许Git跟踪；游戏保存不自动git commit。首次保存才创建目录，不在不可写时替换位置。测试使用显式注入的唯一临时目录，不在真实仓库用户目录生成验收配置。

本Module属于开发编译范围，正式Release排除；公共倍率按钮不依赖本Module。
