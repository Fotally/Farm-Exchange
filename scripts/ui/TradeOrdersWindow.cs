using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Market;
using FarmExchange.Time;
using FarmExchange.Trading;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

/**
 * <summary>显示委托快照并编辑玩家的交易条件。</summary>
 * <remarks>只解析输入与发出命令意图；刷新不覆盖未提交草稿或推进经营。</remarks>
 */
public partial class TradeOrdersWindow : DraggableWindow
{
    private readonly Label _resources;
    private readonly ItemList _orders;
    private readonly Label _details;
    private readonly Label _feedback;
    private readonly Label _editorTitle;
    private readonly OptionButton _commodity;
    private readonly OptionButton _side;
    private readonly OptionButton _frequency;
    private readonly OptionButton _quantityMode;
    private readonly LineEdit _quantity;
    private readonly OptionButton _budgetMode;
    private readonly LineEdit _budget;
    private readonly LineEdit _limit;
    private readonly OptionButton _reserveMode;
    private readonly LineEdit _reserve;
    private readonly Control _budgetRow;
    private readonly Control _quantityRow;
    private readonly Control _reserveRow;
    private readonly Label _reserveHelp;
    private readonly VBoxContainer _groups;
    private readonly Button _save;
    private readonly Button _toggle;
    private readonly Button _cancel;
    private readonly List<int> _orderIds = new();
    private readonly Dictionary<int, TradeOrderSnapshot> _snapshots = new();
    private readonly Dictionary<CommodityId, (int Total, int Available, int Frozen)> _stockResources = new();
    private string _moneyResources = "";
    private readonly List<ConditionGroup> _conditionGroups = new();
    private int? _selectedId;
    private int _nextGroup;
    private int _nextCondition;

    /**
     * <summary>发出创建完整委托的意图。</summary>
     */
    public event Action<TradeOrderRequest>? CreateRequested;
    /**
     * <summary>发出编辑原委托的意图，保留原设单现金基准。</summary>
     */
    public event Action<int, TradeOrderRequest>? UpdateRequested;
    /**
     * <summary>发出撤销所选委托的意图。</summary>
     */
    public event Action<int>? CancelRequested;
    /**
     * <summary>发出停用或恢复持续策略的意图。</summary>
     */
    public event Action<int, bool>? EnabledRequested;

    public TradeOrdersWindow() : base("TradeOrdersWindow", "委托与自动交易", new Vector2(160, 104),
        new Vector2(960, 520))
    {
        Body.AddThemeConstantOverride("separation", 10);
        _resources = Text("", "OrderResources");
        Body.AddChild(_resources);
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 12);
        Body.AddChild(columns);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(255, 0) };
        left.AddThemeConstantOverride("separation", 10);
        columns.AddChild(left);
        left.AddChild(Text("已建委托 · 选择后编辑", "OrderListHeading"));
        _orders = new ItemList
        {
            Name = "OrderList",
            CustomMinimumSize = new Vector2(255, 125),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _orders.ItemSelected += index => SelectOrder(_orderIds[(int)index]);
        left.AddChild(_orders);
        var detailScroll = new ScrollContainer
        {
            Name = "OrderDetailScroll",
            CustomMinimumSize = new Vector2(255, 145),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        left.AddChild(detailScroll);
        _details = Text("选择委托查看执行状态", "OrderDetails", wrap: true);
        _details.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        detailScroll.AddChild(_details);
        Button newOrder = MakeSecondaryButton("＋ 新建委托", 110, 34);
        newOrder.Name = "NewOrderButton";
        newOrder.Pressed += NewDraft;
        left.AddChild(newOrder);
        var editorScroll = new ScrollContainer
        {
            Name = "OrderEditorScroll",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        columns.AddChild(editorScroll);
        var editor = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        editor.AddThemeConstantOverride("separation", 12);
        editorScroll.AddChild(editor);
        _editorTitle = Text("新建委托", "OrderEditorTitle");
        _editorTitle.AddThemeFontSizeOverride("font_size", 16);
        editor.AddChild(_editorTitle);
        editor.AddChild(Text("商品、方向与执行方式", "OrderIdentityHeading"));
        var identity = Row(editor);
        _commodity = Choices("OrderCommodity", CommodityNames(), 190);
        _commodity.ItemSelected += _ => UpdateResources();
        _side = Choices("OrderSide", new[] { "买入", "卖出" }, 90);
        _frequency = Choices("OrderFrequency", new[] { "一次委托", "持续策略" }, 130);
        identity.AddChild(_commodity);
        identity.AddChild(_side);
        identity.AddChild(_frequency);
        _side.ItemSelected += _ => UpdateModes();
        _frequency.ItemSelected += _ => UpdateModes();
        _budgetRow = Row(editor);
        _budgetRow.AddChild(Text("买入资金", "OrderBudgetLabel"));
        _budgetMode = Choices("OrderBudgetMode", new[] { "最高单价＋数量", "固定总预算" }, 160);
        _budgetRow.AddChild(_budgetMode);
        _limit = Input("OrderLimitInput", "最高单价 / 金币", 155);
        _budget = Input("OrderBudgetInput", "总预算 / 含费用", 155);
        _budgetRow.AddChild(_limit);
        _budgetRow.AddChild(_budget);
        _budgetMode.ItemSelected += _ => UpdateModes();
        _quantityRow = Row(editor);
        _quantityMode = Choices("OrderQuantityMode", new[] { "固定数量", "补到目标库存", "卖到剩余库存" }, 175);
        _quantity = Input("OrderQuantityInput", "数量 / 份", 160);
        _quantityRow.AddChild(_quantityMode);
        _quantityRow.AddChild(_quantity);
        _reserveRow = Row(editor);
        _reserveRow.AddChild(Text("现金保留", "OrderReserveLabel"));
        _reserveMode = Choices("OrderReserveMode", new[] { "固定金额", "设单现金比例" }, 150);
        _reserve = Input("OrderReserveInput", "金币", 140);
        _reserve.Text = "0";
        _reserveRow.AddChild(_reserveMode);
        _reserveRow.AddChild(_reserve);
        _reserveMode.ItemSelected += _ => _reserve.PlaceholderText = _reserveMode.Selected == 0 ? "金币" : "百分比 0～100";
        _reserveHelp = Text("比例在设单时锁定为保留金额；编辑仍沿用原设单现金。", "OrderReserveHelp", wrap: true);
        editor.AddChild(_reserveHelp);
        editor.AddChild(Text("触发条件：组内同时满足，组间满足任一组", "OrderConditionsLabel", wrap: true));
        _groups = new VBoxContainer { Name = "OrderConditionGroups" };
        _groups.AddThemeConstantOverride("separation", 10);
        editor.AddChild(_groups);
        Button addGroup = MakeSecondaryButton("＋ 添加“或”条件组", 180, 34);
        addGroup.Name = "AddOrderGroupButton";
        addGroup.Pressed += () => AddGroup();
        editor.AddChild(addGroup);
        editor.AddChild(Text("一次委托冻结资源；持续策略不冻结。成交手续费 1%，未成交与撤销不收费。", "OrderRulesLabel", wrap: true));
        var footer = Row(Body);
        _save = MakeButton("创建委托", Mid, 110, 36);
        _save.Name = "SaveOrderButton";
        _save.Pressed += Submit;
        footer.AddChild(_save);
        _toggle = MakeSecondaryButton("停用", 90, 36);
        _toggle.Name = "ToggleOrderButton";
        _toggle.Pressed += ToggleSelected;
        footer.AddChild(_toggle);
        _cancel = MakeQuietButton("撤销委托", 110, 36);
        _cancel.Name = "CancelOrderButton";
        _cancel.Pressed += () => { if (_selectedId is int id) CancelRequested?.Invoke(id); };
        footer.AddChild(_cancel);
        _feedback = Text("暂停时可以编辑，恢复经营后检查成交。", "OrderFeedback", wrap: true);
        _feedback.CustomMinimumSize = new Vector2(430, 0);
        _feedback.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footer.AddChild(_feedback);
        AddGroup();
        UpdateModes();
        UpdateActions();
    }

    /**
     * <summary>刷新余额、委托列表和所选委托的真实执行结果。</summary>
     * <remarks>不重新加载编辑器，保留条件草稿、节点、焦点、光标和滚动。</remarks>
     * <param name="game">本局经营状态，只读取公开快照。</param>
     */
    public void Refresh(FarmGame game)
    {
        _moneyResources = $"金币 总 {FormatCoins(game.MoneyCents)} / 可用 {FormatCoins(game.AvailableMoneyCents)} / 冻结 {FormatCoins(game.FrozenMoneyCents)}";
        foreach (CommodityDefinition commodity in CommodityCatalog.All)
            _stockResources[commodity.Id] = (game.GetStock(commodity.Id), game.GetAvailableStock(commodity.Id), game.GetFrozenStock(commodity.Id));
        UpdateResources();
        foreach (TradeOrderSnapshot order in game.GetTradeOrders())
        {
            _snapshots[order.Id] = order;
            int index = _orderIds.IndexOf(order.Id);
            string text = $"#{order.Id} {CommodityCatalog.Get(order.Request.Commodity).Name} · {(order.Request.Side == TradeOrderSide.Buy ? "买" : "卖")} · {StatusText(order.Status)}";
            if (index < 0)
            {
                _orderIds.Add(order.Id);
                _orders.AddItem(text);
                index = _orderIds.Count - 1;
            }
            else if (_orders.GetItemText(index) != text) _orders.SetItemText(index, text);
            if (_selectedId == order.Id) _orders.Select(index);
        }
        UpdateDetails();
        UpdateActions();
    }

    /**
     * <summary>显示经营入口接受或拒绝编辑的结果。</summary>
     * <param name="result">真实委托命令结果。</param>
     */
    public void ShowCommandResult(TradeOrderCommandResult result)
    {
        _feedback.Text = result.Success ? $"委托 #{result.Id} 已更新" : result.ErrorMessage;
        if (!result.Success) return;
        _selectedId = result.Id;
        _editorTitle.Text = $"编辑委托 #{result.Id}（保留原设单现金基准）";
        _save.Text = "保存修改";
    }

    private void NewDraft()
    {
        _selectedId = null;
        _orders.DeselectAll();
        _editorTitle.Text = "新建委托";
        _save.Text = "创建委托";
        _details.Text = "填写条件后创建；提交本身不立即成交。";
        _feedback.Text = "当前草稿可作为新委托提交，使用新设单现金基准。";
        UpdateActions();
    }

    private void SelectOrder(int id)
    {
        _selectedId = id;
        TradeOrderRequest request = _snapshots[id].Request;
        for (int i = 0; i < CommodityCatalog.All.Count; i++)
            if (CommodityCatalog.All[i].Id == request.Commodity) _commodity.Select(i);
        _side.Select((int)request.Side);
        _frequency.Select((int)request.Frequency);
        _quantityMode.Select((int)request.QuantityMode);
        _quantity.Text = request.Quantity.ToString(CultureInfo.InvariantCulture);
        _budgetMode.Select(request.BudgetMode == TradeOrderBudgetMode.FixedBudget ? 1 : 0);
        _budget.Text = FormatCoins(request.BudgetCents);
        _limit.Text = FormatCoins(request.LimitPriceCents);
        _reserveMode.Select((int)request.ReserveMode);
        _reserve.Text = request.ReserveMode == CashReserveMode.Amount
            ? FormatCoins(request.ReserveValue) : request.ReserveValue.ToString(CultureInfo.InvariantCulture);
        _reserve.PlaceholderText = request.ReserveMode == CashReserveMode.Amount ? "金币" : "百分比 0～100";
        foreach (ConditionGroup group in _conditionGroups)
        {
            _groups.RemoveChild(group.Root);
            group.Root.QueueFree();
        }
        _conditionGroups.Clear();
        foreach (IReadOnlyList<TradeOrderCondition> conditions in request.ConditionGroups) AddGroup(conditions);
        _editorTitle.Text = $"编辑委托 #{id}（保留原设单现金基准）";
        _save.Text = "保存修改";
        UpdateModes();
        UpdateResources();
        UpdateDetails();
        UpdateActions();
    }

    private void UpdateModes()
    {
        bool buy = _side.Selected == (int)TradeOrderSide.Buy;
        bool onceBuy = buy && _frequency.Selected == (int)TradeOrderFrequency.Once;
        _budgetRow.Visible = onceBuy;
        _budget.Visible = onceBuy && _budgetMode.Selected == 1;
        _limit.Visible = onceBuy && _budgetMode.Selected == 0;
        _quantityRow.Visible = !onceBuy || _budgetMode.Selected == 0;
        _quantityMode.SetItemDisabled(1, !buy);
        _quantityMode.SetItemDisabled(2, buy);
        if ((buy && _quantityMode.Selected == 2) || (!buy && _quantityMode.Selected == 1)) _quantityMode.Select(0);
        _reserveRow.Visible = buy;
        _reserveHelp.Visible = buy;
    }

    private void UpdateActions()
    {
        bool selected = _selectedId is int id && _snapshots.ContainsKey(id);
        TradeOrderSnapshot? order = selected ? _snapshots[_selectedId!.Value] : null;
        bool active = order?.Status is TradeOrderStatus.Waiting or TradeOrderStatus.Disabled;
        _save.Disabled = selected && !active;
        _cancel.Disabled = !active;
        _toggle.Disabled = !active || order?.Request.Frequency != TradeOrderFrequency.Continuous;
        _toggle.Text = order?.Status == TradeOrderStatus.Disabled ? "恢复" : "停用";
    }

    private void UpdateDetails()
    {
        if (_selectedId is not int id || !_snapshots.TryGetValue(id, out TradeOrderSnapshot? order)) return;
        _details.Text = $"#{id} · {StatusText(order.Status)}\n设单现金 {FormatCoins(order.CashBasisCents)}\n锁定保留 {FormatCoins(order.ReserveCents)}\n冻结金币 {FormatCoins(order.FrozenCents)}\n冻结商品 {order.FrozenQuantity} 份\n单次锁定数量 {order.LockedQuantity} 份";
        if (!string.IsNullOrEmpty(order.WaitingReason)) _details.Text += "\n" + order.WaitingReason;
        if (order.LastFill is TradeOrderFillSnapshot fill)
        {
            TradeResult trade = fill.Trade;
            bool bought = fill.Side == TradeOrderSide.Buy;
            long actual = bought ? trade.TotalCents + trade.FeeCents : trade.TotalCents - trade.FeeCents;
            _details.Text += $"\n最近成交 {(bought ? "买入" : "卖出")} {CommodityCatalog.Get(fill.Commodity).Name} {trade.Quantity} 份\n商品总额 {FormatCoins(trade.TotalCents)}\n手续费 {FormatCoins(trade.FeeCents)}\n实际{(bought ? "支出" : "收入")} {FormatCoins(actual)}\n成交后余额 {FormatCoins(fill.BalanceCents)}";
        }
    }

    private void UpdateResources()
    {
        CommodityId commodity = CommodityCatalog.All[_commodity.Selected].Id;
        if (!_stockResources.TryGetValue(commodity, out var stock)) return;
        _resources.Text = _moneyResources + $"\n{CommodityCatalog.Get(commodity).Name}库存 总 {stock.Total} / 可用 {stock.Available} / 冻结 {stock.Frozen}";
    }

    private void ToggleSelected()
    {
        if (_selectedId is int id && _snapshots.TryGetValue(id, out TradeOrderSnapshot? order))
            EnabledRequested?.Invoke(id, order.Status == TradeOrderStatus.Disabled);
    }

    private void AddGroup(IReadOnlyList<TradeOrderCondition>? conditions = null)
    {
        int groupId = ++_nextGroup;
        var root = new VBoxContainer { Name = $"OrderGroup{groupId}" };
        Label heading = Text(_conditionGroups.Count == 0 ? "同时满足" : "或，同时满足", $"OrderGroup{groupId}Label");
        var group = new ConditionGroup(root, heading);
        _conditionGroups.Add(group);
        _groups.AddChild(root);
        var header = Row(root);
        header.AddChild(heading);
        root.AddThemeConstantOverride("separation", 8);
        Button add = MakeSecondaryButton("添加条件", 100, 32);
        add.Name = $"AddOrderCondition{groupId}Button";
        add.Pressed += () => AddCondition(group);
        header.AddChild(add);
        Button remove = MakeQuietButton("删除组", 80, 32);
        remove.Name = $"RemoveOrderGroup{groupId}Button";
        remove.Pressed += () =>
        {
            _conditionGroups.Remove(group);
            root.GetParent().RemoveChild(root);
            root.QueueFree();
            for (int i = 0; i < _conditionGroups.Count; i++)
                _conditionGroups[i].Heading.Text = i == 0 ? "同时满足" : "或，同时满足";
        };
        header.AddChild(remove);
        if (conditions == null) AddCondition(group);
        else foreach (TradeOrderCondition condition in conditions) AddCondition(group, condition);
    }

    private void AddCondition(ConditionGroup group, TradeOrderCondition? condition = null)
    {
        int conditionId = ++_nextCondition;
        HBoxContainer row = Row(group.Root);
        row.Name = $"OrderCondition{conditionId}";
        OptionButton factor = Choices($"OrderConditionFactor{conditionId}", new[] { "当前价格", "总库存", "当前季节" }, 125);
        OptionButton comparison = Choices($"OrderConditionComparison{conditionId}", new[] { "<", "≤", "=", "≥", ">" }, 65);
        LineEdit value = Input($"OrderConditionValue{conditionId}", "金币", 145);
        OptionButton season = Choices($"OrderConditionSeason{conditionId}", new[] { "春", "夏", "秋", "冬" }, 145);
        row.AddChild(factor);
        row.AddChild(comparison);
        row.AddChild(value);
        row.AddChild(season);
        var editor = new ConditionEditor(row, factor, comparison, value, season);
        group.Conditions.Add(editor);
        factor.ItemSelected += _ => UpdateCondition(editor);
        Button remove = MakeQuietButton("×", 32, 32);
        remove.Name = $"RemoveOrderCondition{conditionId}Button";
        remove.Pressed += () => { group.Conditions.Remove(editor); row.GetParent().RemoveChild(row); row.QueueFree(); };
        row.AddChild(remove);
        if (condition != null)
        {
            factor.Select((int)condition.Factor);
            comparison.Select((int)condition.Comparison);
            if (condition.Factor == TradeConditionFactor.Season) season.Select(condition.Value);
            else value.Text = condition.Factor == TradeConditionFactor.Price
                ? FormatCoins(condition.Value) : condition.Value.ToString(CultureInfo.InvariantCulture);
        }
        UpdateCondition(editor);
    }

    private static void UpdateCondition(ConditionEditor editor)
    {
        bool season = editor.Factor.Selected == (int)TradeConditionFactor.Season;
        editor.Comparison.Visible = !season;
        editor.Value.Visible = !season;
        editor.Season.Visible = season;
        editor.Value.PlaceholderText = editor.Factor.Selected == (int)TradeConditionFactor.Price ? "金币" : "库存 / 份";
    }

    private void Submit()
    {
        bool onceBuy = _side.Selected == 0 && _frequency.Selected == 0;
        TradeOrderBudgetMode budgetMode = !onceBuy ? TradeOrderBudgetMode.None :
            _budgetMode.Selected == 1 ? TradeOrderBudgetMode.FixedBudget : TradeOrderBudgetMode.LimitPrice;
        int quantity = 0, budget = 0, limit = 0, reserve = 0;
        if (budgetMode == TradeOrderBudgetMode.FixedBudget)
        {
            if (!Money(_budget.Text, out budget) || budget == 0) { Reject("总预算须为正金额，最多两位小数"); return; }
        }
        else
        {
            if (!Integer(_quantity.Text, out quantity)) { Reject("数量须为非负整数"); return; }
            if (budgetMode == TradeOrderBudgetMode.LimitPrice && (!Money(_limit.Text, out limit) || limit == 0))
            { Reject("最高单价须为正金额，最多两位小数"); return; }
        }
        if (_side.Selected == 0)
        {
            bool validReserve = _reserveMode.Selected == 0 ? Money(_reserve.Text, out reserve) : Integer(_reserve.Text, out reserve) && reserve <= 100;
            if (!validReserve) { Reject("保留金额须为非负金额，比例须为 0～100 的整数"); return; }
        }
        var groups = new List<IReadOnlyList<TradeOrderCondition>>();
        foreach (ConditionGroup group in _conditionGroups)
        {
            var conditions = new List<TradeOrderCondition>();
            foreach (ConditionEditor editor in group.Conditions)
            {
                TradeConditionFactor factor = (TradeConditionFactor)editor.Factor.Selected;
                int value;
                if (factor == TradeConditionFactor.Season) value = editor.Season.Selected;
                else if (!(factor == TradeConditionFactor.Price ? Money(editor.Value.Text, out value) : Integer(editor.Value.Text, out value)))
                { Reject("条件价格须为非负金额，库存须为非负整数"); return; }
                conditions.Add(new TradeOrderCondition(factor,
                    factor == TradeConditionFactor.Season ? TradeConditionComparison.Equal : (TradeConditionComparison)editor.Comparison.Selected, value));
            }
            groups.Add(conditions);
        }
        var request = new TradeOrderRequest(CommodityCatalog.All[_commodity.Selected].Id,
            (TradeOrderSide)_side.Selected, (TradeOrderFrequency)_frequency.Selected,
            budgetMode == TradeOrderBudgetMode.FixedBudget ? TradeOrderQuantityMode.Fixed : (TradeOrderQuantityMode)_quantityMode.Selected,
            quantity, budgetMode, budget, limit, (CashReserveMode)_reserveMode.Selected, reserve, groups);
        if (_selectedId is int id) UpdateRequested?.Invoke(id, request);
        else CreateRequested?.Invoke(request);
    }

    private void Reject(string message) => _feedback.Text = message;

    private static bool Integer(string text, out int value) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool Money(string text, out int cents)
    {
        cents = 0;
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount) ||
            amount < 0 || amount > int.MaxValue / 100m || amount * 100 != decimal.Truncate(amount * 100)) return false;
        cents = (int)(amount * 100);
        return true;
    }

    private static string[] CommodityNames()
    {
        var names = new string[CommodityCatalog.All.Count];
        for (int i = 0; i < names.Length; i++) names[i] = CommodityCatalog.All[i].Name;
        return names;
    }

    private static string StatusText(TradeOrderStatus status) => status switch
    {
        TradeOrderStatus.Waiting => "等待",
        TradeOrderStatus.Disabled => "停用",
        TradeOrderStatus.Completed => "已成交",
        TradeOrderStatus.Cancelled => "已撤销",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static HBoxContainer Row(Control parent)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        parent.AddChild(row);
        return row;
    }

    private static Label Text(string text, string name, bool wrap = false)
    {
        Label label = MakeLabel(text, 12, Ink);
        label.Name = name;
        if (!wrap) label.AutowrapMode = TextServer.AutowrapMode.Off;
        return label;
    }

    private static OptionButton Choices(string name, string[] items, float width)
    {
        var control = new OptionButton { Name = name, CustomMinimumSize = new Vector2(width, 36) };
        foreach (string item in items) control.AddItem(item);
        return control;
    }

    private static LineEdit Input(string name, string placeholder, float width) => new()
    {
        Name = name,
        PlaceholderText = placeholder,
        CustomMinimumSize = new Vector2(width, 36),
    };

    private sealed record ConditionGroup(VBoxContainer Root, Label Heading)
    {
        public List<ConditionEditor> Conditions { get; } = new();
    }

    private sealed record ConditionEditor(HBoxContainer Root, OptionButton Factor, OptionButton Comparison,
        LineEdit Value, OptionButton Season);
}
