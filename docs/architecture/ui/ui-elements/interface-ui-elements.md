# UiElements 内部接口

对应 `scripts/ui/UiElements.cs`，只供本工程 UI 模块使用。它集中提供当前主题颜色、标签、按钮、信息卡、边距、样式、金币格式和目录卡片清理，避免 `Main` 与各窗口复制同一视觉构造。它不保存经营或窗口状态；具体窗口仍由各自模块拥有控件。

FormatCoins(long cents) 接受交易聚合金额并固定显示两位小数；现有 int 金额调用可直接传入。此格式接口不扩大 Wallet 或库存的 int 容量。
