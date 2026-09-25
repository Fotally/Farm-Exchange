using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.World;

namespace FarmExchange.UI;

public partial class Main : Node2D
{
    private static readonly Color Dark = new(0.13f, 0.24f, 0.22f);
    private static readonly Color Mid = new(0.25f, 0.42f, 0.35f);
    private static readonly Color Cream = new(1f, 0.98f, 0.91f);
    private static readonly Color Ink = new(0.16f, 0.29f, 0.25f);
    private static readonly Color Gold = new(0.74f, 0.55f, 0.32f);
    private static readonly Color Muted = new(0.70f, 0.75f, 0.69f);

    private readonly FarmGame _game = new();
    private readonly List<Control> _windows = new();
    private WorldMap _worldMap = null!;
    private Control _uiRoot = null!;
    private Label _moneyLabel = null!;
    private Label _messageLabel = null!;
    private Label _buildHint = null!;
    private Button _cancelPlacementButton = null!;
    private PanelContainer _detailWindow = null!;
    private PanelContainer _buildWindow = null!;
    private PanelContainer _cropWindow = null!;
    private PanelContainer _inventoryWindow = null!;
    private PanelContainer _marketWindow = null!;
    private VBoxContainer _detailContent = null!;
    private GridContainer _buildCards = null!;
    private LineEdit _buildSearch = null!;
    private VBoxContainer _cropCards = null!;
    private VBoxContainer _inventoryRows = null!;
    private VBoxContainer _marketRows = null!;
    private Button _marketSellButton = null!;
    private Vector2I? _selectedCell;
    private Placement? _placement;
    private bool _showProcessors;
    private Control? _dragWindow;
    private Vector2 _dragOffset;

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
        GetViewport().SizeChanged += ClampWindows;
        RefreshUi();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_dragWindow == null)
            return;
        if (inputEvent is InputEventMouseMotion motion)
        {
            _dragWindow.Position = motion.Position + _dragOffset;
            ClampWindow(_dragWindow);
            GetViewport().SetInputAsHandled();
        }
        else if (inputEvent is InputEventMouseButton mouse &&
                 mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed)
        {
            _dragWindow = null;
            GetViewport().SetInputAsHandled();
        }
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

        _detailWindow = CreateWindow("DetailWindow", "地块详情", new Vector2(914, 86),
            new Vector2(350, 500), CloseDetail, out VBoxContainer detailBody);
        var detailScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        detailBody.AddChild(detailScroll);
        _detailContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _detailContent.AddThemeConstantOverride("separation", 9);
        detailScroll.AddChild(_detailContent);

        _buildWindow = CreateWindow("BuildWindow", "建造目录", new Vector2(30, 252),
            new Vector2(680, 340), () => _buildWindow.Hide(), out VBoxContainer buildBody);
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        buildBody.AddChild(tabs);
        Button farmTab = MakeButton("♧ 农田", Mid, 100, 40);
        farmTab.Name = "FarmTab";
        farmTab.Pressed += () => { _showProcessors = false; RebuildBuildCards(); };
        tabs.AddChild(farmTab);
        Button processorTab = MakeButton("⚙ 加工场地", Mid, 132, 40);
        processorTab.Name = "ProcessorTab";
        processorTab.Pressed += () => { _showProcessors = true; RebuildBuildCards(); };
        tabs.AddChild(processorTab);
        _buildSearch = new LineEdit { Name = "BuildSearch", PlaceholderText = "按名称搜索加工场地" };
        _buildSearch.TextChanged += _ => RebuildBuildCards();
        buildBody.AddChild(_buildSearch);
        var buildScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        buildBody.AddChild(buildScroll);
        _buildCards = new GridContainer { Name = "BuildCards", Columns = 3 };
        _buildCards.AddThemeConstantOverride("h_separation", 8);
        _buildCards.AddThemeConstantOverride("v_separation", 8);
        buildScroll.AddChild(_buildCards);
        RebuildBuildCards();

        _cropWindow = CreateWindow("CropWindow", "选择作物", new Vector2(455, 140),
            new Vector2(430, 390), () => _cropWindow.Hide(), out VBoxContainer cropBody);
        var cropScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        cropBody.AddChild(cropScroll);
        _cropCards = new VBoxContainer { Name = "CropCards", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _cropCards.AddThemeConstantOverride("separation", 7);
        cropScroll.AddChild(_cropCards);

        _inventoryWindow = CreateWindow("InventoryWindow", "库存", new Vector2(310, 140),
            new Vector2(660, 455), () => _inventoryWindow.Hide(), out VBoxContainer inventoryBody);
        var inventoryScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        inventoryBody.AddChild(inventoryScroll);
        _inventoryRows = new VBoxContainer { Name = "InventoryRows", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _inventoryRows.AddThemeConstantOverride("separation", 8);
        inventoryScroll.AddChild(_inventoryRows);

        _marketWindow = CreateWindow("MarketWindow", "市场", new Vector2(310, 120),
            new Vector2(660, 485), () => _marketWindow.Hide(), out VBoxContainer marketBody);
        var marketScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        marketBody.AddChild(marketScroll);
        _marketRows = new VBoxContainer { Name = "MarketRows", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _marketRows.AddThemeConstantOverride("separation", 8);
        marketScroll.AddChild(_marketRows);
        _marketSellButton = MakeButton("出售全部加工品", Gold, 0, 43);
        _marketSellButton.Name = "SellButton";
        _marketSellButton.Pressed += SellAll;
        marketBody.AddChild(_marketSellButton);
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

    private PanelContainer CreateWindow(string name, string title, Vector2 position, Vector2 size,
        Action close, out VBoxContainer body)
    {
        var window = new PanelContainer { Name = name, Position = position, Size = size, Visible = false };
        window.MouseFilter = Control.MouseFilterEnum.Stop;
        window.AddThemeStyleboxOverride("panel", Style(Cream, 16));
        _uiRoot.AddChild(window);
        _windows.Add(window);
        var outer = new VBoxContainer();
        window.AddChild(outer);
        var header = new PanelContainer { Name = "Header", CustomMinimumSize = new Vector2(0, 58) };
        header.AddThemeStyleboxOverride("panel", Style(Dark, 14));
        header.GuiInput += inputEvent => StartWindowDrag(window, inputEvent);
        outer.AddChild(header);
        var headerMargin = WrapMargin(header, 16, 9);
        headerMargin.MouseFilter = Control.MouseFilterEnum.Ignore;
        var headerRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        headerMargin.AddChild(headerRow);
        Label titleLabel = MakeLabel(title, 19, Cream);
        titleLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        headerRow.AddChild(titleLabel);
        Button closeButton = MakeButton("×", Mid, 32, 32);
        closeButton.Name = "CloseButton";
        closeButton.Pressed += close;
        headerRow.AddChild(closeButton);
        var contentMargin = WrapMargin(outer, 16, 14);
        contentMargin.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body = new VBoxContainer { Name = "Body", SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        contentMargin.AddChild(body);
        window.Size = size;
        return window;
    }

    private void StartWindowDrag(Control window, InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            return;
        _dragWindow = window;
        _dragOffset = window.Position - GetViewport().GetMousePosition();
        _uiRoot.MoveChild(window, _uiRoot.GetChildCount() - 1);
        GetViewport().SetInputAsHandled();
    }

    private void ClampWindows()
    {
        foreach (Control window in _windows)
            ClampWindow(window);
    }

    private void ShowWindow(Control window)
    {
        window.Show();
        _uiRoot.MoveChild(window, _uiRoot.GetChildCount() - 1);
        ClampWindow(window);
    }

    private void ClampWindow(Control window)
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float bottom = window == _buildWindow || window == _cropWindow ? viewport.Y - 111f : viewport.Y;
        float maxX = Math.Max(8f, viewport.X - window.Size.X - 8f);
        float maxY = Math.Max(78f, bottom - window.Size.Y - 8f);
        window.Position = new Vector2(Math.Clamp(window.Position.X, 8f, maxX),
            Math.Clamp(window.Position.Y, 78f, maxY));
    }

    private void OnSelectionChanged(Vector2I cell)
    {
        if (_placement is Placement placement)
        {
            string? error = placement.Kind == BuildingKind.Farm
                ? _game.BuildFarm(cell)
                : _game.BuildProcessor(cell, placement.Crop);
            if (error != null)
            {
                _messageLabel.Text = error;
                _worldMap.ClearSelection();
                return;
            }
            _messageLabel.Text = $"{placement.Name}已建造，花费 {FormatCoins(FarmGame.BuildingCostCents)} 金币";
            _placement = null;
            _worldMap.SyncFromGame();
        }
        _selectedCell = cell;
        _cropWindow.Hide();
        _inventoryWindow.Hide();
        _marketWindow.Hide();
        RefreshUi();
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
        ShowWindow(_buildWindow);
        RebuildBuildCards();
        RefreshFooter();
    }

    private void RebuildBuildCards()
    {
        if (_buildCards == null)
            return;
        ClearChildren(_buildCards);
        _buildSearch.Visible = _showProcessors;
        if (!_showProcessors)
        {
            Button farm = MakeButton($"♧ 农田\n建造费 {FormatCoins(FarmGame.BuildingCostCents)} 金币", Mid, 194, 75);
            farm.Name = "FarmCard";
            farm.Pressed += () => StartPlacement(new Placement(BuildingKind.Farm, CropKind.Wheat));
            _buildCards.AddChild(farm);
            return;
        }
        int count = 0;
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            if (!crop.BuildingName.Contains(_buildSearch.Text, StringComparison.OrdinalIgnoreCase))
                continue;
            CropKind kind = crop.Kind;
            Button card = MakeButton($"{crop.BuildingName}\n{crop.CropName} → {crop.ProductName} · {FormatCoins(FarmGame.BuildingCostCents)} 金币",
                Mid, 194, 75);
            card.Name = $"ProcessorCard{crop.Kind}";
            card.Pressed += () => StartPlacement(new Placement(BuildingKind.Processor, kind));
            _buildCards.AddChild(card);
            count++;
        }
        if (count == 0)
            _buildCards.AddChild(MakeLabel("没有匹配的加工场地", 14, Ink));
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
        RebuildCropCards();
        ShowWindow(_cropWindow);
    }

    private void RebuildCropCards()
    {
        ClearChildren(_cropCards);
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            CropKind kind = crop.Kind;
            Button option = MakeButton(
                $"{crop.CropName} · 原料售价：{FormatCoins(_game.GetRawPriceCents(kind))} 金币\n" +
                $"原料库存 {_game.GetRawStock(kind)} · {crop.ProductName}库存 {_game.GetProductStock(kind)}",
                Mid, 390, 57);
            option.Name = $"CropCard{kind}";
            option.Pressed += () => SetCrop(kind);
            _cropCards.AddChild(option);
        }
    }

    private void SetCrop(CropKind crop)
    {
        if (_selectedCell is not Vector2I cell)
            return;
        string? error = _game.SetFarmCrop(cell, crop);
        _messageLabel.Text = error ?? $"农田已改种{FarmGame.GetCrop(crop).CropName}";
        if (error == null)
            _cropWindow.Hide();
        _worldMap.SyncFromGame();
        RefreshUi();
    }

    private void RemoveSelected()
    {
        if (_selectedCell is not Vector2I cell)
            return;
        string? error = _game.RemoveBuilding(cell);
        _messageLabel.Text = error ?? "建筑已移除；不退还建造费";
        _worldMap.SyncFromGame();
        RefreshUi();
    }

    private void OpenInventory()
    {
        _buildWindow.Hide();
        _cropWindow.Hide();
        _marketWindow.Hide();
        RebuildInventory();
        ShowWindow(_inventoryWindow);
    }

    private void RebuildInventory()
    {
        ClearChildren(_inventoryRows);
        foreach (CropDefinition crop in FarmGame.Crops)
            _inventoryRows.AddChild(MakeInfoCard(
                $"{crop.CropName}原料  {_game.GetRawStock(crop.Kind)}   →   {crop.ProductName}  {_game.GetProductStock(crop.Kind)}"));
    }

    private void OpenMarket()
    {
        _buildWindow.Hide();
        _cropWindow.Hide();
        _inventoryWindow.Hide();
        RebuildMarket();
        ShowWindow(_marketWindow);
    }

    private void RebuildMarket()
    {
        ClearChildren(_marketRows);
        bool hasProducts = false;
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            CropKind kind = crop.Kind;
            int raw = _game.GetRawStock(kind);
            int product = _game.GetProductStock(kind);
            hasProducts |= product > 0;
            _marketRows.AddChild(MakeInfoCard(
                $"{crop.CropName}原料  {raw} · 售价 {FormatCoins(_game.GetRawPriceCents(kind))} 金币\n" +
                $"{crop.ProductName}加工品  {product} · 售价 {FormatCoins(_game.GetProductPriceCents(kind))} 金币"));
            Button sellRaw = MakeButton($"卖出全部{crop.CropName}原料", Mid, 0, 38);
            sellRaw.Name = $"SellRaw{kind}Button";
            sellRaw.Disabled = raw == 0;
            sellRaw.Pressed += () => SellRaw(kind);
            _marketRows.AddChild(sellRaw);
        }
        _marketSellButton.Disabled = !hasProducts;
    }

    private void SellRaw(CropKind crop)
    {
        SaleResult sale = _game.SellRaw(crop);
        _messageLabel.Text = $"卖出 {sale.Quantity} 份{FarmGame.GetCrop(crop).CropName}原料，获得 {FormatCoins(sale.RevenueCents)} 金币";
        RefreshUi();
    }

    private void SellAll()
    {
        SaleResult sale = _game.SellAll();
        _messageLabel.Text = sale.Quantity == 0
            ? "加工品库存为空"
            : $"卖出 {sale.Quantity} 份加工品，获得 {FormatCoins(sale.RevenueCents)} 金币";
        RefreshUi();
    }

    private void OnTick()
    {
        TickResult result = _game.AdvanceTick();
        if (result.Harvested > 0 || result.Produced > 0)
            _messageLabel.Text = $"收获 {result.Harvested} 份原料，加工产出 {result.Produced} 份";
        _worldMap.SyncFromGame();
        RefreshUi();
    }

    private void RefreshUi()
    {
        _moneyLabel.Text = FormatCoins(_game.MoneyCents);
        RefreshFooter();
        RebuildDetail();
        if (_inventoryWindow.Visible) RebuildInventory();
        if (_marketWindow.Visible) RebuildMarket();
        if (_cropWindow.Visible) RebuildCropCards();
    }

    private void RefreshFooter()
    {
        _buildHint.Text = _placement is Placement placement
            ? $"摆放中：{placement.Name} · 点击地图空位建造 · 费用 {FormatCoins(FarmGame.BuildingCostCents)} 金币"
            : "选择建筑，再点击地图空位摆放";
        _cancelPlacementButton.Visible = _placement != null;
    }

    private void RebuildDetail()
    {
        if (_selectedCell is not Vector2I cell || _placement != null)
        {
            _detailWindow.Hide();
            return;
        }
        ClearChildren(_detailContent);
        PlotSnapshot plot = _game.GetPlot(cell);
        _detailContent.AddChild(MakeLabel($"地块 ({cell.X}, {cell.Y})", 13, Ink));
        if (plot.Building == BuildingKind.None)
        {
            _detailContent.AddChild(MakeInfoCard("空地\n点击底部“建造”选择建筑，再点击地图空位摆放。"));
        }
        else if (plot.Building == BuildingKind.Farm)
        {
            CropDefinition crop = FarmGame.GetCrop(plot.CropKind);
            string status = plot.Crop switch
            {
                CropStage.Seeded => "等待浇水",
                CropStage.Growing => "生长中",
                _ => "等待工人照料",
            };
            _detailContent.AddChild(MakeInfoCard($"{crop.CropName}农田 · {status}"));
            _detailContent.AddChild(MakeInfoCard($"生长周期：浇水后 {crop.GrowthTicks} 秒成熟"));
            _detailContent.AddChild(MakeInfoCard(
                $"{crop.CropName}原材料售价：{FormatCoins(_game.GetRawPriceCents(crop.Kind))} 金币"));
            _detailContent.AddChild(MakeInfoCard(
                $"{crop.CropName}原料库存：{_game.GetRawStock(crop.Kind)}\n" +
                $"{crop.ProductName}加工品库存：{_game.GetProductStock(crop.Kind)}"));
            Button change = MakeButton("更换作物 · 查看价格与库存", Mid, 0, 43);
            change.Name = "ChangeCropButton";
            change.Pressed += OpenCrop;
            _detailContent.AddChild(change);
            _detailContent.AddChild(MakeLabel("改种会清除当前未收获作物。", 12, Ink));
            Button remove = MakeButton("移除农田", new Color(0.66f, 0.36f, 0.31f), 0, 43);
            remove.Name = "RemoveButton";
            remove.Pressed += RemoveSelected;
            _detailContent.AddChild(remove);
        }
        else
        {
            CropDefinition crop = FarmGame.GetCrop(plot.CropKind);
            string status = plot.RemainingTicks > 0 ? "加工中" : $"等待{crop.CropName}";
            _detailContent.AddChild(MakeInfoCard($"{crop.BuildingName} · {status}"));
            _detailContent.AddChild(MakeInfoCard($"{crop.CropName} → {crop.ProductName}"));
            Button remove = MakeButton("移除加工场地", new Color(0.66f, 0.36f, 0.31f), 0, 43);
            remove.Name = "RemoveButton";
            remove.Pressed += RemoveSelected;
            _detailContent.AddChild(remove);
        }
        _detailWindow.Show();
        ClampWindow(_detailWindow);
    }

    private static PanelContainer MakeInfoCard(string text)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Style(new Color(0.90f, 0.94f, 0.86f), 9));
        var margin = WrapMargin(card, 11, 9);
        margin.AddChild(MakeLabel(text, 14, Ink));
        return card;
    }

    private static PanelContainer MakeStat(string caption, out Label value, string initial = "")
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(caption == "工人" ? 140 : 105, 48) };
        panel.AddThemeStyleboxOverride("panel", Style(new Color(0.22f, 0.36f, 0.31f), 10));
        var margin = WrapMargin(panel, 10, 4);
        var box = new VBoxContainer();
        margin.AddChild(box);
        box.AddChild(MakeLabel(caption, 11, Muted));
        value = MakeLabel(initial, 16, Cream);
        box.AddChild(value);
        return panel;
    }

    private static MarginContainer WrapMargin(Control parent, int horizontal, int vertical)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", horizontal);
        margin.AddThemeConstantOverride("margin_right", horizontal);
        margin.AddThemeConstantOverride("margin_top", vertical);
        margin.AddThemeConstantOverride("margin_bottom", vertical);
        parent.AddChild(margin);
        return margin;
    }

    private static Label MakeLabel(string text, int size, Color color)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static Button MakeButton(string text, Color background, float width, float height)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(width, height) };
        button.AddThemeStyleboxOverride("normal", Style(background, 9));
        button.AddThemeStyleboxOverride("hover", Style(background.Lightened(0.12f), 9));
        button.AddThemeStyleboxOverride("pressed", Style(background.Darkened(0.12f), 9));
        button.AddThemeStyleboxOverride("disabled", Style(new Color(0.39f, 0.47f, 0.42f), 9));
        button.AddThemeColorOverride("font_color", Cream);
        button.AddThemeColorOverride("font_hover_color", Cream);
        button.AddThemeColorOverride("font_pressed_color", Cream);
        button.AddThemeColorOverride("font_disabled_color", Muted);
        button.AddThemeFontSizeOverride("font_size", 14);
        return button;
    }

    private static StyleBoxFlat Style(Color color, int radius)
    {
        return new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
        };
    }

    private static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string FormatCoins(int cents) =>
        (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
