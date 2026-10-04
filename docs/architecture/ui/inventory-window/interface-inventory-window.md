# InventoryWindow 接口

对应 `scripts/ui/InventoryWindow.cs`，继承 `DraggableWindow`。按稳定商品目录创建十四商品纸面行，原料、对应加工品相邻排列；每行明确显示总量、可用量和冻结量。七个原料行另建保留底线输入和设置按钮；`Refresh(FarmGame)` 读取真实库存与已提交底线，不重建控件。输入有焦点或存在未提交编辑时，刷新不覆盖文本，因此光标、焦点、滚动位置和草稿保留；关闭重开也保留未提交编辑。

`RawReserveRequested(crop, quantity)` 只发出设置意图，由 `Main` 调用经营命令；按钮和回车使用同一校验，允许 0 至 `int.MaxValue` 的十进制非负整数，空串、负数、小数和溢出值显示中文错误，不发出命令。`ConfirmRawReserve(crop, quantity)` 由 `Main` 在命令成功后调用，确认输入并清除编辑标记。底线与库存的权威状态始终属于经营模块。

窗口名 `InventoryWindow`，内容容器名 `InventoryRows`，滚动容器名 `InventoryScroll`。各品种控件名为 `RawReserve{CropKind}Input` 和 `SetRawReserve{CropKind}Button`。窗口不执行交易；出售入口留在市场窗口。

各行保留底线标签关闭自动换行，以自然单行宽度参与横向布局；输入和设置按钮垂直居中，保持约 36 像素高，不随标签或行高度拉伸。#94 默认尺寸 660×510、位置 `(310,104)`，使用共享木框、纸面与深棕文字；统一避让顶部 94 像素和底部 90 像素常驻区，十四商品通过 `InventoryScroll` 纵向滚动访问。

`InventoryFilter0Button`、`InventoryFilter1Button`、`InventoryFilter2Button` 分别筛选全部、原料、加工品；`InventorySearchInput` 按商品名称做包含搜索，与类别共同生效。筛选只控制已有商品行的显示，不重建、清空或提交底线控件，隐藏原料行后未提交草稿仍保留。名称和类别没有匹配时显示 `InventoryEmptyMessage`。刷新及关闭重开保留搜索文字、类别、草稿；原料底线仍只按按钮或回车显式提交。

商品行命名为 `Inventory{CropKind}{Raw/Product}Row`，数量标签为 `Inventory{CropKind}{Raw/Product}Stock`。`TestCoreLoop.RunLayoutChecks` 检查真实卖单带来的总量 2、可用 1、冻结 1，名称/类别组合筛选、空结果、隐藏底线草稿与搜索焦点/光标保持，以及单行标签、约 36 像素输入高度、无重叠和末行可滚动到达；独立场景与汇总套件执行同一入口。
