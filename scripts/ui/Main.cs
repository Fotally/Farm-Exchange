using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Land;
using FarmExchange.Market;
using FarmExchange.Time;
using FarmExchange.Trading;
using FarmExchange.World;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class Main : Node2D
{
    private readonly FarmGame _game = new();
    private WorldMap _worldMap = null!;
    private Control _uiRoot = null!;
    private Label _moneyLabel = null!;
    private Label _workerLabel = null!;
    private Label _calendarLabel = null!;
    private Button _pauseButton = null!;
    private Label _messageLabel = null!;
    private Label _buildHint = null!;
    private Button _cancelPlacementButton = null!;
    private DraggableWindow _detailWindow = null!;
    private BuildCatalogWindow _buildWindow = null!;
    private CropSelectionWindow _cropWindow = null!;
    private InventoryWindow _inventoryWindow = null!;
    private MarketWindow _marketWindow = null!;
    private TradeOrdersWindow _ordersWindow = null!;
    private Label _detailCell = null!;
    private PanelContainer _emptyDetails = null!;
    private FarmDetailsPanel _farmDetails = null!;
    private ProcessorDetailsPanel _processorDetails = null!;
    private RoadDetailsPanel _roadDetails = null!;
    private Vector2I? _selectedCell;
    private Placement? _placement;

    private readonly record struct Placement(BuildingKind Kind, CropKind Crop)
    {
        public string Name => Kind == BuildingKind.Farm ? "农田" :
            Kind == BuildingKind.Road ? "道路" : FarmGame.GetCrop(Crop).BuildingName;
    }

    internal FarmGame Game => _game;

    public override void _Ready()
    {
        _worldMap = GetNode<WorldMap>("WorldMap");
        _worldMap.SetGame(_game);
        GetNode<Camera2D>("Camera2D").GlobalPosition = _worldMap.GetGridWorldPosition(
            new Vector2((FarmGame.MapSize - 1) / 2f, (FarmGame.MapSize - 1) / 2f));
        var workerPresentation = new WorkerPresentation();
        _worldMap.AddChild(workerPresentation);
        workerPresentation.SetGame(_game, _worldMap);
        _worldMap.SelectionChanged += OnSelectionChanged;
        _uiRoot = GetNode<Control>("CanvasLayer/UiRoot");
        BuildInterface();
        GetNode<Timer>("TickTimer").Timeout += OnTick;
        RefreshUi();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Keycode: Key.Escape })
            return;
        if (_placement is { Kind: BuildingKind.Road }) CancelPlacement();
        else if (_ordersWindow.Visible) _ordersWindow.Hide();
        else if (_marketWindow.Visible) _marketWindow.Hide();
        else if (_inventoryWindow.Visible) _inventoryWindow.Hide();
        else if (_cropWindow.Visible) _cropWindow.Hide();
        else if (_buildWindow.Visible) _buildWindow.Hide();
        else if (_placement != null) CancelPlacement();
        else if (_detailWindow.Visible) CloseDetail();
        else return;
        GetViewport().SetInputAsHandled();
    }

    private void BuildInterface()
    {
        BuildTopBar();
        BuildBottomBar();
        BuildMessage();

        _detailWindow = new DraggableWindow("DetailWindow", "地块详情", new Vector2(914, 86),
            new Vector2(350, 500), close: CloseDetail);
        _uiRoot.AddChild(_detailWindow);
        VBoxContainer detailBody = _detailWindow.Body;
        var detailScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        detailBody.AddChild(detailScroll);
        var detailContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        detailContent.AddThemeConstantOverride("separation", 9);
        detailScroll.AddChild(detailContent);
        _detailCell = MakeLabel("", 13, Ink);
        detailContent.AddChild(_detailCell);
        _emptyDetails = MakeInfoCard("空地\n点击底部“建造”选择建筑，再点击地图空位摆放。");
        detailContent.AddChild(_emptyDetails);
        _farmDetails = new FarmDetailsPanel();
        _farmDetails.ChangeCropRequested += OpenCrop;
        _farmDetails.RemoveRequested += RemoveSelected;
        detailContent.AddChild(_farmDetails);
        _processorDetails = new ProcessorDetailsPanel();
        _processorDetails.RemoveRequested += RemoveSelected;
        detailContent.AddChild(_processorDetails);
        _roadDetails = new RoadDetailsPanel();
        _roadDetails.RemoveRequested += RemoveSelected;
        detailContent.AddChild(_roadDetails);

        _buildWindow = new BuildCatalogWindow();
        _buildWindow.SelectionRequested += (kind, crop) => StartPlacement(new Placement(kind, crop));
        _uiRoot.AddChild(_buildWindow);

        _cropWindow = new CropSelectionWindow();
        _cropWindow.CropRequested += SetCrop;
        _uiRoot.AddChild(_cropWindow);

        _inventoryWindow = new InventoryWindow();
        _inventoryWindow.RawReserveRequested += SetRawReserve;
        _uiRoot.AddChild(_inventoryWindow);

        _marketWindow = new MarketWindow();
        _marketWindow.SellRawRequested += SellRaw;
        _marketWindow.SellAllRequested += SellAll;
        _marketWindow.BuyRequested += (commodity, quantity) => Trade(commodity, quantity, buy: true);
        _marketWindow.SellRequested += (commodity, quantity) => Trade(commodity, quantity, buy: false);
        _marketWindow.SellCommodityAllRequested += SellCommodityAll;
        _marketWindow.TradeInputRejected += message => _messageLabel.Text = message;
        _marketWindow.OrdersRequested += OpenTradeOrders;
        _uiRoot.AddChild(_marketWindow);

        _ordersWindow = new TradeOrdersWindow();
        _ordersWindow.CreateRequested += request => ShowOrderResult(_game.CreateTradeOrder(request));
        _ordersWindow.UpdateRequested += (id, request) => ShowOrderResult(_game.UpdateTradeOrder(id, request));
        _ordersWindow.CancelRequested += id => ShowOrderResult(_game.CancelTradeOrder(id));
        _ordersWindow.EnabledRequested += (id, enabled) => ShowOrderResult(_game.SetTradeOrderEnabled(id, enabled));
        _uiRoot.AddChild(_ordersWindow);
    }

    private void BuildTopBar()
    {
        var top = new PanelContainer { Name = "TopBar", AnchorRight = 1f, OffsetBottom = 70f };
        top.AddThemeStyleboxOverride("panel", Style(Dark, 0));
        _uiRoot.AddChild(top);
        var margin = WrapMargin(top, 14, 10);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        margin.AddChild(row);
        Label gameTitle = MakeLabel("✿ 农场交易", 21, Cream);
        gameTitle.AutowrapMode = TextServer.AutowrapMode.Off;
        gameTitle.CustomMinimumSize = new Vector2(160, 0);
        row.AddChild(gameTitle);
        row.AddChild(MakeStat("金币", out _moneyLabel));
        row.AddChild(MakeStat("工人", out _workerLabel));
        _workerLabel.Name = "WorkerCountLabel";
        row.AddChild(MakeStat("日期", out _calendarLabel, width: 235));
        _pauseButton = MakeButton("暂停", Mid, 70, 44);
        _pauseButton.Name = "PauseButton";
        _pauseButton.Pressed += TogglePause;
        row.AddChild(_pauseButton);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        Button inventory = MakeButton("▣ 库存", Mid, 92, 44);
        inventory.Name = "InventoryButton";
        inventory.Pressed += OpenInventory;
        row.AddChild(inventory);
        Button market = MakeButton("▤ 市场", Mid, 92, 44);
        market.Name = "MarketButton";
        market.Pressed += OpenMarket;
        row.AddChild(market);
        Button save = MakeButton("存档 · 规划", Mid, 110, 44);
        save.Disabled = true;
        row.AddChild(save);
        Button person = MakeButton("人物 · 规划", Mid, 110, 44);
        person.Disabled = true;
        row.AddChild(person);
    }

    private void BuildBottomBar()
    {
        var bottom = new PanelContainer
        {
            Name = "BottomBar",
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 16f,
            OffsetRight = -16f,
            OffsetTop = -109f,
            OffsetBottom = -15f,
        };
        bottom.AddThemeStyleboxOverride("panel", Style(Dark, 16));
        _uiRoot.AddChild(bottom);
        var margin = WrapMargin(bottom, 15, 12);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        margin.AddChild(row);
        Button build = MakeButton("⚒ 建造", Gold, 142, 60);
        build.Name = "BuildButton";
        build.Pressed += OpenBuild;
        row.AddChild(build);
        _buildHint = MakeLabel("选择建筑，再点击地图空位摆放", 15, Cream);
        _buildHint.Name = "BuildHint";
        _buildHint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(_buildHint);
        _cancelPlacementButton = MakeButton("取消摆放 Esc", Mid, 128, 44);
        _cancelPlacementButton.Name = "CancelPlacementButton";
        _cancelPlacementButton.Pressed += CancelPlacement;
        _cancelPlacementButton.Hide();
        row.AddChild(_cancelPlacementButton);
    }

    private void BuildMessage()
    {
        var panel = new PanelContainer { Name = "MessagePanel", Position = new Vector2(20, 525), Size = new Vector2(620, 38) };
        panel.AddThemeStyleboxOverride("panel", Style(Dark, 9));
        _uiRoot.AddChild(panel);
        var margin = WrapMargin(panel, 12, 7);
        _messageLabel = MakeLabel("点击“建造”选择建筑，或点击地图查看详情", 13, Cream);
        _messageLabel.Name = "MessageLabel";
        margin.AddChild(_messageLabel);
    }

    private void OnSelectionChanged(Vector2I cell)
    {
        bool built = false;
        if (_placement is Placement placement)
        {
            PlacementResult result = _game.TryPlace(cell, placement.Kind, placement.Crop);
            if (!result.Success)
            {
                _messageLabel.Text = result.ErrorMessage;
                _worldMap.ClearSelection();
                return;
            }
            _messageLabel.Text = $"{placement.Name}已建造，花费 {FormatCoins(result.ChargedCents)} 金币";
            if (placement.Kind != BuildingKind.Road)
                _placement = null;
            built = true;
        }
        _selectedCell = cell;
        _cropWindow.Hide();
        _inventoryWindow.Hide();
        _marketWindow.Hide();
        _ordersWindow.Hide();
        if (built) RefreshAfterGameChange(worldChanged: true);
        else RefreshUi();
        _uiRoot.MoveChild(_detailWindow, _uiRoot.GetChildCount() - 1);
    }

    private void OpenBuild()
    {
        _placement = null;
        _cancelPlacementButton.Hide();
        _detailWindow.Hide();
        _cropWindow.Hide();
        _inventoryWindow.Hide();
        _marketWindow.Hide();
        _ordersWindow.Hide();
        _buildWindow.ShowRaised();
        _buildWindow.RefreshCards();
        RefreshFooter();
    }

    private void StartPlacement(Placement placement)
    {
        _placement = placement;
        _selectedCell = null;
        _worldMap.ClearSelection();
        _detailWindow.Hide();
        _buildWindow.Hide();
        _cropWindow.Hide();
        _messageLabel.Text = placement.Kind == BuildingKind.Road ? "逐格点击空位铺设道路，按 Esc 可取消" :
            $"点击空小格作为{placement.Name}的锚点，占地 3×3，按 Esc 可取消";
        RefreshFooter();
    }

    private void CancelPlacement()
    {
        _placement = null;
        _messageLabel.Text = "已取消摆放";
        RefreshFooter();
    }

    private void CloseDetail()
    {
        _selectedCell = null;
        _detailWindow.Hide();
        _cropWindow.Hide();
        _worldMap.ClearSelection();
    }

    private void OpenCrop()
    {
        if (_selectedCell is not Vector2I cell || _game.GetPlot(cell).Building != BuildingKind.Farm)
            return;
        _buildWindow.Hide();
        _cropWindow.Refresh(_game);
        _cropWindow.ShowRaised();
    }

    private void SetCrop(CropKind crop)
    {
        if (_selectedCell is not Vector2I cell)
            return;
        string? error = _game.SetFarmCrop(cell, crop);
        _messageLabel.Text = error ?? $"农田已改种{FarmGame.GetCrop(crop).CropName}";
        if (error == null)
            _cropWindow.Hide();
        RefreshAfterGameChange(worldChanged: true);
    }

    private void RemoveSelected()
    {
        if (_selectedCell is not Vector2I cell)
            return;
        string? error = _game.RemoveBuilding(cell);
        _messageLabel.Text = error ?? "建筑已移除；不退还建造费";
        RefreshAfterGameChange(worldChanged: true);
    }

    private void OpenInventory()
    {
        _buildWindow.Hide();
        _cropWindow.Hide();
        _marketWindow.Hide();
        _ordersWindow.Hide();
        _inventoryWindow.Refresh(_game);
        _inventoryWindow.ShowRaised();
    }

    private void OpenMarket()
    {
        _buildWindow.Hide();
        _cropWindow.Hide();
        _inventoryWindow.Hide();
        _ordersWindow.Hide();
        _marketWindow.Refresh(_game);
        _marketWindow.ShowRaised();
    }

    private void OpenTradeOrders()
    {
        _marketWindow.Hide();
        _ordersWindow.Refresh(_game);
        _ordersWindow.ShowRaised();
    }

    private void ShowOrderResult(TradeOrderCommandResult result)
    {
        _ordersWindow.ShowCommandResult(result);
        _messageLabel.Text = result.Success ? $"委托 #{result.Id} 操作成功" : result.ErrorMessage;
        RefreshAfterGameChange(worldChanged: false);
    }

    private void SetRawReserve(CropKind crop, int quantity)
    {
        RawReserveFailure failure = _game.SetRawReserve(crop, quantity);
        if (failure == RawReserveFailure.None)
        {
            _inventoryWindow.ConfirmRawReserve(crop, quantity);
            _messageLabel.Text = $"{FarmGame.GetCrop(crop).CropName}原料保留底线已设为 {quantity}";
        }
        else
            _messageLabel.Text = failure == RawReserveFailure.InvalidCrop
                ? "无效作物" : "保留底线必须是非负整数";
        RefreshAfterGameChange(worldChanged: false);
    }

    private void SellRaw(CropKind crop)
    {
        SaleResult sale = _game.SellRaw(crop);
        ShowMarketFeedback(sale.Success
            ? $"卖出 {sale.Quantity} 份{FarmGame.GetCrop(crop).CropName}原料，获得 {FormatCoins(sale.RevenueCents)} 金币"
            : sale.ErrorMessage!);
        RefreshAfterGameChange(worldChanged: false);
    }

    private void SellAll()
    {
        SaleResult sale = _game.SellAll();
        ShowMarketFeedback(!sale.Success ? sale.ErrorMessage! : sale.Quantity == 0
            ? "加工品库存为空"
            : $"卖出 {sale.Quantity} 份加工品，获得 {FormatCoins(sale.RevenueCents)} 金币");
        RefreshAfterGameChange(worldChanged: false);
    }

    private void Trade(CommodityId commodity, int quantity, bool buy)
    {
        TradeResult result = buy ? _game.Buy(commodity, quantity) : _game.Sell(commodity, quantity);
        ShowTradeFeedback(commodity, result, buy);
    }

    private void SellCommodityAll(CommodityId commodity) =>
        ShowTradeFeedback(commodity, _game.SellCommodityAll(commodity), buy: false);

    private void ShowTradeFeedback(CommodityId commodity, TradeResult result, bool buy)
    {
        ShowMarketFeedback(result.Success
            ? $"{(buy ? "买入" : "卖出")} {result.Quantity} 份{CommodityCatalog.Get(commodity).Name}，" +
                $"{(buy ? "花费" : "获得")} {FormatCoins(result.TotalCents)} 金币"
            : result.ErrorMessage!);
        RefreshAfterGameChange(worldChanged: false);
    }

    private void ShowMarketFeedback(string message)
    {
        _messageLabel.Text = message;
        _marketWindow.ShowFeedback(message);
    }

    private void OnTick()
    {
        if (_game.IsPaused)
            return;
        TickResult result = _game.AdvanceTick();
        if (result.Harvested > 0 || result.Produced > 0)
            _messageLabel.Text = $"收获 {result.Harvested} 份原料，加工产出 {result.Produced} 份";
        RefreshAfterGameChange(worldChanged: true);
    }

    private void RefreshAfterGameChange(bool worldChanged)
    {
        if (worldChanged)
            _worldMap.SyncFromGame();
        RefreshUi();
    }

    private void RefreshUi()
    {
        _moneyLabel.Text = FormatCoins(_game.MoneyCents);
        _workerLabel.Text = $"{_game.GetWorkers().Count} · 自动照料";
        CalendarSnapshot calendar = _game.Calendar;
        string season = calendar.Season switch
        {
            Season.Spring => "春",
            Season.Summer => "夏",
            Season.Autumn => "秋",
            _ => "冬",
        };
        _calendarLabel.Text = $"{season} · 第 {calendar.Year} 年 · {calendar.Month} 月 · {calendar.Day} 日";
        _pauseButton.Text = _game.IsPaused ? "继续" : "暂停";
        RefreshFooter();
        RefreshDetail();
        if (_inventoryWindow.Visible) _inventoryWindow.Refresh(_game);
        if (_marketWindow.Visible) _marketWindow.Refresh(_game);
        if (_ordersWindow.Visible) _ordersWindow.Refresh(_game);
        if (_cropWindow.Visible) _cropWindow.Refresh(_game);
    }

    private void TogglePause()
    {
        _game.SetPaused(!_game.IsPaused);
        RefreshUi();
    }

    private void RefreshFooter()
    {
        if (_placement is not Placement placement)
        {
            _buildHint.Text = "选择建筑，再点击地图空位摆放";
            _cancelPlacementButton.Hide();
            return;
        }
        string cost = FormatCoins(FarmGame.GetBuildingCostCents(placement.Kind));
        _buildHint.Text = placement.Kind == BuildingKind.Road
            ? $"铺路中：道路 · 连续点击地图空位 · 每格 {cost} 金币"
            : $"摆放中：{placement.Name} · 占地 3×3 · 点击空小格建造 · 费用 {cost} 金币";
        _cancelPlacementButton.Show();
    }

    private void RefreshDetail()
    {
        if (_selectedCell is not Vector2I cell || _placement != null)
        {
            _detailWindow.Hide();
            return;
        }
        PlotSnapshot plot = _game.GetPlot(cell);
        BuildingSpaceSnapshot? space = _game.GetBuildingSpace(cell);
        _detailCell.Text = space == null ? $"地块 ({cell.X}, {cell.Y})" :
            $"建筑锚点 ({space.AnchorCell.X}, {space.AnchorCell.Y}) · 占地 {space.Footprint.Offsets.Count} 格";
        _emptyDetails.Visible = plot.Building == BuildingKind.None;
        _farmDetails.Visible = plot.Building == BuildingKind.Farm;
        _processorDetails.Visible = plot.Building == BuildingKind.Processor;
        _roadDetails.Visible = plot.Building == BuildingKind.Road;
        if (_farmDetails.Visible)
            _farmDetails.Refresh(_game.GetFarmDetails(cell));
        else if (_processorDetails.Visible)
            _processorDetails.Refresh(_game.GetProcessorDetails(cell));
        _detailWindow.Show();
        _detailWindow.ClampToViewport();
    }

    private static PanelContainer MakeStat(string caption, out Label value, string initial = "", int width = 0)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width > 0 ? width : caption == "工人" ? 140 : 105, 48) };
        panel.AddThemeStyleboxOverride("panel", Style(new Color(0.22f, 0.36f, 0.31f), 10));
        var margin = WrapMargin(panel, 10, 4);
        var box = new VBoxContainer();
        margin.AddChild(box);
        box.AddChild(MakeLabel(caption, 11, Muted));
        value = MakeLabel(initial, 16, Cream);
        box.AddChild(value);
        return panel;
    }

}
