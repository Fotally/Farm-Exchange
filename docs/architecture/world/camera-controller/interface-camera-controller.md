# CameraController 对外接口

对应类型：FarmExchange.World.CameraController，代码位于 scripts/world/CameraController.cs。作为 Godot Camera2D 节点，它接收输入，使用 WorldMap.SelectAtScreenPosition 完成短按选格，使用 WorldMap.ClampGlobalCameraCenter 限制全局镜头位置。

左键按下到释放的移动距离达到 8 个逻辑视口像素时转为拖动，不再发出选格；释放立即结束拖动，即使释放事件被界面拦截，也在下一帧校正。中键拖动、滚轮缩放、WASD 和方向键移动由该模块处理。玩家倍率范围为 1.25～2 倍，开局为 1.25。

`IsDragging` 只读报告左键实际拖动或中键拖动，`Main` 用它隐藏放置预览；左键尚未达到阈值不算拖动。`IsMouseInsideWindow` 由窗口进入/离开信号维护，供协调入口决定预览可见性；离窗清除鼠标按下与拖动状态，退出场景取消窗口信号订阅。

原始 `_Input` 按下先清除上一轮左键意图，只有进入 `_UnhandledInput` 的地图按下才登记新点击。因此 UI 按下后地图松开不能确认；地图按下在 UI 松开由原始释放清除，不能留下下一轮点击意图。中键释放被界面截获时也停止拖动，逐帧物理按键检查继续校正左/中键状态。右键交由 `Main` 统一取消摆放，不由镜头消费。

## Module 与 Interface

`CameraController` 是显示与输入的 Module：场景配置初始位置、初始 `Zoom`，提供同级 `WorldMap`；引擎传入视口尺寸变化和输入。窗口适配、倍率限制、鼠标拖动换算、键盘速度和镜头位置限制由同一 Implementation 完成。调用方不需要按顺序执行“读窗口比例、重设倍率、重新限制位置”。`Main` 和 UI 不持有第二份玩家倍率，也没有新增配置或适配命令。

运行后 `Camera2D.Zoom` 是抵消视口拉伸后的引擎倍率，不再直接表示玩家倍率；初始场景值只用于初始化内部玩家倍率。正常操作通过已有滚轮输入完成，不在场景协调或 UI 中直接改写运行时 `Zoom`。全局镜头位置仍沿用 `Camera2D.GlobalPosition`。

## Implementation 与验收

Module 内部唯一保存玩家倍率；初始化及视口尺寸变化时，按视口拉伸比例换算引擎倍率。窗口放大时同一倍率下格子、设施及人物的屏幕像素大小保持，镜头中心与玩家选择保持；退出场景时取消视口尺寸订阅。UI 的既有画布拉伸方式保持。

拖动使用逻辑视口位移及引擎倍率，键盘移动使用玩家倍率，让同一物理鼠标拖动距离和相同键盘时长的地图位移不随窗口大小改变。尺寸变化只更新镜头，`WorldMap` 通过已有画布变化检测更新块可见性，经营状态不变。

`TestCameraInteraction.RunChecks` 检查既有选择、拖动释放及边界；`RunResizeChecksAsync` 通过真实主场景、窗口尺寸、公开输入和可见画布变换检查 1280×720、1920×1080、2560×1440 及往返、玩家缩放保持、上下限、UI 比例、点击、拖动、键盘平移与大窗口冷启动。有窗口运行另检查真实全屏，并保存窗口和全屏截图；汇总套件等待异步检查与窗口恢复完成。

它不计算等距坐标、不修改经营状态；输入的玩家可见行为见[地图与操作](../../../gameplay/world/map-and-camera.md)。tests/integration/TestCameraInteraction.cs 检查镜头与地图协作。

#130 通过内部 PlayerZoom 读取模块持有的真实玩家倍率供表现诊断；它不是抵消画布拉伸后的 Camera2D.Zoom。Main 只在检测到实际视图变化且局级 ViewChanged 入选时记录，不改变镜头输入或缩放规则。
