# InventoryWindow 接口

对应 `scripts/ui/InventoryWindow.cs`，继承 `DraggableWindow`。创建时为七种作物各建一组库存标签、原料保留底线输入和设置按钮；`Refresh(FarmGame)` 更新公共库存与已提交底线，不重建控件。输入有焦点或存在未提交编辑时，刷新不覆盖文本，因此光标、焦点、滚动位置和草稿保留；关闭重开也保留未提交编辑。

`RawReserveRequested(crop, quantity)` 只发出设置意图，由 `Main` 调用经营命令；按钮和回车使用同一校验，允许 0 至 `int.MaxValue` 的十进制非负整数，空串、负数、小数和溢出值显示中文错误，不发出命令。`ConfirmRawReserve(crop, quantity)` 由 `Main` 在命令成功后调用，确认输入并清除编辑标记。底线与库存的权威状态始终属于经营模块。

窗口名 `InventoryWindow`，内容容器名 `InventoryRows`，滚动容器名 `InventoryScroll`。各品种控件名为 `RawReserve{CropKind}Input` 和 `SetRawReserve{CropKind}Button`。窗口不执行交易；出售入口留在市场窗口。

各行保留底线标签关闭自动换行，以自然单行宽度参与横向布局；输入和设置按钮垂直居中，保持约 36 像素高，不随标签或行高度拉伸。窗口保持 660×455，七种作物通过纵向滚动访问。`TestCoreLoop.RunLayoutChecks` 在挂树并等待布局帧后检查实际单行数量、控件高度、相邻控件无重叠，以及滚动后萝卜输入框完整可见；独立场景与汇总套件均执行该检查。
