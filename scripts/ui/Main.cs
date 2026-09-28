using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Time;
using FarmExchange.World;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class Main : Node2D
{
    private readonly FarmGame _game = new();
    private WorldMap _worldMap = null!;
    private Control _uiRoot = null!;
    private Label _moneyLabel = null!;
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
    private Label _detailCell = null!;
    private PanelContainer _emptyDetails = null!;
    private FarmDetailsPanel _farmDetails = null!;
    private ProcessorDetailsPanel _processorDetails = null!;
    private Vector2I? _selectedCell;
    private Placement? _placement;

    private readonly record struct Placement(BuildingKind Kind, CropKind Crop)
    {
        public string Name => Kind == BuildingKind.Farm ? "农田" : FarmGame.GetCrop(Crop).BuildingName;
    }

    internal FarmGame Game => _game;

    public override void _Ready()
    {
        _worldMap = GetNode<WorldMap>("WorldMap");
        _worldMap.SetGame(_game);
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
        if (_marketWindow.Visible) _marketWindow.Hide();
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

        _buildWindow = new BuildCatalogWindow();
        _buildWindow.SelectionRequested += (kind, crop) => StartPlacement(new Placement(kind, crop));
        _uiRoot.AddChild(_buildWindow);

        _cropWindow = new CropSelectionWindow();
        _cropWindow.CropRequested += SetCrop;
        _uiRoot.AddChild(_cropWindow);

        _inventoryWindow = new InventoryWindow();
        _uiRoot.AddChild(_inventoryWindow);

        _marketWindow = new MarketWindow();
        _marketWindow.SellRawRequested += SellRaw;
        _marketWindow.SellAllRequested += SellAll;
        _uiRoot.AddChild(_marketWindow);
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
        row.AddChild(MakeStat("工人", out _, "1 · 自动照料"));
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
            _placement = null;
            built = true;
        }
        _selectedCell = cell;
        _cropWindow.Hide();
        _inventoryWindow.Hide();
        _marketWindow.Hide();
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
        _messageLabel.Text = $"选择地图空位摆放{placement.Name}，按 Esc 可取消";
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
        _inventoryWindow.Refresh(_game);
        _inventoryWindow.ShowRaised();
    }

    private void OpenMarket()
    {
        _buildWindow.Hide();
        _cropWindow.Hide();
        _inventoryWindow.Hide();
        _marketWindow.Refresh(_game);
        _marketWindow.ShowRaised();
    }

    private void SellRaw(CropKind crop)
    {
        SaleResult sale = _game.SellRaw(crop);
        _messageLabel.Text = $"卖出 {sale.Quantity} 份{FarmGame.GetCrop(crop).CropName}原料，获得 {FormatCoins(sale.RevenueCents)} 金币";
        RefreshAfterGameChange(worldChanged: false);
    }

    private void SellAll()
    {
        SaleResult sale = _game.SellAll();
        _messageLabel.Text = sale.Quantity == 0
            ? "加工品库存为空"
            : $"卖出 {sale.Quantity} 份加工品，获得 {FormatCoins(sale.RevenueCents)} 金币";
        RefreshAfterGameChange(worldChanged: false);
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
        if (_cropWindow.Visible) _cropWindow.Refresh(_game);
    }

    private void TogglePause()
    {
        _game.SetPaused(!_game.IsPaused);
        RefreshUi();
    }

    private void RefreshFooter()
    {
        _buildHint.Text = _placement is Placement placement
            ? $"摆放中：{placement.Name} · 点击地图空位建造 · 费用 {FormatCoins(FarmGame.BuildingCostCents)} 金币"
            : "选择建筑，再点击地图空位摆放";
        _cancelPlacementButton.Visible = _placement != null;
    }

    private void RefreshDetail()
    {
        if (_selectedCell is not Vector2I cell || _placement != null)
        {
            _detailWindow.Hide();
            return;
        }
        PlotSnapshot plot = _game.GetPlot(cell);
        _detailCell.Text = $"地块 ({cell.X}, {cell.Y})";
        _emptyDetails.Visible = plot.Building == BuildingKind.None;
        _farmDetails.Visible = plot.Building == BuildingKind.Farm;
        _processorDetails.Visible = plot.Building == BuildingKind.Processor;
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
