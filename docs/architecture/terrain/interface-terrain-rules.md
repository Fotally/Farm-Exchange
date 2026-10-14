# 地形配置接口

关联 [#110](https://github.com/Fotally/Farm-Exchange/issues/110)。实现为 `scripts/terrain/TerrainRules.cs`，正式配置为 `assets/gameplay/terrain/terrain_rules.json`。本页只说明配置解析和开局保护校验，不表示岸线生成、导航或经营接入已经验收；完整范围及已确认数值见[水域规划](../../project/water-terrain-proposal.md)。

## 状态归属与调用

`TerrainRules` 属于地形 Module，唯一解释配置字段；配置实例在私有构造中完成验证后发布，属性和嵌套值全部不可变。地图尺寸仍由经营 Module 提供，配置不复制工速、开局设施坐标或工人起点。`TerrainMap` 的完整创建流程负责封装加载、生成和导航，不要求经营、表现或测试分别解释 JSON。

| Interface | 约定 |
| --- | --- |
| `Load(source, mapSize, openingCells, workerStarts)` | 每次打开文件并完整校验，不设全局缓存；文件修改只影响下一次读取，已有配置不变 |
| `Parse(json, source, mapSize, openingCells, workerStarts)` | 正式与测试共用字段、数值及上下文检查；`source` 必须非空，用于诊断 |
| `ValidateContext(mapSize, openingCells, workerStarts)` | 显式配置用于新局时再次检查实际地图尺寸和开局保护，尺寸必须与解析时一致 |
| 只读字段 | 对应 JSON 的全部输入；另提供 `Source`、`MapSize`、`ProtectionCells`、`TotalPondCount`、`IsAllLand` |

`openingCells` 必须是实际设施的**完整占地格**，由 `BuildingFootprint` 或实际空间快照展开，不能只提供锚点；`workerStarts` 是真实工人脚根的基础格平面坐标。两组输入必须非空，验证后不保存集合引用；后续修改传入集合不会改变配置。调用方须提供真实上下文，不能伪造空布局绕过检查。

读取、语法、字段和业务校验失败统一抛出 `JsonException`，消息包含来源、字段路径和原因；不补缺失值、不忽略未知字段、不截断参数、不生成替代配置。配置有效只代表配置阶段检查通过，最终池塘、实际水面面积、净空和连通仍须由完整创建验证。

## 字段与数值约束

所有对象严格区分大小写，并拒绝缺失、未知和重复属性。`SchemaVersion`、`GenerationVersion` 当前仅接受整数 `1`，后者是拟实施生成版本的输入声明，不表示已完成生成认证。数量、边缘格数和保护宽高为 32 位整数，不能用小数或字符串代替。

其他数值使用 `decimal` 精确保留十进制输入；允许可无损表示的指数记法及冗余末尾零。无法无损表示的超长小数、溢出、非数值和非有限输入明确拒绝，解析器不把细小半径舍入成零或近邻值。此为数值表示约束，默认参数均可精确表示。导航可将十进制尾数和尺度转为精确有理数；候选筛选若转浮点，不能将筛选近似当作最终认证。

| 字段 | 校验及含义 |
| --- | --- |
| `Small/Medium/Large.Count` | 非负；总数用宽整数派生，不另配总数 |
| 各档 `LongAxis/ShortAxis.Min/Max` | 完整轴跨度，`0 < Min ≤ Max`；范围中必须存在长轴不小于短轴的组合。生成仍须逐候选满足实际长短轴关系 |
| `WaterCoverage.Target/Min/Max` | `0 ≤ Min ≤ Target ≤ Max < 1`；最终水面面积比例，禁建格面积不代替它 |
| `MinimumPondSeparation` | 非负岸线距离 |
| `MapEdgeLandWidth` | `0..floor((mapSize-1)/2)` 的整数完整陆地格数 |
| `MaximumOutlinePerturbation` | `[0,1)` 的半轴扰动比例 |
| `CenterProtection.Width/Height` | `1..mapSize` 的整数格数 |
| `CenterProtection.ShoreClearance` | 非负岸线退让 |
| `NearPond.LongAxis/ShortAxis` | 参考椭圆完整轴长；长轴不小于短轴，分别落在小池塘两轴范围内 |
| `NearPond.CenterDistance.Min/Max` | `0 < Min ≤ Max`；最终最近岸线距地图中心的距离 |
| `WorkerFootRadius` | 正数，直径小于地图边长，实际起点圆脚底完整在中心保护区内 |

正式 JSON 默认保留 16/24/8 座、小中大三档已确认尺寸、水面 .08/.06/.10、间距 8、边缘 8、扰动 .15、保护 24×24 和退让 2、近景 10×7 与距离 14～18、脚底 .125；配置文件是项目自行维护的规则数据，不是美术包复制素材。

保护区每轴使用 `start=floor((N-S)/2)`，结束格为 `start+S-1`。默认 `180..203`，连续外边界 `179.5..203.5`；无法精确居中时固定向较小坐标偏半格，不扩大保护区。所有开局占地必须包含在格区间中，圆脚底还须完整包含在连续外边界中。

含水配置三档数量均须为正，水面下限也须为正；近景占一个小池塘名额。唯一已支持的零数量组合是三档数量与三项水面比例全部为零，此时 `IsAllLand=true`，近景停用。部分档为零的组合拒绝。纯陆地仍检查全部字段、尺寸关系、保护区、真实开局和脚底，不作为含水生成失败的替代结果。

含水配置还拒绝可直接证明的冲突：最小轴跨度超过边缘留陆后内方形对角线、岸距无法容纳多座池塘、近景距离上限小于保护区最近外边界加退让、水面下限超过边缘留陆后可用面积。通过这些必要条件不保证完整布局可生成，不能据此放宽生成约束。

## 验证入口

`tests/unit/terrain/TestTerrainRules.cs` 显式选择纯陆地配置创建真实 `FarmGame`，从空闲开局快照展开设施和工人输入，检查正式默认、合法变体、纯陆地、奇偶保护居中、输入及配置对象隔离；配置单元不等待含水导航。逐对象覆盖未知、缺失、重复字段，覆盖类型、版本、范围、无损小数和跨字段拒绝。临时文件测试验证重新读取和非法文件不影响原配置。

独立场景为 `tests/unit/terrain/test_terrain_rules.tscn`，同时纳入 `tests/TestSuite.cs` 的统一 headless 与覆盖率入口。完整游戏构建、覆盖率、生成及导航测试由 issue 主流程统一记录，本配置单元不单独宣称通过其余水域门禁。
