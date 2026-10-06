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
    private readonly SimulationDriver _driver = new();
    private WorkerPresentation _workerPresentation = null!;
    private Button _rateButton = null!;
    private long _tickHarvested;
    private long _tickProduced;
#if DEBUG
    private DeveloperToolsWindow _developerWindow = null!;
#endif
    private WorldMap _worldMap = null!;
    private CameraController _camera = null!;
    private Control _uiRoot = null!;
    private Label _moneyLabel = null!;
    private Label _workerLabel = null!;
    private Label _calendarLabel = null!;
    private Button _pauseButton = null!;
    private Label _messageLabel = null!;
    private Label _activityLabel = null!;
    private Label _farmCountLabel = null!;
    private Label _processorCountLabel = null!;
    private Label _calendarStatusLabel = null!;
    private Label _workerStatusLabel = null!;
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
    internal SimulationDriver Driver => _driver;

    public override void _Ready()
    {
        _worldMap = GetNode<WorldMap>("WorldMap");
        _camera = GetNode<CameraController>("Camera2D");
        ProcessPriority = 1;
        _worldMap.SetGame(_game);
        GetNode<Camera2D>("Camera2D").GlobalPosition = _worldMap.GetGridWorldPosition(
            new Vector2((FarmGame.MapSize - 1) / 2f, (FarmGame.MapSize - 1) / 2f));
        _workerPresentation = new WorkerPresentation();
        _worldMap.AddChild(_workerPresentation);
        _workerPresentation.SetGame(_game, _worldMap, _driver);
        _workerPresentation.ProcessPriority = 2;
        _worldMap.SelectionChanged += OnSelectionChanged;
        _uiRoot = GetNode<Control>("CanvasLayer/UiRoot");
        _uiRoot.Theme = SharedTheme;
        BuildInterface();
        UiScaling.Bind(_uiRoot);
        _driver.RateChanged += RefreshSimulationRate;
        RefreshUi();
    }

    public override void _ExitTree()
    {
        _driver.RateChanged -= RefreshSimulationRate;
    }

    public override void _Process(double delta)
    {
        AdvanceSimulation(delta);
#if DEBUG
        _developerWindow.AdvanceIndependent(delta);
#endif
        RefreshPlacementPreview();
    }

    internal uint AdvanceSimulation(double delta)
    {
        _tickHarvested = _tickProduced = 0;
        uint advanced;
        try
        {
#if DEBUG
            advanced = _driver.Advance(delta, _game, ObserveSimulationCheckpoint,
                _developerWindow.GetCurrentMaxTicks);
#else
            advanced = _driver.Advance(delta, _game, ObserveSimulationCheckpoint);
#endif
        }
        catch (System.InvalidOperationException error)
        {
#if DEBUG
            _developerWindow.Abort("经营推进被拒绝：" + error.Message);
#endif
            SetMessage("经营推进被拒绝：" + error.Message);
            return 0;
        }
        if (_tickHarvested > 0 || _tickProduced > 0)
            SetMessage($"收获 {_tickHarvested} 份原料，加工产出 {_tickProduced} 份");
        if (advanced > 0) RefreshAfterGameChange(worldChanged: true);
        return advanced;
    }

    private bool ObserveSimulationCheckpoint(SimulationCheckpoint point)
    {
        _tickHarvested += point.Result.Harvested;
        _tickProduced += point.Result.Produced;
#if DEBUG
        return _developerWindow.ObserveCurrentCheckpoint(point);
#else
        return true;
#endif
    }

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
#if DEBUG
        else if (_developerWindow.Visible) _developerWindow.Hide();
#endif
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

        _detailWindow = new DraggableWindow("DetailWindow", "地块详情", new Vector2(1480, 143),
            new Vector2(409, 727), close: CloseDetail);
        AddWindow(_detailWindow, right: true);
        VBoxContainer detailBody = _detailWindow.Body;
        var detailContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        detailContent.AddThemeConstantOverride("separation", 9);
        detailBody.AddChild(detailContent);
        _detailCell = MakeLabel("", 13, Ink);
        detailContent.AddChild(_detailCell);
        _emptyDetails = MakeInfoCard("空地\n点击底部“建造”选择建筑，再点击地图空位摆放。");
        detailContent.AddChild(_emptyDetails);
        _farmDetails = new FarmDetailsPanel { Name = "FarmDetailsPanel" };
        _farmDetails.ChangeCropRequested += () => OpenCrop(prepareNext: false);
        _farmDetails.PrepareCropRequested += () => OpenCrop(prepareNext: true);
        _farmDetails.CultivationRequested += OpenCultivation;
        _farmDetails.RemoveRequested += RemoveSelected;
        detailContent.AddChild(_farmDetails);
        _processorDetails = new ProcessorDetailsPanel { Name = "ProcessorDetailsPanel" };
        _processorDetails.RemoveRequested += RemoveSelected;
        _processorDetails.InventoryRequested += OpenInventory;
        detailContent.AddChild(_processorDetails);
        _roadDetails = new RoadDetailsPanel { Name = "RoadDetailsPanel" };
        _roadDetails.RemoveRequested += RemoveSelected;
        detailContent.AddChild(_roadDetails);

        _buildWindow = new BuildCatalogWindow();
        _buildWindow.SelectionRequested += (kind, crop) => StartPlacement(new Placement(kind, crop));
        AddWindow(_buildWindow);

        _cropWindow = new CropSelectionWindow();
        _cropWindow.CropRequested += SetCrop;
        _cropWindow.NextCropRequested += PrepareCrop;
        AddWindow(_cropWindow);

        _inventoryWindow = new InventoryWindow();
        _inventoryWindow.RawReserveRequested += SetRawReserve;
        AddWindow(_inventoryWindow);

        _marketWindow = new MarketWindow();
        _marketWindow.SellRawRequested += SellRaw;
        _marketWindow.SellAllRequested += SellAll;
        _marketWindow.BuyRequested += (commodity, quantity) => Trade(commodity, quantity, buy: true);
        _marketWindow.SellRequested += (commodity, quantity) => Trade(commodity, quantity, buy: false);
        _marketWindow.SellCommodityAllRequested += SellCommodityAll;
        _marketWindow.TradeInputRejected += SetMessage;
        _marketWindow.OrdersRequested += OpenTradeOrders;
        AddWindow(_marketWindow);

        _ordersWindow = new TradeOrdersWindow();
        _ordersWindow.CreateRequested += request => ShowOrderResult(_game.CreateTradeOrder(request));
        _ordersWindow.UpdateRequested += (id, request) => ShowOrderResult(_game.UpdateTradeOrder(id, request));
        _ordersWindow.CancelRequested += id => ShowOrderResult(_game.CancelTradeOrder(id));
        _ordersWindow.EnabledRequested += (id, enabled) => ShowOrderResult(_game.SetTradeOrderEnabled(id, enabled));
        AddWindow(_ordersWindow);

        _cultivationWindow = new CultivationWindow();
        _cultivationWindow.SaveRequested += (id, request) =>
        {
            CultivationCommandResult result = id is int existingId
                ? _game.UpdateCultivationPlan(existingId, request) : _game.CreateCultivationPlan(request);
            _cultivationWindow.ShowCommandResult(result);
            SetMessage(result.Error ?? "共享年度表已保存");
            RefreshAfterGameChange(worldChanged: result.Success);
        };
        _cultivationWindow.ApplyRequested += (id, cells) =>
        {
            string? error = _game.ApplyCultivationPlan(id, cells);
            _cultivationWindow.ShowApplyResult(error);
            SetMessage(error ?? "共享表已应用到勾选农田，当前轮保留");
            RefreshAfterGameChange(worldChanged: true);
        };
        _cultivationWindow.DeleteRequested += id =>
        {
            string? error = _game.DeleteCultivationPlan(id);
            _cultivationWindow.ShowDeleteResult(error);
            SetMessage(error ?? "共享年度表已删除，原引用农田按当前作物自动复种");
            RefreshAfterGameChange(worldChanged: false);
        };
        AddWindow(_cultivationWindow);
#if DEBUG
        _developerWindow = new DeveloperToolsWindow(_game, _driver,
            () => RefreshAfterGameChange(worldChanged: true));
        AddWindow(_developerWindow);
        var developerButton = MakeQuietButton("开发测试", 115, 36);
        developerButton.Name = "DeveloperToolsButton";
        developerButton.Position = new Vector2(47, 520);
        developerButton.Pressed += _developerWindow.ShowRaised;
        _uiRoot.AddChild(developerButton);
#endif
    }

    private void AddWindow(DraggableWindow window, bool right = false)
    {
        window.SetInitialPlacement(right);
        _uiRoot.AddChild(window);
    }

    private void BuildTopBar()
    {
        var top = new Control
        {
            Name = "TopBar",
            AnchorRight = 1f,
            OffsetBottom = 139f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _uiRoot.AddChild(top);
        var brand = new PanelContainer
        {
            Name = "BrandPanel",
            Position = new Vector2(44, 38),
            Size = new Vector2(277, 93),
        };
        brand.AddThemeStyleboxOverride("panel", Frame(Paper, 4));
        top.AddChild(brand);
        var brandRow = new HBoxContainer();
        brandRow.AddThemeConstantOverride("separation", 16);
        WrapMargin(AddFrameLining(brand), 17, 11).AddChild(brandRow);
        var logo = new PanelContainer { CustomMinimumSize = new Vector2(57, 57) };
        logo.AddThemeStyleboxOverride("panel", Style(new Color("a58c5a"), 0));
        WrapMargin(logo, 9, 9).AddChild(UiIcons.Create(UiIcon.Leaf, 39, Cream));
        brandRow.AddChild(logo);
        var brandText = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        brandText.AddThemeConstantOverride("separation", 5);
        brandRow.AddChild(brandText);
        Label title = MakeLabel("田间", 26, Ink);
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        brandText.AddChild(title);
        Label englishTitle = MakeLabel("FARM EXCHANGE", 10, Ink);
        englishTitle.AutowrapMode = TextServer.AutowrapMode.Off;
        brandText.AddChild(englishTitle);

        var datePanel = new PanelContainer
        {
            Name = "CalendarPanel",
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -260f,
            OffsetRight = 260f,
            OffsetTop = 38f,
            OffsetBottom = 131f,
            GrowHorizontal = Control.GrowDirection.Both,
        };
        datePanel.AddThemeStyleboxOverride("panel", Frame(Paper, 4));
        top.AddChild(datePanel);
        var dateRow = new HBoxContainer();
        dateRow.AddThemeConstantOverride("separation", 17);
        WrapMargin(AddFrameLining(datePanel), 21, 13).AddChild(dateRow);
        dateRow.AddChild(UiIcons.Create(UiIcon.Leaf, 28, Mid));
        var dateText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        dateText.AddThemeConstantOverride("separation", 5);
        dateRow.AddChild(dateText);
        _calendarLabel = MakeLabel("", 20, Ink);
        _calendarLabel.Name = "CalendarLabel";
        _calendarLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        dateText.AddChild(_calendarLabel);
        _calendarStatusLabel = MakeLabel("", 12, Muted);
        _calendarStatusLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        dateText.AddChild(_calendarStatusLabel);
        dateRow.AddChild(new VSeparator { Name = "CalendarActionSeparator" });
        _rateButton = MakeQuietButton("1×", 65, 46);
        _rateButton.Name = "SimulationRateButton";
        _rateButton.Pressed += () =>
        {
            _driver.SetRate(NextPublicRate(_driver.Rate), SimulationRateSource.Player);
        };
        dateRow.AddChild(_rateButton);
        _pauseButton = MakeQuietButton("", 43, 46);
        _pauseButton.Name = "PauseButton";
        _pauseButton.Icon = UiIcons.Texture(UiIcon.Pause);
        _pauseButton.ExpandIcon = true;
        _pauseButton.AddThemeConstantOverride("icon_max_width", 23);
        _pauseButton.AddThemeColorOverride("icon_normal_color", Ink);
        _pauseButton.AddThemeColorOverride("icon_hover_color", Ink);
        _pauseButton.Pressed += TogglePause;
        dateRow.AddChild(_pauseButton);

        var resources = new PanelContainer
        {
            Name = "ResourcesPanel",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -404f,
            OffsetRight = -31f,
            OffsetTop = 32f,
            OffsetBottom = 139f,
        };
        resources.AddThemeStyleboxOverride("panel", Frame(new Color("f1e3c0"), 5));
        top.AddChild(resources);
        var resourceRow = new HBoxContainer();
        resourceRow.AddThemeConstantOverride("separation", 16);
        WrapMargin(AddFrameLining(resources), 19, 12).AddChild(resourceRow);
        resourceRow.AddChild(UiIcons.Create(UiIcon.Coin, 27, Gold));
        var money = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        money.AddThemeConstantOverride("separation", 3);
        money.AddChild(MakeLabel("可用金币", 12, Muted));
        _moneyLabel = MakeLabel("50.00", 30, Ink);
        _moneyLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        _moneyLabel.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Consolas", "Microsoft YaHei" } });
        money.AddChild(_moneyLabel);
        resourceRow.AddChild(money);
        resourceRow.AddChild(new VSeparator());
        resourceRow.AddChild(UiIcons.Create(UiIcon.People, 27, Ink));
        var workers = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        workers.AddThemeConstantOverride("separation", 3);
        _workerLabel = MakeLabel("", 18, Ink);
        _workerLabel.Name = "WorkerCountLabel";
        _workerLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        workers.AddChild(_workerLabel);
        _workerStatusLabel = MakeLabel("", 12, Muted);
        _workerStatusLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        workers.AddChild(_workerStatusLabel);
        resourceRow.AddChild(workers);
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
            OffsetLeft = -387f,
            OffsetRight = 387f,
            OffsetTop = -177f,
            OffsetBottom = -84f,
        };
        bottom.AddThemeStyleboxOverride("panel", Frame(new Color("71583e"), 5));
        _uiRoot.AddChild(bottom);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        WrapMargin(AddFrameLining(bottom), 10, 8).AddChild(row);
        Button build = DockButton("建造", "BuildButton", UiIcon.Build, 190, new Color("8c774e"));
        build.Pressed += OpenBuild;
        row.AddChild(build);
        row.AddChild(new VSeparator());
        Button inventory = DockButton("库存", "InventoryButton", UiIcon.Box, 167, new Color("71583e"));
        inventory.Pressed += OpenInventory;
        row.AddChild(inventory);
        Button market = DockButton("市场", "MarketButton", UiIcon.Market, 167, new Color("71583e"));
        market.Pressed += OpenMarket;
        row.AddChild(market);
        Button cultivation = DockButton("年度耕作表", "CultivationButton", UiIcon.Calendar, 190, new Color("71583e"));
        cultivation.Pressed += OpenCultivation;
        row.AddChild(cultivation);
    }

    private static Button DockButton(string text, string name, UiIcon icon, float width, Color color)
    {
        Button button = MakeButton(text, color, width, 63);
        button.Name = name;
        button.Icon = UiIcons.Texture(icon);
        button.ExpandIcon = true;
        button.AddThemeConstantOverride("icon_max_width", 29);
        button.AddThemeConstantOverride("h_separation", 14);
        button.AddThemeFontSizeOverride("font_size", 17);
        return button;
    }

    private void BuildMessage()
    {
        var panel = new PanelContainer
        {
            Name = "MessagePanel",
            Position = new Vector2(47, 143),
            Size = new Vector2(299, 389),
        };
        panel.AddThemeStyleboxOverride("panel", Frame(new Color("f1e3c0"), 5));
        _uiRoot.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 19);
        WrapMargin(AddFrameLining(panel), 22, 20).AddChild(body);
        var heading = new HBoxContainer();
        Label title = MakeLabel("田间近况", 17, Ink);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        heading.AddChild(title);
        heading.AddChild(UiIcons.Create(UiIcon.Leaf, 23, new Color("84926d")));
        body.AddChild(heading);
        body.AddChild(ActivityItem(UiIcon.Wheat, "", "作物自动生长", out _farmCountLabel));
        _farmCountLabel.Name = "FarmCountLabel";
        body.AddChild(ActivityItem(UiIcon.Mill, "", "原料自动领取", out _processorCountLabel));
        _processorCountLabel.Name = "ProcessorCountLabel";
        body.AddChild(ActivityItem(UiIcon.Market, "十四种商品，独立报价", "双周行情 · 即时买卖零费", out _));
        body.AddChild(new HSeparator());
        _activityLabel = MakeLabel("", 12, Muted);
        _activityLabel.Name = "ActivityLabel";
        body.AddChild(_activityLabel);
        _messageLabel = MakeLabel("", 12, Muted);
        _messageLabel.Name = "MessageLabel";
        _messageLabel.Hide();
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

    private static Control ActivityItem(UiIcon icon, string caption, string note, out Label label)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 13);
        row.AddChild(UiIcons.Create(icon, 24, new Color("84926d")));
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 5);
        label = MakeLabel(caption, 16, Ink);
        text.AddChild(label);
        text.AddChild(MakeLabel(note, 12, Muted));
        row.AddChild(text);
        return row;
    }

    private void OnSelectionChanged(Vector2I cell)
    {
        if (_placement is Placement placement)
        {
            SetPlacementCandidate(cell, placement);
            PlacementResult result = _game.TryPlace(cell, placement.Kind, placement.Crop);
            if (!result.Success)
            {
                SetMessage(result.ErrorMessage);
                _worldMap.ClearSelection();
                RefreshPlacementPreview();
                return;
            }
            SetMessage($"{placement.Name}已建造，花费 {FormatCoins(result.ChargedCents)} 金币");
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
        SetMessage($"移动鼠标预览{placement.Name}，左键短按建造，拖动平移；右键或 Esc 取消");
        RefreshPlacementPreview();
    }

    private void CancelPlacement()
    {
        _placement = null;
        ClearPlacementPreview();
        SetMessage("已取消摆放");
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
        SetMessage(error ?? $"农田已改种{FarmGame.GetCrop(crop).CropName}");
        if (error == null)
            _cropWindow.Hide();
        RefreshAfterGameChange(worldChanged: true);
    }

    private void RemoveSelected()
    {
        if (_selectedCell is not Vector2I cell)
            return;
        string? error = _game.RemoveBuilding(cell);
        SetMessage(error ?? "建筑已移除；不退还建造费");
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
        SetMessage(error ?? $"下一轮已预备{FarmGame.GetCrop(crop).CropName}，本田已转为手动");
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
        SetMessage(result.Success ? $"委托 #{result.Id} 操作成功" : result.ErrorMessage);
        RefreshAfterGameChange(worldChanged: false);
    }

    private void SetRawReserve(CropKind crop, int quantity)
    {
        RawReserveFailure failure = _game.SetRawReserve(crop, quantity);
        if (failure == RawReserveFailure.None)
        {
            _inventoryWindow.ConfirmRawReserve(crop, quantity);
            SetMessage($"{FarmGame.GetCrop(crop).CropName}原料保留底线已设为 {quantity}");
        }
        else
            SetMessage(failure == RawReserveFailure.InvalidCrop
                ? "无效作物" : "保留底线必须是非负整数");
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
        SetMessage(message);
        _marketWindow.ShowFeedback(message);
    }

    private void RefreshAfterGameChange(bool worldChanged)
    {
        if (worldChanged)
            _worldMap.SyncFromGame();
        RefreshUi();
    }

    private void SetMessage(string? message)
    {
        _messageLabel.Text = message ?? "";
        _messageLabel.Visible = !string.IsNullOrEmpty(_messageLabel.Text);
    }

    private static double NextPublicRate(double rate) => rate switch { 1 => 2, 2 => 0.5, _ => 1 };

    private void RefreshSimulationRate(double rate, SimulationRateSource source)
    {
        _rateButton.Text = rate.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture) + "×";
        _rateButton.TooltipText = "点击切换至 " + NextPublicRate(rate).ToString(System.Globalization.CultureInfo.InvariantCulture) + "×";
    }

    private void RefreshUi()
    {
        _moneyLabel.Text = FormatCoins(_game.AvailableMoneyCents);
        _moneyLabel.TooltipText = $"总余额 {FormatCoins(_game.MoneyCents)} · 冻结 {FormatCoins(_game.FrozenMoneyCents)}";
        _workerLabel.Text = $"{_game.GetWorkers().Count} 名工人";
        RefreshSimulationRate(_driver.Rate, SimulationRateSource.Player);
        _workerStatusLabel.Text = _game.IsPaused ? "照料已暂停" : "自动照料中";
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
        _pauseButton.Icon = UiIcons.Texture(_game.IsPaused ? UiIcon.Play : UiIcon.Pause);
        _pauseButton.TooltipText = _game.IsPaused ? "继续经营" : "暂停经营";
        _calendarStatusLabel.Text = _game.IsPaused ? "经营已暂停 · 仍可交易" : "自动照料 · 成熟自动入库";
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
        _farmCountLabel.Text = $"{farms} 块农田";
        _processorCountLabel.Text = $"{processors} 处加工场地";
        _activityLabel.Text = $"道路 {roads} 格\n" +
            (_game.IsPaused ? "经营已暂停，仍可主动交易。" : "工人自动照料，成熟后自动入库。");
    }

    private void TogglePause()
    {
        _game.SetPaused(!_game.IsPaused);
        _workerPresentation._Process(0);
        RefreshUi();
    }

    private void RefreshFooter()
    {
        if (_placement is not Placement placement)
        {
            _buildHint.Text = "建造 → 选择设施\n点击地图空位连续摆放";
            _buildHint.Hide();
            _buildCost.Hide();
            _cancelPlacementButton.Hide();
            return;
        }
        int costCents = FarmGame.GetBuildingCostCents(placement.Kind);
        _buildHint.Show();
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
        _detailWindow.SetTitle(plot.Building switch
        {
            BuildingKind.Farm => "农田详情",
            BuildingKind.Processor => "加工场地详情",
            BuildingKind.Road => "道路详情",
            _ => "地块详情",
        });
        _detailCell.Visible = plot.Building == BuildingKind.None;
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
            _processorDetails.RefreshPlacement(space!, plot.CropKind);
        }
        if (_roadDetails.Visible) _roadDetails.RefreshPlacement(space!);
        _detailWindow.Show();
        _detailWindow.ClampToViewport();
    }

}
