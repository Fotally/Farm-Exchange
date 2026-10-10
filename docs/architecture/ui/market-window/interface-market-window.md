# MarketWindow 接口

视觉基准为1920×1080，另覆盖2560×1440及3840×2160；扩大分辨率保持窗口及字段的设定像素尺寸。通过统一 `UiScaling` 接口设置局部整体/字体倍率，只改变表现，不改所选商品、数量草稿及交易入口；局部设置不污染库存等其它窗口。

市场布局由汇总 `TestSuite` 和独立 `TestCoreLoop` 在实际1920×1080视口下检查。Godot headless 默认原生窗口只有64×64，夹具先设置1080P并等待尺寸生效，再验证143/180像素上下避让、输入草稿与末行可达，不用旧画布拉伸掩盖窗口尺寸。

对应 `scripts/ui/MarketWindow.cs`，继承 `DraggableWindow`，采用[已确认市场方案](../../../gameplay/trading/market-quotes.md)。

构造时创建十四商品固定行，按作物的原料、加工品相邻排列。每行包含商品选择按钮、当前报价、上次报价、实际涨跌百分比和公共库存；七个原料行另有真实可点击的按品种全部出售按钮。顶部显示本次和下次实际报价年月日，并在独立有界滚动区显示已公布消息：标明公布日期及对应报价日期，直接呈现经营快照的真实因素和改期理由。消息保留最近公告，不能把旧公告的因素解释为下一次报价。

`Refresh(FarmGame)` 读取 `GetMarketSnapshot()` 和 `GetStock(CommodityId)`，只更新现有标签与库存相关按钮状态。数量输入、所选商品、焦点、光标、表格与消息滚动、窗口位置和控件身份保留；关闭重开保留当前运行的草稿与选择。界面不计算价格、季节因素、事件、报价排期或交易金额。

| 玩家意图 | 事件与结果 |
| --- | --- |
| 选择商品，输入数量后买入或卖出 | `BuyRequested(CommodityId,int)` / `SellRequested(CommodityId,int)`；输入接受 1～int.MaxValue 整数，空白、负数、小数、0、溢出都不发出交易意图，窗口显示错误并通过 `TradeInputRejected(string)` 通知 Main。 |
| 出售选中商品全部公共库存 | `SellCommodityAllRequested(CommodityId)`；不读取数量草稿，库存为 0 时按钮禁用。 |
| 原料行快捷、全部加工品快捷 | `SellRawRequested(CropKind)` / `SellAllRequested`，保持完整结算入口，无对应库存时禁用。 |
| 经营入口执行后反馈 | `ShowFeedback(string)` 在窗口内显示真实成交结果或正常失败原因；Main 同时更新全局消息。 |

数量买卖按钮保留可用，以便数量、资金或库存不足时由执行入口给出明确失败。点击执行时 Main 查询的生效报价用于成交，不能使用窗口上次刷新时的价格。暂停期间这些按钮仍可使用，主动交易不推进经营。

输入拒绝时，窗口反馈与主界面操作消息在事件回调内立即显示，不等待下一经营步。`TestBuildPlacement` 使用全新暂停主场景检查首次无效数量的全局反馈可见，同时验证经营日期保持。

报价日期行提供“委托与策略”按钮，事件 `OrdersRequested` 交由 Main 打开 [TradeOrdersWindow](../trade-orders-window/interface-trade-orders-window.md)，即时交易数量草稿保持原状。市场公共库存仍显示总量；存在单次卖单冻结时追加可用量，悬停说明总库存、可用库存、冻结库存。全售选中商品、原料快捷和全部加工品快捷均按 `GetAvailableStock` 判断可售量，冻结库存不能启用全售按钮；数量交易继续由经营入口给出资源不足原因。

窗口基准尺寸810×510，首次在可用视口居中，使用统一木框、纸面和深棕正文；定位及避让范围见[可拖动窗口](../draggable-window/interface-draggable-window.md)。十四行表格使用浅色商品选择按钮，数字字段右对齐，按列压缩宽度；买入为橄榄绿主操作、卖出为纸面次操作，全售快捷降低强调。选择与交易仍使用原真实入口。

消息滚动区高 60，十四行表格独立滚动且保留至少 154 像素内容高度；交易数量与三个操作共用一行，全部加工品按钮及反馈放在表格下方。数量标题与交易反馈固定为单行，避免初次零宽换行撑高窗口。常规基准尺寸中交易操作可见；较小可用高度由共享窗口外层滚动容纳全部内容。冻结商品以“总量 / 可用量”展示，悬停完整说明总量、可用与冻结；不把冻结库存计为可售量。布局检查保留末行可达、真实公告完整性、输入焦点/草稿及全部交易按钮的可用区域断言。

保留节点名 `MarketWindow`、`MarketRows`、`MarketScroll`、`SellRaw{作物}Button`、`SellButton`。新增 `MarketDates`、`MarketNewsScroll`、`MarketNews`、`MarketQuantityInput`、`SelectedCommodityLabel`、`Commodity{作物}{Raw/Product}Button`、`BuyCommodityButton`、`SellCommodityButton`、`SellCommodityAllButton`、`MarketFeedback`；单个报价字段为 `Quote{作物}{Raw/Product}{Price/Previous/Change/Stock}`。
