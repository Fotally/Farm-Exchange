using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Cultivation;
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
    private CameraController _camera = null!;
    private Control _uiRoot = null!;
    private Label _moneyLabel = null!;
    private Label _workerLabel = null!;
    private Label _calendarLabel = null!;
    private Button _pauseButton = null!;
    private Label _messageLabel = null!;
    private Label _activityLabel = null!;
    private Label _buildHint = null!;
    private Label _buildCost = null!;
    private Button _cancelPlacementButton = null!;
    private DraggableWindow _detailWindow = null!;
    private BuildCatalogWindow _buildWindow = null!;
    private CropSelectionWindow _cropWindow = null!;
    private InventoryWindow _inventoryWindow = null!;
    private MarketWindow _marketWindow = null!;
    private TradeOrdersWindow _ordersWindow = null!;
    private CultivationWindow _cultivationWindow = null!;
    private Label _detailCell = null!;
    private PanelContainer _emptyDetails = null!;
    private FarmDetailsPanel _farmDetails = null!;
    private ProcessorDetailsPanel _processorDetails = null!;
    private RoadDetailsPanel _roadDetails = null!;
    private Vector2I? _selectedCell;
    private Placement? _placement;
    private Vector2I? _placementCell;
    private LandFailure _placementFailure;

    private readonly record struct Placement(BuildingKind Kind, CropKind Crop)
    {
        public string Name => Kind == BuildingKind.Farm ? "农田" :
            Kind == BuildingKind.Road ? "道路" : FarmGame.GetCrop(Crop).BuildingName;
    }

    internal FarmGame Game => _game;

    public override void _Ready()
    {
        _worldMap = GetNode<WorldMap>("WorldMap");
        _camera = GetNode<CameraController>("Camera2D");
        ProcessPriority = 1;
        _worldMap.SetGame(_game);
        GetNode<Camera2D>("Camera2D").GlobalPosition = _worldMap.GetGridWorldPosition(
            new Vector2((FarmGame.MapSize - 1) / 2f, (FarmGame.MapSize - 1) / 2f));
        var workerPresentation = new WorkerPresentation();
        _worldMap.AddChild(workerPresentation);
        workerPresentation.SetGame(_game, _worldMap);
        _worldMap.SelectionChanged += OnSelectionChanged;
        _uiRoot = GetNode<Control>("CanvasLayer/UiRoot");
        _uiRoot.Theme = SharedTheme;
        BuildInterface();
        GetNode<Timer>("TickTimer").Timeout += OnTick;
        RefreshUi();
    }

    public override void _Process(double delta) => RefreshPlacementPreview();

    public override void _Input(InputEvent inputEvent)
    {
        if (_placement == null) return;
        if (inputEvent is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } ||
            inputEvent is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            CancelPlacement();
            GetViewport().SetInputAsHandled();
        }
        else if (inputEvent is InputEventMouseMotion motion)
            RefreshPlacementPreview(motion.Position);
        else if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mouse)
            RefreshPlacementPreview(mouse.Position);
    }

    private void ClearPlacementPreview()
    {
        _placementCell = null;
        _placementFailure = LandFailure.None;
        _worldMap.ClearPlacementPreview();
    }

    private void RefreshPlacementPreview(Vector2? screenPosition = null)
    {
        if (_placement is not Placement placement)
        {
            ClearPlacementPreview();
            return;
        }
        Vector2 mousePosition = screenPosition ?? GetViewport().GetMousePosition();
        bool overUi = IsPointerOverUi(_uiRoot, mousePosition);
        if (!_camera.IsMouseInsideWindow || _camera.IsDragging ||
            !GetViewport().GetVisibleRect().HasPoint(mousePosition) || overUi)
        {
            ClearPlacementPreview();
            RefreshFooter();
            return;
        }
        SetPlacementCandidate(_worldMap.ScreenToCell(mousePosition), placement);
    }

    private static bool IsPointerOverUi(Control control, Vector2 position)
    {
        if (!control.IsVisibleInTree()) return false;
        if (control.MouseFilter != Control.MouseFilterEnum.Ignore)
            return control.GetGlobalRect().HasPoint(position);
        foreach (Node child in control.GetChildren())
            if (child is Control nested && IsPointerOverUi(nested, position)) return true;
        return false;
    }

    private void SetPlacementCandidate(Vector2I cell, Placement placement)
    {
        _placementCell = cell;
        _worldMap.UpdatePlacementPreview(placement.Kind, placement.Crop, cell);
        _placementFailure = _game.CheckPlacement(_placementCell.Value, placement.Kind, placement.Crop).Failure;
        RefreshFooter();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Keycode: Key.Escape })
            return;
        if (_placement != null) CancelPlacement();
        else if (_cultivationWindow.Visible) _cultivationWindow.Hide();
        else if (_ordersWindow.Visible) _ordersWindow.Hide();
        else if (_marketWindow.Visible) _marketWindow.Hide();
        else if (_inventoryWindow.Visible) _inventoryWindow.Hide();
        else if (_cropWindow.Visible) _cropWindow.Hide();
        else if (_buildWindow.Visible) _buildWindow.Hide();
        else if (_detailWindow.Visible) CloseDetail();
        else return;
        GetViewport().SetInputAsHandled();
    }

    private void BuildInterface()
    {
        BuildTopBar();
        BuildBottomBar();
        BuildMessage();

        _detailWindow = new DraggableWindow("DetailWindow", "地块详情", new Vector2(914, 104),
            new Vector2(350, 490), close: CloseDetail);
        _uiRoot.AddChild(_detailWindow);
        VBoxContainer detailBody = _detailWindow.Body;
        var detailContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        detailContent.AddThemeConstantOverride("separation", 9);
        detailBody.AddChild(detailContent);
        _detailCell = MakeLabel("", 13, Ink);
        detailContent.AddChild(_detailCell);
        _emptyDetails = MakeInfoCard("空地\n点击底部“建造”选择建筑，再点击地图空位摆放。");
        detailContent.AddChild(_emptyDetails);
        _farmDetails = new FarmDetailsPanel();
        _farmDetails.ChangeCropRequested += () => OpenCrop(prepareNext: false);
        _farmDetails.PrepareCropRequested += () => OpenCrop(prepareNext: true);
        _farmDetails.CultivationRequested += OpenCultivation;
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
        _cropWindow.NextCropRequested += PrepareCrop;
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

        _cultivationWindow = new CultivationWindow();
        _cultivationWindow.SaveRequested += (id, request) =>
        {
            CultivationCommandResult result = id is int existingId
                ? _game.UpdateCultivationPlan(existingId, request) : _game.CreateCultivationPlan(request);
            _cultivationWindow.ShowCommandResult(result);
            _messageLabel.Text = result.Error ?? "共享年度表已保存";
            RefreshAfterGameChange(worldChanged: result.Success);
        };
        _cultivationWindow.ApplyRequested += (id, cells) =>
        {
            string? error = _game.ApplyCultivationPlan(id, cells);
            _cultivationWindow.ShowApplyResult(error);
            _messageLabel.Text = error ?? "共享表已应用到勾选农田，当前轮保留";
            RefreshAfterGameChange(worldChanged: true);
        };
        _uiRoot.AddChild(_cultivationWindow);
    }

    private void BuildTopBar()
    {
        var top = new Control
        {
            Name = "TopBar",
            AnchorRight = 1f,
            OffsetBottom = 84f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _uiRoot.AddChild(top);
        var brand = new PanelContainer
        {
            Name = "BrandPanel",
            Position = new Vector2(20, 20),
            Size = new Vector2(200, 62),
        };
        top.AddChild(brand);
        var brandRow = new HBoxContainer();
        WrapMargin(brand, 14, 9).AddChild(brandRow);
        Label mark = MakeLabel("田", 27, Mid);
        mark.CustomMinimumSize = new Vector2(42, 0);
        brandRow.AddChild(mark);
        Label title = MakeLabel("农场交易", 21, Ink);
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        brandRow.AddChild(title);

        var datePanel = new PanelContainer
        {
            Name = "CalendarPanel",
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -171f,
            OffsetRight = 171f,
            OffsetTop = 20f,
            OffsetBottom = 82f,
        };
        top.AddChild(datePanel);
        var dateRow = new HBoxContainer();
        WrapMargin(datePanel, 12, 8).AddChild(dateRow);
        _calendarLabel = MakeLabel("", 15, Ink);
        _calendarLabel.Name = "CalendarLabel";
        _calendarLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        _calendarLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _calendarLabel.VerticalAlignment = VerticalAlignment.Center;
        dateRow.AddChild(_calendarLabel);
        _pauseButton = MakeSecondaryButton("暂停", 64, 38);
        _pauseButton.Name = "PauseButton";
        _pauseButton.Pressed += TogglePause;
        dateRow.AddChild(_pauseButton);

        var resources = new PanelContainer
        {
            Name = "ResourcesPanel",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -310f,
            OffsetRight = -20f,
            OffsetTop = 20f,
            OffsetBottom = 82f,
        };
        top.AddChild(resources);
        var resourceRow = new HBoxContainer();
        WrapMargin(resources, 12, 5).AddChild(resourceRow);
        resourceRow.AddChild(MakeStat("金币", out _moneyLabel, width: 116));
        resourceRow.AddChild(MakeStat("工人", out _workerLabel, width: 130));
        _workerLabel.Name = "WorkerCountLabel";
    }

    private void BuildBottomBar()
    {
        var bottom = new PanelContainer
        {
            Name = "BottomBar",
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -270f,
            OffsetRight = 270f,
            OffsetTop = -78f,
            OffsetBottom = -18f,
        };
        bottom.AddThemeStyleboxOverride("panel", Style(Dark, 0));
        _uiRoot.AddChild(bottom);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        WrapMargin(bottom, 10, 7).AddChild(row);
        Button build = MakeButton("建造", Gold, 124, 44);
        build.Name = "BuildButton";
        build.Pressed += OpenBuild;
        row.AddChild(build);
        Button inventory = MakeButton("库存", Dark, 116, 44);
        inventory.Name = "InventoryButton";
        inventory.Pressed += OpenInventory;
        row.AddChild(inventory);
        Button market = MakeButton("市场", Dark, 116, 44);
        market.Name = "MarketButton";
        market.Pressed += OpenMarket;
        row.AddChild(market);
        Button cultivation = MakeButton("年度耕作表", Dark, 136, 44);
        cultivation.Name = "CultivationButton";
        cultivation.Pressed += OpenCultivation;
        row.AddChild(cultivation);
    }

    private void BuildMessage()
    {
        var panel = new PanelContainer
        {
            Name = "MessagePanel",
            Position = new Vector2(20, 104),
            Size = new Vector2(270, 0),
        };
        _uiRoot.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        WrapMargin(panel, 14, 14).AddChild(body);
        body.AddChild(MakeLabel("农场近况", 17, Ink));
        _activityLabel = MakeLabel("", 14, Ink);
        _activityLabel.Name = "ActivityLabel";
        body.AddChild(_activityLabel);
        body.AddChild(new HSeparator());
        _messageLabel = MakeLabel("点击地图设施查看详情", 13, Muted);
        _messageLabel.Name = "MessageLabel";
        body.AddChild(_messageLabel);
        _buildHint = MakeLabel("", 13, Ink);
        _buildHint.Name = "BuildHint";
        _buildHint.AutowrapMode = TextServer.AutowrapMode.Off;
        body.AddChild(_buildHint);
        _buildCost = MakeLabel("", 14, Ink);
        _buildCost.Name = "BuildCost";
        _buildCost.AutowrapMode = TextServer.AutowrapMode.Off;
        _buildCost.Hide();
        body.AddChild(_buildCost);
        _cancelPlacementButton = MakeSecondaryButton("取消摆放 Esc", 0, 36);
        _cancelPlacementButton.Name = "CancelPlacementButton";
        _cancelPlacementButton.Pressed += CancelPlacement;
        _cancelPlacementButton.Hide();
        body.AddChild(_cancelPlacementButton);
    }

    private void OnSelectionChanged(Vector2I cell)
    {
        if (_placement is Placement placement)
        {
            SetPlacementCandidate(cell, placement);
            PlacementResult result = _game.TryPlace(cell, placement.Kind, placement.Crop);
            if (!result.Success)
            {
                _messageLabel.Text = result.ErrorMessage;
                _worldMap.ClearSelection();
                RefreshPlacementPreview();
                return;
            }
            _messageLabel.Text = $"{placement.Name}已建造，花费 {FormatCoins(result.ChargedCents)} 金币";
            _worldMap.ClearSelection();
            RefreshAfterGameChange(worldChanged: true);
            RefreshPlacementPreview();
            return;
        }
        _selectedCell = cell;
        _cultivationWindow.Hide();
        _cropWindow.Hide();
        _inventoryWindow.Hide();
        _marketWindow.Hide();
        _ordersWindow.Hide();
        RefreshUi();
        _uiRoot.MoveChild(_detailWindow, _uiRoot.GetChildCount() - 1);
    }

    private void OpenBuild()
    {
        _cultivationWindow.Hide();
        CancelPlacement();
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
        ClearPlacementPreview();
        _selectedCell = null;
        _worldMap.ClearSelection();
        _detailWindow.Hide();
        _buildWindow.Hide();
        _cropWindow.Hide();
        _messageLabel.Text = $"移动鼠标预览{placement.Name}，左键短按建造，拖动平移；右键或 Esc 取消";
        RefreshPlacementPreview();
    }

    private void CancelPlacement()
    {
        _placement = null;
        ClearPlacementPreview();
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

    private void OpenCrop(bool prepareNext)
    {
        if (_selectedCell is not Vector2I cell || _game.GetPlot(cell).Building != BuildingKind.Farm)
            return;
        _buildWindow.Hide();
        _cropWindow.SetManualMode(prepareNext, cell);
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
        if (error == null)
            _cropWindow.Hide();
        RefreshAfterGameChange(worldChanged: true);
    }

    private void OpenInventory()
    {
        _cultivationWindow.Hide();
        _buildWindow.Hide();
        _cropWindow.Hide();
        _marketWindow.Hide();
        _ordersWindow.Hide();
        _inventoryWindow.Refresh(_game);
        _inventoryWindow.ShowRaised();
    }

    private void OpenMarket()
    {
        _cultivationWindow.Hide();
        _buildWindow.Hide();
        _cropWindow.Hide();
        _inventoryWindow.Hide();
        _ordersWindow.Hide();
        _marketWindow.Refresh(_game);
        _marketWindow.ShowRaised();
    }

    private void PrepareCrop(CropKind crop)
    {
        if (_selectedCell is not Vector2I cell) return;
        string? error = _game.PrepareFarmCrop(cell, crop);
        _messageLabel.Text = error ?? $"下一轮已预备{FarmGame.GetCrop(crop).CropName}，本田已转为手动";
        if (error == null) _cropWindow.Hide();
        RefreshAfterGameChange(worldChanged: true);
    }

    private void OpenCultivation()
    {
        CancelPlacement();
        _buildWindow.Hide();
        _cropWindow.Hide();
        _inventoryWindow.Hide();
        _marketWindow.Hide();
        _ordersWindow.Hide();
        _detailWindow.Hide();
        _cultivationWindow.Refresh(_game);
        _cultivationWindow.ShowRaised();
        RefreshFooter();
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
        RefreshActivity();
        _pauseButton.Text = _game.IsPaused ? "继续" : "暂停";
        RefreshFooter();
        RefreshDetail();
        if (_inventoryWindow.Visible) _inventoryWindow.Refresh(_game);
        if (_marketWindow.Visible) _marketWindow.Refresh(_game);
        if (_ordersWindow.Visible) _ordersWindow.Refresh(_game);
        if (_cropWindow.Visible) _cropWindow.Refresh(_game);
        if (_cultivationWindow.Visible) _cultivationWindow.Refresh(_game);
    }

    private void RefreshActivity()
    {
        int farms = 0, processors = 0, roads = 0;
        foreach (BuildingSpaceSnapshot space in _game.GetBuildingSpaces())
        {
            if (space.Building == BuildingKind.Farm) farms++;
            else if (space.Building == BuildingKind.Processor) processors++;
            else if (space.Building == BuildingKind.Road) roads++;
        }
        _activityLabel.Text = $"{farms} 块农田 · 作物自动生长\n" +
            $"{processors} 处加工场地 · 自动领取\n{roads} 格道路\n" +
            $"十四种商品 · 独立报价\n{(_game.IsPaused ? "经营已暂停 · 仍可交易" : "工人自动照料中")}";
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
            _buildHint.Text = "建造 → 选择设施\n点击地图空位连续摆放";
            _buildCost.Hide();
            _cancelPlacementButton.Hide();
            return;
        }
        int costCents = FarmGame.GetBuildingCostCents(placement.Kind);
        bool insufficient = _game.AvailableMoneyCents < costCents;
        _buildHint.Text = $"{placement.Name} · 占地 {BuildingFootprint.Get(placement.Kind).Offsets.Count} 格\n" +
            "左键建造 · 拖动平移\n右键 / Esc 取消" +
            (_placementFailure == LandFailure.None ? "" :
                $"\n无法建造：\n{new PlacementResult(_placementFailure, 0).ErrorMessage}");
        _buildCost.Text = insufficient
            ? $"费用 {FormatCoins(costCents)} 金币\n可用 {FormatCoins(_game.AvailableMoneyCents)}"
            : $"费用 {FormatCoins(costCents)} 金币";
        Color costColor = insufficient ? new Color("a34131") : Ink;
        if (_buildCost.GetThemeColor("font_color") != costColor)
            _buildCost.AddThemeColorOverride("font_color", costColor);
        _buildCost.Show();
        _cancelPlacementButton.Show();
    }

    private void RefreshDetail()
    {
        if (_selectedCell is not Vector2I cell || _placement != null || _cultivationWindow.Visible)
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
        {
            _farmDetails.Refresh(_game.GetFarmDetails(cell));
            _farmDetails.RefreshCultivation(_game, cell);
        }
        else if (_processorDetails.Visible)
        {
            _processorDetails.Refresh(_game.GetProcessorDetails(cell));
            _processorDetails.RefreshProgress(plot);
        }
        _detailWindow.Show();
        _detailWindow.ClampToViewport();
    }

    private static PanelContainer MakeStat(string caption, out Label value, string initial = "", int width = 0)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width > 0 ? width : caption == "工人" ? 140 : 105, 48) };
        panel.AddThemeStyleboxOverride("panel", Style(Paper, 0));
        var margin = WrapMargin(panel, 10, 4);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 1);
        margin.AddChild(box);
        box.AddChild(MakeLabel(caption, 11, Muted));
        value = MakeLabel(initial, 16, Ink);
        box.AddChild(value);
        return panel;
    }

}
