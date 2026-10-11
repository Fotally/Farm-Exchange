# 局部天气表现与验收

履行 [WorldMap 接口](interface-world-map.md)，业务天气状态由天气模块唯一维护。本实现属于 [#34](https://github.com/Fotally/Farm-Exchange/issues/34)，不增加玩家天气控制或预报。

## 绘制与时间

`WorldMap` 挂载内部 `WeatherPresentation`，每帧传入当前真实活动矩形、地图本地可见范围、暂停和同局倍率。基础格矩形通过共享等距坐标转换成菱形，再裁切到可见矩形；仅对裁切后区域绘制低透明度灰蓝阴影。多云和降雨使用不同透明度，不加载新图片。天气层位于设施及人物之上、选择和建造反馈之下，主界面独立 `CanvasLayer` 不受影响。

降雨在可见范围内使用稀疏网格及固定视觉哈希确定落点；落点必须属于当前真实雨区。落下阶段的两个线段端点也重验真实雨区和可见范围，落地阶段显示短小横线。雨点最多180个，网格间距同时受屏幕像素密度限制；不会按经营倍率增加发射量，不保存离屏积压。视觉哈希不读取或改变天气预排及农田产量的随机序列。

0.5×/1×/2×按同局倍率推进视觉时间，其他开发倍率采用1×视觉；暂停不增加视觉时间且不改变当前雨线／落地阶段。镜头移动、缩放和区域真实状态改变时刷新裁切与落点，离屏区域不绘制。云影没有额外随机动画，晴天不绘制天气层。供水与产量仍完全由真实经营读取同一份天气判定，视觉不会执行这些命令。

农田详情只新增「当前天气」，按真实九格占地读取雨＞云＞晴的整田判定，悬停解释跨界口径；原产量字段和选种卡统一称「基准产量」，保留定义数值。实现及节点约定见[详情](../../ui/farm-details-panel/interface-farm-details-panel.md)和[选种](../../ui/crop-selection-window/interface-crop-selection-window.md)。

## 自动化入口

全局构建与引擎运行由主任务串行执行，以下为可复验入口，不代表尚未运行的测试已经通过。

`tests/integration/TestWeatherPresentation.cs` 的 `RunChecks(Node)` 纳入 headless 套件，独立场景为 `res://tests/integration/test_weather_presentation.tscn`。通过真实显式天气局与地图绑定验证：默认晴天无天气绘制、局部云雨、落点位于真实雨区、暂停冻结、四种倍率的密度上限、离屏清空、绘制不改变日期／库存、详情读取当前整田天气及作物基准量。

既有 `TestWorldArt`、`TestFacilityOrientations`、`TestWorkerPresentation` 与 `TestCultivationWindow` 的降雨夹具改用显式天气局及正式区域设置／清除输入，不再向经营推进传入旧布尔值。美术原图核验在供水经营秒后清除夹具雨区，保留真实湿土而避免天气遮色影响原图像素比对；真正云雨画面由上述独立天气场景验收。年度表首轮验证累计真实成功收获结果并与公共库存守恒核对，不再使用旧6份固定产量。

有窗口性能场景为 `res://tests/performance/test_weather_fps.tscn`，使用1920×1080真实主场景及满地图16,384个生产实例，显式设置互不重叠的雨／云／离屏雨三区。主场景的 DEBUG `InitialWeatherMode` 仅供夹具在挂树前选择显式局，默认仍为自然天气；夹具创建完即恢复配置。默认1×，用 `--weather-rate` 指定0.5、1、2或16。每轮晴天截图后设置天气，天气预热2秒后截图，再独立采样8秒；截图读取不进入采样区间。报告同时记录真实经营推进、雨点上限、区域数及UI纸面内部像素不受天气影响的检查。

```powershell
& 'E:/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe' --path . res://tests/performance/test_weather_fps.tscn -- --weather-rate 1
```

按相同命令分别将末尾倍率设为0.5、2、16。截图和JSON报告保存到 `coverage/weather-0-5x/`、`coverage/weather-1-0x/`、`coverage/weather-2-0x/`、`coverage/weather-16-0x/`。每个目录包含 `before-sunny.png`、`after-cloud-rain.png`、`end-cloud-rain.png` 和 `performance.json`。平均FPS须至少60，P95帧间隔须不超过16.67ms；失败返回非零退出码。截图只作为本地构建证据保存，不重复纳入文档库。

同场景晴天性能对照可追加 `--weather-baseline`：仅不设置云雨三区，保持种子34、满地图负载、镜头、倍率、UI、预热和采样流程一致。详情应显示晴天，活动区、云影与雨点均须为零；纸面UI不受染暗的检查继续执行。对照报告单独保存到 `coverage/weather-16-0x-clear/`（其他倍率同样追加 `-clear`），包含 `before-sunny.png`、`after-sunny.png`、`end-sunny.png`、`performance.json`，不会覆盖云雨场景失败证据。报告中的 `WeatherBaseline` 明确标识对照，性能门槛保持相同。

16×对照曾发现晴天与云雨均有经营刷新帧峰值，用户确认将现有地图的等价空间同步优化纳入#34：占地快照引用未变时跳过道路全图赋值、设施删除集合和环境占地清理，保留动态外观及可见局部动效同步。实现和行为回归见[分块缓存](implementation-chunk-cache.md)。优化后的晴天及云雨仍分别运行相同场景、相同门槛和完整套件，不以降低负载或更改经营刷新时机满足验收；实际通过数据由主任务记录。

```powershell
& 'E:/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe' --path . res://tests/performance/test_weather_fps.tscn -- --weather-rate 16 --weather-baseline
```

## 真实调用阶段诊断

同场景另提供独立 `--weather-stage-probe`，不进入普通TestSuite，也不修改默认FPS场景的负载、时间或门槛。诊断保持种子34、显式云雨三区、满地图与固定中心镜头，固定16×；停止主场景自动 `_Process` 驱动，在两帧真实布局后解除暂停并在样本外预热三个正式接口。

随后依次各采8个样本：`Game.AdvanceTicks(1)`纯经营、`WorldMap.SyncFromGame()`地图同步、`Main.AdvanceSimulation(1/16)`完整16×推进及原有地图/UI刷新。地图同步不推进业务；预热只额外推进两个经营秒，正式样本只推进16秒，含夹具初始化的累计经营秒不超过26，避免加工首批结束混入该对照。每阶段GC在采样外执行。逐样本记录单调时间与当前线程分配，保留固定先后顺序；该诊断不包含GPU/原生节点分配或后台日志线程，也不作为FPS通过证据。

```powershell
& 'E:/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe' --path . res://tests/performance/test_weather_fps.tscn -- --weather-stage-probe
```

报告为 `coverage/weather-stage-probe/performance.json`，记录种子、阶段顺序、预热前后及结束经营秒、每个样本耗时／分配和平均值。诊断不能与 `--weather-baseline` 同用；实际FPS仍通过前述晴天／云雨完整场景重新验收。
