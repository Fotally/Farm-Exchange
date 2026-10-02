using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Market;
using FarmExchange.Time;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class MarketWindow : DraggableWindow
{
    private static readonly float[] ColumnWidths = { 150, 85, 85, 85, 80, 150 };
    private readonly Dictionary<CommodityId, QuoteRow> _rows = new();
    private readonly Label _dates;
    private readonly Label _news;
    private readonly Label _selectedLabel;
    private readonly Label _feedback;
    private readonly LineEdit _quantity;
    private readonly Button _sellButton;
    private readonly Button _sellSelectedButton;
    private readonly Button _sellSelectedAllButton;
    private CommodityId _selected = CommodityCatalog.All[0].Id;

    public event Action<CommodityId, int>? BuyRequested;
    public event Action<CommodityId, int>? SellRequested;
    public event Action<CommodityId>? SellCommodityAllRequested;
    public event Action<CropKind>? SellRawRequested;
    public event Action? SellAllRequested;
    public event Action<string>? TradeInputRejected;
    public event Action? OrdersRequested;

    public MarketWindow() : base("MarketWindow", "市场 · 即时买卖", new Vector2(250, 78),
        new Vector2(780, 520), avoidBottomBar: true)
    {
        Body.AddThemeConstantOverride("separation", 6);
        _dates = MakeLabel("", 13, Ink);
        _dates.Name = "MarketDates";
        _dates.AutowrapMode = TextServer.AutowrapMode.Off;
        var datesRow = new HBoxContainer();
        _dates.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        datesRow.AddChild(_dates);
        Button orders = MakeButton("委托与策略", Mid, 120, 30);
        orders.Name = "OpenTradeOrdersButton";
        orders.Pressed += () => OrdersRequested?.Invoke();
        datesRow.AddChild(orders);
        Body.AddChild(datesRow);
        var newsScroll = new ScrollContainer
        {
            Name = "MarketNewsScroll",
            CustomMinimumSize = new Vector2(0, 60),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        Body.AddChild(newsScroll);
        _news = MakeLabel("", 12, Ink);
        _news.Name = "MarketNews";
        _news.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        newsScroll.AddChild(_news);
        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 8);
        Body.AddChild(heading);
        string[] headings = { "选择商品", "现价 / 金币", "上次 / 金币", "实际涨跌", "公共库存", "原料快捷" };
        for (int column = 0; column < headings.Length; column++)
            heading.AddChild(MakeCellLabel(headings[column], column));
        var scroll = new ScrollContainer
        {
            Name = "MarketScroll",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        Body.AddChild(scroll);
        var rows = new GridContainer { Name = "MarketRows", Columns = 6 };
        rows.AddThemeConstantOverride("h_separation", 8);
        rows.AddThemeConstantOverride("v_separation", 4);
        scroll.AddChild(rows);
        var selectionGroup = new ButtonGroup();
        foreach (CommodityDefinition definition in CommodityCatalog.All)
        {
            CommodityId id = definition.Id;
            Button select = MakeButton(definition.Name, Mid, ColumnWidths[0], 34);
            select.Name = $"Commodity{id.Crop}{id.Kind}Button";
            select.ToggleMode = true;
            select.ButtonGroup = selectionGroup;
            select.ButtonPressed = id == _selected;
            select.Pressed += () => Select(id);
            rows.AddChild(select);
            Label price = MakeCellLabel("", 1);
            Label previous = MakeCellLabel("", 2);
            Label change = MakeCellLabel("", 3);
            Label stock = MakeCellLabel("", 4);
            price.Name = $"Quote{id.Crop}{id.Kind}Price";
            previous.Name = $"Quote{id.Crop}{id.Kind}Previous";
            change.Name = $"Quote{id.Crop}{id.Kind}Change";
            stock.Name = $"Quote{id.Crop}{id.Kind}Stock";
            rows.AddChild(price);
            rows.AddChild(previous);
            rows.AddChild(change);
            rows.AddChild(stock);
            Button? rawShortcut = null;
            if (id.Kind == CommodityKind.Raw)
            {
                rawShortcut = MakeButton("全部售原料", Mid, ColumnWidths[5], 34);
                rawShortcut.Name = $"SellRaw{id.Crop}Button";
                rawShortcut.Pressed += () => SellRawRequested?.Invoke(id.Crop);
                rows.AddChild(rawShortcut);
            }
            else
                rows.AddChild(new Control { CustomMinimumSize = new Vector2(ColumnWidths[5], 0) });
            _rows.Add(id, new QuoteRow(price, previous, change, stock, rawShortcut));
        }
        var tradeRow = new HBoxContainer { Name = "MarketTradeControls" };
        tradeRow.AddThemeConstantOverride("separation", 8);
        Body.AddChild(tradeRow);
        _selectedLabel = MakeLabel(CommodityCatalog.Get(_selected).Name, 13, Ink);
        _selectedLabel.Name = "SelectedCommodityLabel";
        _selectedLabel.CustomMinimumSize = new Vector2(150, 0);
        _selectedLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        tradeRow.AddChild(_selectedLabel);
        Label quantityLabel = MakeLabel("数量", 13, Ink);
        quantityLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        tradeRow.AddChild(quantityLabel);
        _quantity = new LineEdit
        {
            Name = "MarketQuantityInput",
            Text = "1",
            CustomMinimumSize = new Vector2(120, 38),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        tradeRow.AddChild(_quantity);
        Button buy = MakeButton("买入", Mid, 72, 38);
        buy.Name = "BuyCommodityButton";
        buy.Pressed += () => Submit(buy: true);
        tradeRow.AddChild(buy);
        _sellSelectedButton = MakeButton("卖出", Mid, 72, 38);
        _sellSelectedButton.Name = "SellCommodityButton";
        _sellSelectedButton.Pressed += () => Submit(buy: false);
        tradeRow.AddChild(_sellSelectedButton);
        _sellSelectedAllButton = MakeButton("该商品全售", Gold, 110, 38);
        _sellSelectedAllButton.Name = "SellCommodityAllButton";
        _sellSelectedAllButton.Pressed += () => SellCommodityAllRequested?.Invoke(_selected);
        tradeRow.AddChild(_sellSelectedAllButton);
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 10);
        Body.AddChild(footer);
        _sellButton = MakeButton("出售全部加工品", Gold, 180, 38);
        _sellButton.Name = "SellButton";
        _sellButton.Pressed += () => SellAllRequested?.Invoke();
        footer.AddChild(_sellButton);
        _feedback = MakeLabel("同价买卖，无手续费；按成交时报价结算", 12, Ink);
        _feedback.AutowrapMode = TextServer.AutowrapMode.Off;
        _feedback.Name = "MarketFeedback";
        _feedback.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footer.AddChild(_feedback);
    }

    public void Refresh(FarmGame game)
    {
        MarketSnapshot market = game.GetMarketSnapshot();
        _dates.Text = $"本次报价：{FormatDate(market.LastQuoteDate)}    下次报价：{FormatDate(market.NextQuoteDate)}";
        string newsText = market.News is MarketNewsSnapshot news
            ? $"{FormatDate(news.PublishedDate)} 公布 · 对应 {FormatDate(news.QuoteDate)} 报价\n" +
                string.Join("\n", news.Lines)
            : "市场消息：尚无已公布消息";
        if (_news.Text != newsText) _news.Text = newsText;
        bool hasProducts = false;
        foreach (MarketQuoteSnapshot quote in market.Quotes)
        {
            QuoteRow row = _rows[quote.Id];
            int stock = game.GetStock(quote.Id);
            int available = game.GetAvailableStock(quote.Id);
            int frozen = game.GetFrozenStock(quote.Id);
            row.Available = available;
            row.Price.Text = FormatCoins(quote.PriceCents);
            row.Previous.Text = FormatCoins(quote.PreviousPriceCents);
            row.Change.Text = quote.ChangePercent.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%";
            row.Stock.Text = frozen == 0 ? stock.ToString(CultureInfo.InvariantCulture) : $"{stock}\n可用 {available}";
            row.Stock.TooltipText = $"总库存 {stock}，可用 {available}，冻结 {frozen}";
            if (row.RawShortcut != null) row.RawShortcut.Disabled = available == 0;
            else hasProducts |= available > 0;
        }
        _sellButton.Disabled = !hasProducts;
        _sellSelectedAllButton.Disabled = game.GetAvailableStock(_selected) == 0;
    }

    public void ShowFeedback(string message) => _feedback.Text = message;

    private void Select(CommodityId commodity)
    {
        _selected = commodity;
        _selectedLabel.Text = CommodityCatalog.Get(commodity).Name;
        _sellSelectedAllButton.Disabled = _rows[commodity].Available == 0;
    }

    private void Submit(bool buy)
    {
        if (!int.TryParse(_quantity.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int quantity) ||
            quantity < 1)
        {
            const string message = "请输入 1 到 2147483647 之间的整数";
            ShowFeedback(message);
            TradeInputRejected?.Invoke(message);
            return;
        }
        if (buy) BuyRequested?.Invoke(_selected, quantity);
        else SellRequested?.Invoke(_selected, quantity);
    }

    private static Label MakeCellLabel(string text, int column)
    {
        Label label = MakeLabel(text, 12, Ink);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.CustomMinimumSize = new Vector2(ColumnWidths[column], 0);
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return label;
    }

    private static string FormatDate(GameDate date) => $"第 {date.Year} 年 {date.Month} 月 {date.Day} 日";

    private sealed record QuoteRow(Label Price, Label Previous, Label Change, Label Stock, Button? RawShortcut)
    {
        public int Available { get; set; }
    }
}
