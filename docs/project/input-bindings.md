# 键鼠操作登记与冲突核对

关联 [#153：大小地图导航与键鼠操作登记](https://github.com/Fotally/Farm-Exchange/issues/153)。本表记录正式代码的输入归属，供新增操作前查重；本批次的运行验收状态见[大小地图规划](map-navigation-proposal.md)，玩家操作见[地图与镜头](../gameplay/world/map-and-camera.md)。

## 主场景操作

| 输入 | 动作及生效范围 | 归属与限制 | 实现入口 |
| --- | --- | --- | --- |
| WASD／方向键，持续按住 | 平移主镜头 | 可编辑文字／数字框有焦点或大地图展开时停止；普通按钮与只读焦点保持原平移 | `CameraController._Process`、`SetInputEnabled` |
| 左键短按 | 主地图选格；设施摆放；道路工具选择起终点 | 按下未被界面接收，拖动不提交；小地图和大地图另行接收 | `CameraController._UnhandledInput`、`Main` |
| 左键拖动／中键拖动 | 主地图平移镜头 | 左键达到 8 个逻辑视口像素转为拖动；大地图展开时禁用 | `CameraController._Input`、`_UnhandledInput` |
| 滚轮上／下 | 主地图以镜头中心缩放，1.25～2 倍 | 仅未被界面处理的滚轮；小地图吞掉，大地图独立缩放 | `CameraController._UnhandledInput` |
| R | 农田／加工候选旋转 | 拒绝重复、Ctrl／Alt／Meta 和 LineEdit／TextEdit 焦点；原规则允许 Shift；大地图展开时不分发 | `Main._Input` |
| 右键按下／Esc | 有道路段先取消段，无段则退出工具 | 大地图展开时右键属于大地图，Esc 只关闭大地图 | `Main._Input`、`CancelCurrentSegmentOrPlacement` |
| Esc | 非摆放时关闭一个窗口 | 按开发工具（DEBUG）、年度表、委托、市场、库存、选种、建造、详情顺序；大地图先关闭且不穿透 | `Main._UnhandledInput` |
| 左键拖动标题栏 | 移动对应窗口 | 原始输入捕获移动和释放；打开大地图调用 `CancelDrag`，保留窗口位置和内容 | `DraggableWindow.StartDrag`、`_Input` |
| 左键点击／拖放年度条目 | 选中、编辑与拖放年度条目 | Godot GUI 拖放；打开大地图调用 `Viewport.GuiCancelDrag` 结束旧拖放 | `CultivationTimeline` |
| Esc（开发确认面板） | 取消确认面板 | 确认面板可见时禁止打开大地图；确认内容仍由开发窗口维护 | `ScenarioConfirmationPanel._Input`、`DeveloperToolsWindow.HasVisibleConfirmation` |

来源：[镜头代码](../../scripts/world/CameraController.cs)、[主场景代码](../../scripts/ui/Main.cs)、[窗口拖动](../../scripts/ui/DraggableWindow.cs)、[年度时间图](../../scripts/ui/CultivationTimeline.cs)、[开发确认面板](../../scripts/ui/development/ScenarioConfirmationPanel.cs)。暂停、倍率、建造目录、库存、市场仍以按钮操作，不额外分配 Space、B、I 等键；文本编辑和按钮激活的内建操作不是空闲键位。

## 大小地图操作

| 输入／入口 | 动作 | 归属与限制 | 实现入口 |
| --- | --- | --- | --- |
| M／展开按钮 | 打开大地图；M 再次关闭 | M 忽略自动重复及 Ctrl／Alt／Shift／Meta，文字编辑时不响应；主局未就绪或确认框可见不能打开 | `Main._Input`、`SetMapExpanded` |
| Esc／返回按钮 | 关闭大地图 | Esc 同次只关闭地图，不关闭背景窗口 | `Main._Input`、`MapNavigation.CloseRequested` |
| 小地图左键点击／拖动 | 定位主镜头 | 固定全图，点击不展开；不穿透选格、建造或拆路 | `MapNavigation.NavigateRequested`、`CameraController.NavigateTo` |
| 小地图滚轮 | 消费滚轮 | 不改变任何地图倍率 | `MapNavigation` |
| 大地图左键短按 | 定位主镜头并保持展开 | 菱形外忽略；不提交经营命令 | `MapNavigation.NavigateRequested` |
| 大地图左键拖动 | 浏览大地图底图 | 达到 8 像素判为拖动，返回起点也不短按定位 | `MapNavigation` |
| 大地图右键拖动 | 辅助浏览底图 | 不移动主镜头、不触发背景取消 | `MapNavigation` |
| 大地图滚轮 | 以鼠标为锚点缩放 | 每档乘／除 1.25，全图适配的 1～12 倍，不改变主镜头倍率 | `MapNavigation` |
| 加减／全图／定位当前镜头按钮 | 辅助浏览 | 加减以视图中心缩放；全图恢复 1 倍居中；定位保留倍率 | `MapNavigation` |

大地图首次打开全图，本次局内关闭重开恢复倍率与浏览位置，新局重置。开关地图不修改暂停；打开会取消全部建造工具与道路草稿，保留其他窗口、编辑内容和位置。背景键盘焦点暂时释放，关闭后恢复仍有效且可见的原焦点。视野框来自完整实际视口，包含被 UI 遮挡的部分。

## 输入归属与验收

同一手势只归属起始控件。地图拖出控件后继续捕获至松开；离窗、失焦或关闭结束手势且不定位。背景镜头门控同时覆盖 `_UnhandledInput` 和全局按键轮询，不能仅凭 GUI 消费键盘事件判断隔离完成。

需要回归文字框内 M／R／WASD、M 自动重复与组合键、确认框阻止打开、未完成道路段打开后的工具取消、背景窗口及年度条目拖动取消、Esc 单层关闭、小地图与大地图滚轮分流、拖回起点和跨控件释放、离窗失焦、重开记忆与新局重置、开关不改变暂停。测试定义与本轮实际通过记录分开维护，不把文档登记当作已完成运行验收。

## 独立场景

| 场景 | 输入 | 动作 | 入口 |
| --- | --- | --- | --- |
| NPC 预览 | WASD／方向键 | 移动预览人物 | `NpcPreview._PhysicsProcess` |
| NPC 预览 | Q／E | 切换预览角色 | `NpcPreview._UnhandledInput` |
| NPC 预览 | 数字 1～5 | 切换五种预览动作 | `NpcPreview._UnhandledInput` |

来源：[NPC 预览代码](../../scripts/ui/NpcPreview.cs)。独立场景键位与主场景分开登记，移植功能时需重新核对。[1080P 原型](../architecture/ui/main/prototype-maps.md) 中左右方向键切换 A／B／C 仅供演示，不占用正式游戏键位。

## 维护方式

新增或修改操作时同步动作、生效上下文、优先级、实现入口；先查同键和组合键，再查手势跨控件行为。正式生效规则同步玩法与接口文档，运行证据由相应 issue 验收记录维护。本表不引入改键设置、通用输入框架或自动冲突检查器。
