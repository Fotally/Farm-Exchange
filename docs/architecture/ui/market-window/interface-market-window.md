# MarketWindow 接口

对应 `scripts/ui/MarketWindow.cs`，继承 `DraggableWindow`。创建时为当前六种作物固定建立原料/加工品价格行、按品种卖出原料按钮及出售全部加工品按钮；`Refresh(FarmGame)` 更新现行售价、公共库存和按钮可用状态，不替换控件。

`SellRawRequested(CropKind)` 和 `SellAllRequested` 只发出玩家意图；`Main` 调用 `FarmGame` 结算并刷新所有可见窗口。窗口名 `MarketWindow`，内容容器名 `MarketRows`，滚动容器名 `MarketScroll`，出售按钮沿用 `SellRaw{作物}Button` 与 `SellButton`。刷新保持窗口位置、滚动位置和操作按钮身份。
