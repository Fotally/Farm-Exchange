# CropSelectionWindow 接口

对应 `scripts/ui/CropSelectionWindow.cs`，继承 `DraggableWindow`。创建时一次建立六种作物按钮；`Refresh(FarmGame)` 更新原料当日售价和公共库存，`CropRequested(CropKind)` 向 `Main` 报告改种意图，不直接修改农田。

窗口名 `CropWindow`，按钮名 `CropCard` 加作物种类。重新打开和经营刷新沿用滚动位置、窗口位置及按钮实例；建造目录关闭时也不会销毁作物选项。
