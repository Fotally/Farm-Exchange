# FarmDetailsPanel 接口

对应 `scripts/ui/FarmDetailsPanel.cs`，由 `Main` 放在地块详情窗口内。`Refresh(FarmDetailsSnapshot)` 只更新现有状态、周期、每轮产量、原料当前报价和库存标签；`ChangeCropRequested`、`RemoveRequested` 把按钮意图交给 `Main`。

状态语义由 `FarmGame.GetFarmDetails` 提供，面板把等待工人、湿润待播种、等待浇水、生长中、不适季与计划休耕映射为玩家文案；预计剩余时间不足显示“可播种、有枯萎风险”，不表示禁止播种。周期显示“获得水后 N 天成熟”。

`RefreshCultivation(FarmGame, Vector2I)` 读取本田引用、实际剩余时间及预备安排；实际剩余换算为游戏天数并向上取一位小数，不向玩家展示经营秒。待水时明确完成日期尚未确定。`PrepareCropRequested` 报告保留本轮的手动预备意图，`CultivationRequested` 打开共享表窗口。`ChangeCropButton` 和 `RemoveButton` 保留原节点名，新增 `PrepareCropButton` 与 `FarmCultivationButton`。两种手动操作的损失及解除引用提示在作物窗口选择前显示。加工字段由独立加工详情面板显示。

`FarmCultivationStatus` 标签展示预备日期。初年春初引用冬春连续条时，计划原起点可能在游戏时间零点之前；按年度位置显示“上一年度 X月X日”，不转成无符号累计日期、不夹到开局日、不虚构第0年。非负绝对起点继续显示真实的“第N年 X月X日”。

#94 按「当前状态 → 真实剩余与水分 → 本轮时间进度 → 周期与每轮产量 → 原料报价及库存 → 年度表与下一轮 → 下一步操作」组织详情。`FarmActualProgress` 独立显示实际剩余、待水或未播种；`FarmProgress` 仅在生长中可见。进度条依据同一 `PlotSnapshot.RemainingSeconds` 和作物定义周期换算，并受剩余秒向上取整的精度限制，不伪造精确完成百分比或确定收获预测；动画和刷新都不推进经营。

共享年度表标签继续保留 `FarmCultivationStatus` 节点名及预备日期语义。预备下一轮为主按钮，立即改种与年度表为纸色次按钮；末尾低强调移除按钮上方明确说明「丢失本轮未收获作物，不退还建造费」。长详情由外层滚动承载，现有事件签名、按钮节点名和经营命令保持。`tests/e2e/TestUiFacilities.cs` 以真实经营推进后的快照核对剩余天数，原有主场景及年度表测试检查改种、预备和移除效果。

## 原型比例复刻

设施图由 [FacilityPreview](../facility-preview/interface-facility-preview.md) 显示在最前方，含实际 `3 × 3` 占地与锚点；作物标题和 `FarmCurrentStatus` 短状态徽标同一行。真实剩余、6 像素基准进度条、水滴图标及水分说明分层排列，周期文字使用次级字体；禁生季与越界风险的完整原因仍在说明中显示。

`FarmHarvestQuantity` 和 `FarmRawPrice` 两个数值分别放入并排指标，数字与「份／金币」单位分开，避免把长报价句子装进大框；`FarmRawPrice.Text` 只含两位小数金额。原料库存作为次级说明保留。年度表按钮包含日历、当前引用／下一轮及箭头，不新增日历状态。立即改种和预备下一轮采用并排的次／主按钮，详细接管影响仍在选种窗口选择前明确显示，按钮悬停也说明影响。

按主稿截图的 1080 像素高度归一，基准标题 27、状态与普通说明 14、弱说明 12、指标数字 27、指标标题及单位 13、动作文字 16；行间距及指标横向间距 11，指标内边距为横向 12／纵向 11。设施缩略高度保持 107，年度入口高 72，两个主要动作高 56，低强调移除高 35、文字 13。几何和图标只跟随整体 UI 倍率，字体使用独立字体倍率；窗口扩大与全屏不放大这些默认尺寸。两种倍率由共享 `UiScaling` 应用，不在详情中另设倍率状态。字体倍率增大后的长详情通过外层滚动到达。

横向排版明确区分单行与可折行：标题、`FarmCurrentStatus`、`FarmHarvestQuantity`、`FarmRawPrice` 及各自 `Unit` 标签关闭自动折行，短状态、金额和单位保持完整一行；数字取得指标内剩余宽度，单位保留自身真实最小宽。`FarmWaterStatus` 占满水滴图标后的剩余宽度，允许按实际行宽折行，不形成逐字竖排。图形验收同时检查这些标签的行数与实际宽度，不能仅以外层窗口边界判断排版完成。

年度表入口中的 `FarmCultivationMargin` 锚定按钮完整区域：`Button` 不负责像容器一样安排子节点，必须明确提供按钮的实际宽度。`FarmCultivationRow` 在内边距中安排居中的日历图标、占满剩余宽度的 `FarmCultivationStatus` 和居中的箭头；子节点忽略鼠标，点击继续由原按钮接收。字号变化后按实际宽度重排引用和下一轮两行信息，不允许以单字窄宽溢出按钮。测试同时检查该标签行数、宽度和按钮内范围，以及默认主要动作完整可见。标题节点为 `FarmCropTitle`。
