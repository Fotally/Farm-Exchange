# InventoryWindow 接口

对应 `scripts/ui/InventoryWindow.cs`，继承 `DraggableWindow`。创建时为当前六种作物各建一行；`Refresh(FarmGame)` 只更新原料和加工品公共库存文本，不重新创建滚动容器或行控件。

窗口名 `InventoryWindow`，内容容器名 `InventoryRows`，滚动容器名 `InventoryScroll`。窗口不执行交易；出售入口留在市场窗口。
