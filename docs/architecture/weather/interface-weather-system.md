# 天气模块 Interface

`scripts/weather/` 使用 `FarmExchange.Weather`。`WeatherSystem` 唯一拥有预排表、当前执行位置及活动矩形，调用方不重建天气规则。现行数据与玩家行为见[地区天气](../../gameplay/world/weather.md)，预排实现见[循环排程](implementation-weather-schedule.md)。

## 类型与状态

- `WeatherKind`：`Sunny`、`Cloudy`、`Rain`，首版互斥。
- `WeatherMode`：`Natural`、`Explicit`，在构造局时确定，不在运行中切换。
- `WeatherRegionSnapshot`：只读值 `Cells: Rect2I`、`Kind: WeatherKind`。矩形使用基础格坐标，右／下边不包含；只返回当前云雨，不包含未来预报或永久分区 ID。
- `WeatherSystem.Mode` 与 `CycleSeconds`：本局模式及当前配置周期经营秒数。
- `ActiveRegions`：只读独立快照缓存；后续推进、设置、清除不改写已返回快照。没有活动区域表示全图晴天。

## 调用顺序

`new WeatherSystem(int seed, WeatherMode mode = Natural, uint elapsedSeconds = 0)` 使用独立天气种子；自然局在构造时一次生成全部周期事件，显式局没有自然事件。创建时同步已有经营时刻，零秒新自然局全图晴天；不能消费开局作物或市场已有随机流。

经营协调在时刻变化后调用 `AdvanceTo(uint elapsedSeconds)`，同步到不早于当前的累计经营秒。允许一次跨越多事件／周期，运行中不抽取随机数。天气同步本身不供水、不推进农田；经营入口按照原相位读取同一天气状态执行生产。倒退时刻及非法模式明确抛出参数异常。

`GetNextEventSeconds(uint now)` 返回严格晚于 `now` 的下一天气生效或结束距离；无事件返回 `uint.MaxValue`。协调入口将它与工人、农田、季节及行情事件一起参与批量推进边界。第一周期不报告尚未存在的前周期尾部结束事件。

## 查询与显式设置

| 成员 | 调用方约定 | 结果 |
| --- | --- | --- |
| `GetWeather(Vector2I cell)` | 基础格在地图内 | 当前互斥天气；越界明确抛出参数异常 |
| `GetFarmWeather(BuildingSpaceSnapshot farm)` | 土地模块真实农田空间快照 | 遍历全部占地，雨＞云＞晴；非农田参数拒绝 |
| `TrySetRegion(Rect2I cells, WeatherKind kind, out string reason)` | 显式局，正宽高且完整在地图内，多云或雨 | 完整检查上限与互斥后一次登记；失败快照与状态零修改 |
| `TryClearRegion(Rect2I cells, out string reason)` | 显式局，矩形与已有活动区完全一致 | 整片清除并恢复默认晴；不存在／部分清除拒绝且零修改 |

整田查询内部按固定几何锚点缓存只读派生结果，命中前仍确认实例类别为农田。同锚点拆除重建保持土地模块定义的固定农田几何，可以复用同一天气结果；缓存不持有实例对象、水分或生产状态。活动快照真正改变后失效：显式设置／清除成功及自然事件／循环状态变化同步清除派生结果；失败修改及无变化推进保留当前结果。

自然局拒绝显式修改，不允许修改预排表。显式区域没有自然到期，推进后保持直到清除；不增加覆盖恢复或区域叠加。成功 `reason` 为空，正常拒绝为中文原因。

## 正式配置与失败

`WeatherRules.Default` 从 `res://assets/gameplay/weather/weather_rules.json` 只加载一次；所有导出预设明确包含该 JSON。`WeatherRules.Parse(string json)` 是配置检查入口，拒绝未知字段、缺失有效数值、非法权重、日历不一致、逆序区间、地图外尺寸或非法周期。加载／解析失败直接抛出异常，不静默替换默认表或运行补排。

公开配置值是数据描述，不是运行中修改局排程的接口；数据修改需同步现行天气文档与验收。

## 责任划分与验收

天气不拥有农田水分、作物进度、每轮随机产量、库存或动画。农田的天气积分沿用实际消耗的精确时间单位，末秒裁到剩余量；该状态由农田模块维护。世界只读取当前区域和暂停／倍率，农田详情读取同一整田查询。

`tests/unit/TestWeatherSystem.cs` 通过公开 Interface 检查配置、显式失败零修改、快照只读、整田九格优先级、完整两周期事件、接缝持续、多个种子与不同批量同步等价；边界冷／热查询、同锚点农田改建道路与重建、显式变更及自然事件／循环失效均与九格位置查询逐项比对。全局覆盖率由统一测试入口检查 `scripts/weather` 至少 80%。
