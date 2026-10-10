# ProcessorDetailsPanel 接口

对应 `scripts/ui/ProcessorDetailsPanel.cs`，由 `Main` 放在地块详情窗口内。`Refresh(ProcessorDetailsSnapshot)` 只更新现有状态、原料与产物关系、加工周期、加工品当前报价和库存标签；`RemoveRequested` 把移除意图交给 `Main`。

状态语义由 `FarmGame.GetProcessorDetails` 提供，面板映射为等待对应原料、等待原料超过保留底线、待领取原料和加工中。降低底线后到下次领取前显示待领取原料；不通过库存或倒计时自行重判原因。移除按钮保留 `RemoveButton` 名称；农田字段由独立的农田详情面板显示。

详情按「当前状态 → 本批真实剩余与时间进度 → 一份原料到一份产物的关系与周期 → 加工品报价及库存 → 下一步 → 低强调移除」。下一步说明按照经营入口的 `ProcessorStatus` 展示等待收获或买入原料、调整底线、等待下一次领取、完成后市场出售等实际入口；面板不自行重判状态，也不执行买卖或修改底线。

调用顺序为 `Refresh(ProcessorDetailsSnapshot)` 后立即 `RefreshProgress(PlotSnapshot)`，两份快照必须来自同一加工实例。`Main` 使用当前已选格的地块快照调用进度入口，无需新增经营查询。`ProcessorActualProgress` 把真实剩余秒向上取一位游戏天数；`ProcessorProgress` 从本批剩余秒与定义中的加工周期换算，仅有活动批次时显示，没有批次时明确说明。进度受快照秒数取整精度限制，不显示虚构精确百分比、不在渲染帧计时。

移除按钮前明确说明「丢失已投入原料和未完成批次，不退还建造费」。`tests/e2e/TestUiFacilities.cs` 通过真实买入与经营步进启动批次，核对实际剩余，并验证暂停刷新不推进进度、恢复经营后进度更新。

## 原型比例复刻

`RefreshPlacement(BuildingSpaceSnapshot, CropKind)` 把同一实例的空间与匹配作物传给 [FacilityPreview](../facility-preview/interface-facility-preview.md)，显示 UI 工坊图、实际占地与锚点；该调用与 `Refresh`、`RefreshProgress` 使用同一已选实例。标题与 `ProcessorCurrentStatus` 短状态徽标并排，下一步说明保留等待原料、保留底线或待领取的完整语义；进度条基准高度 6，原料到产物关系旁使用库存箱线条图标。

`ProcessorProductStock` 和 `ProcessorProductPrice` 分别显示纯整数库存、两位小数价格，单位单独显示，左右并排指标保持真实加工品口径。商品名从匹配作物与产物关系确定，悬停数字还可查看具体加工品名称。`ProcessorInventoryButton` 发出新增 `InventoryRequested` 意图，`Main` 使用既有库存入口打开管理窗口，面板不持有窗口或修改库存。

按主稿的 1080 像素高度归一，默认标题 27、状态及普通说明 14、周期和损失弱说明 12、指标数字 27、指标标题及单位 13、主要动作文字 16／高度 56；行间距和指标横向间距 11，指标内边距横向 12／纵向 11。低强调移除高度 35、文字 13，缩略高度仍为 107。图标和插画由整体 UI 倍率控制，文字通过独立字体倍率调整；共享 `UiScaling` 完成真实排版，窗口扩大或全屏不会放大默认控件。字体增大后的长内容继续通过外层滚动到达。

标题、`ProcessorCurrentStatus`、`ProcessorProductStock`、`ProcessorProductPrice` 与各自 `Unit` 标签保持单行，数字使用指标内实际剩余宽度，单位保留自身最小宽。库存箱后的 `ProcessorRelation` 则扩展到实际行剩余宽度，可按真实宽度折行。验收检查状态、数字和单位实际只有一行，以及关系说明的宽度和行数，避免窗口边界正常但内容逐字竖排并挤出操作按钮。

标题节点为 `ProcessorBuildingTitle`，纳入实际单行排版检查。指标内边距的父节点是 `PanelContainer`，由容器直接提供完整可用区域；没有依赖非容器按钮自行布局的说明卡。
