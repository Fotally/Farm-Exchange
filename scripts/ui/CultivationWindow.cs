using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Gameplay;
using FarmExchange.Time;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

/**
 * <summary>编辑具名共享年度表并向经营入口提交保存、批量应用意图。</summary>
 */
public partial class CultivationWindow : DraggableWindow
{
    private readonly ItemList _plans;
    private readonly LineEdit _name;
    private readonly OptionButton _mode;
    private readonly CultivationTimeline _timeline;
    private readonly Label _feedback;
    private readonly Label _entryInfo;
    private readonly Label _impact;
    private readonly VBoxContainer _farms;
    private readonly Dictionary<Vector2I, CheckBox> _farmChecks = new();
    private readonly List<CultivationEntry> _draft = new();
    private IReadOnlyList<CultivationPlanSnapshot> _snapshots = Array.Empty<CultivationPlanSnapshot>();
    private FarmGame? _game;
    private int? _planId;
    private int? _selectedEntry;

    /**
     * <summary>请求创建或编辑一份完整共享表；空编号表示新建。</summary>
     */
    public event Action<int?, CultivationPlanRequest>? SaveRequested;
    /**
     * <summary>请求把已保存表应用到所勾选农田的锚点列表。</summary>
     */
    public event Action<int, IReadOnlyList<Vector2I>>? ApplyRequested;

    public CultivationWindow() : base("CultivationWindow", "年度耕作表", new Vector2(130, 82),
        new Vector2(1000, 520), avoidBottomBar: true)
    {
        Body.AddThemeConstantOverride("separation", 6);
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 14);
        Body.AddChild(columns);
        var sidebar = new VBoxContainer { CustomMinimumSize = new Vector2(170, 0) };
        columns.AddChild(sidebar);
        sidebar.AddChild(Text("共享年度表", 15));
        _plans = new ItemList { Name = "CultivationPlanList", CustomMinimumSize = new Vector2(170, 120) };
        _plans.ItemSelected += index => LoadPlan(_snapshots[(int)index]);
        sidebar.AddChild(_plans);
        Button create = MakeButton("新建年度表", Mid, 0, 32);
        create.Name = "NewCultivationPlanButton";
        create.Pressed += NewPlan;
        sidebar.AddChild(create);
        sidebar.AddChild(Text("勾选要应用的农田", 13));
        var farmScroll = new ScrollContainer
        {
            Name = "CultivationFarmScroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        sidebar.AddChild(farmScroll);
        _farms = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        farmScroll.AddChild(_farms);
        Button apply = MakeButton("应用已保存表到勾选田", Mid, 0, 36);
        apply.Name = "ApplyCultivationPlanButton";
        apply.Pressed += Apply;
        sidebar.AddChild(apply);

        var editor = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddChild(editor);
        var config = new HBoxContainer();
        editor.AddChild(config);
        _name = new LineEdit { Name = "CultivationPlanName", PlaceholderText = "年度表名称", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        config.AddChild(_name);
        _mode = new OptionButton { Name = "CultivationPlanMode", CustomMinimumSize = new Vector2(150, 0) };
        _mode.AddItem("立即改种");
        _mode.AddItem("预备下一轮");
        config.AddChild(_mode);
        _name.TextChanged += _ => ValidateDraft();
        _mode.ItemSelected += _ => ValidateDraft();
        _timeline = new CultivationTimeline();
        var crops = new HBoxContainer();
        editor.AddChild(crops);
        foreach (CropDefinition crop in FarmGame.Crops) crops.AddChild(new CultivationCropCard(crop.Kind, _timeline));
        _timeline.EntrySelected += SelectEntry;
        _timeline.EntryDropped += DropEntry;
        editor.AddChild(_timeline);
        _entryInfo = Text("从上方拖入作物；空白为休耕。金线为今天。");
        _entryInfo.Name = "CultivationEntryInfo";
        editor.AddChild(_entryInfo);
        var actions = new HBoxContainer();
        editor.AddChild(actions);
        Button remove = MakeButton("移除选中整条", Mid, 140, 32);
        remove.Name = "RemoveCultivationEntryButton";
        remove.Pressed += RemoveEntry;
        actions.AddChild(remove);
        Button save = MakeButton("保存共享表", Gold, 140, 32);
        save.Name = "SaveCultivationPlanButton";
        save.Pressed += Save;
        actions.AddChild(save);
        _impact = Text("新表尚未关联农田");
        _impact.Name = "CultivationImpact";
        actions.AddChild(_impact);
        _feedback = Text("");
        _feedback.Name = "CultivationFeedback";
        Body.AddChild(_feedback);
        Body.AddChild(Text("图上日期是安排，实际播种、浇水与进度以农田详情为准；应用和保存保留当前轮。"));
    }

    /**
     * <summary>更新真实引用与日期，保留本地草稿、焦点和勾选。</summary>
     * <param name="game">只读查询的经营入口。</param>
     */
    public void Refresh(FarmGame game)
    {
        _game = game;
        _snapshots = game.GetCultivationPlans();
        int selection = -1;
        if (_plans.ItemCount != _snapshots.Count)
        {
            _plans.Clear();
            foreach (CultivationPlanSnapshot plan in _snapshots) _plans.AddItem(plan.Name);
        }
        for (int index = 0; index < _snapshots.Count; index++)
        {
            CultivationPlanSnapshot plan = _snapshots[index];
            _plans.SetItemText(index, $"{plan.Name} · {plan.ReferencingFarms}田");
            _plans.SetItemTooltip(index, $"{plan.Name} · 引用 {plan.ReferencingFarms} 块农田");
            if (plan.Id == _planId) selection = index;
        }
        if (selection >= 0) _plans.Select(selection);
        _impact.Text = _planId == null ? "新表尚未关联农田" :
            $"保存影响 {_snapshots.First(p => p.Id == _planId).ReferencingFarms} 块引用田";
        var cells = game.GetBuildingSpaces().Where(s => s.Building == BuildingKind.Farm)
            .Select(s => s.AnchorCell).ToArray();
        var currentCells = cells.ToHashSet();
        foreach (Vector2I oldCell in _farmChecks.Keys.Where(c => !currentCells.Contains(c)).ToArray())
        {
            CheckBox check = _farmChecks[oldCell];
            _farms.RemoveChild(check);
            check.QueueFree();
            _farmChecks.Remove(oldCell);
        }
        foreach (Vector2I cell in cells)
        {
            if (!_farmChecks.TryGetValue(cell, out CheckBox? check))
            {
                check = new CheckBox
                {
                    Name = $"CultivationFarm{cell.X}_{cell.Y}",
                    ClipText = true,
                    CustomMinimumSize = new Vector2(170, 0),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                check.AddThemeFontSizeOverride("font_size", 12);
                check.AddThemeColorOverride("font_color", Ink);
                _farmChecks.Add(cell, check);
                _farms.AddChild(check);
            }
            FarmCultivationSnapshot state = game.GetFarmCultivation(cell);
            check.Text = $"({cell.X},{cell.Y}) · {state.PlanName ?? "手动"}";
            check.TooltipText = $"当前作物：{FarmGame.GetCrop(game.GetPlot(cell).CropKind).CropName}\n" +
                $"引用：{state.PlanName ?? "无共享表"}";
        }
        RefreshTimeline();
    }

    /**
     * <summary>显示保存结果，成功时记住经营入口分配的共享表编号。</summary>
     * <param name="result">创建或编辑的真实结果。</param>
     */
    public void ShowCommandResult(CultivationCommandResult result)
    {
        if (result.Success) _planId = result.Id;
        _feedback.Text = result.Error ?? "共享表已保存；所有引用田的后续安排已更新";
    }

    /**
     * <summary>显示批量应用的真实结果。</summary>
     * <param name="error">空值表示成功。</param>
     */
    public void ShowApplyResult(string? error) => _feedback.Text = error ?? "已应用共享表，当前轮保留";

    private CultivationPlanRequest Request() => new(_name.Text, (CultivationMode)_mode.Selected, _draft.ToArray());

    private void NewPlan()
    {
        _planId = null;
        _selectedEntry = null;
        _draft.Clear();
        _name.Text = "新年度表";
        _mode.Select(0);
        _plans.DeselectAll();
        _impact.Text = "新表尚未关联农田";
        ValidateDraft();
        _entryInfo.Text = "从上方拖入作物；空白为休耕。金线为今天。";
    }

    private void LoadPlan(CultivationPlanSnapshot plan)
    {
        _planId = plan.Id;
        _draft.Clear();
        _draft.AddRange(plan.Entries);
        _selectedEntry = null;
        _name.Text = plan.Name;
        _mode.Select((int)plan.Mode);
        _impact.Text = $"保存影响 {plan.ReferencingFarms} 块引用田";
        _entryInfo.Text = "拖动任何片段都会移动整轮；保存才改变共享表。";
        ValidateDraft();
    }

    private void SelectEntry(int id)
    {
        _selectedEntry = id;
        CultivationEntry entry = _draft.First(e => e.Id == id);
        int end = entry.StartDay + entry.LengthDays;
        string crossing = entry.StartDay / 84 != (end - 1) / 84 ? " · 跨季同一轮" : "";
        if (end > 336) crossing += " · 冬春跨年";
        _entryInfo.Text = $"{FarmGame.GetCrop(entry.Crop).CropName} · 完整 {entry.LengthDays} 天\n" +
            $"{Date(entry.StartDay)} → {(end >= 336 ? "次年 " : "")}{Date(end % 336)}{crossing}";
        RefreshTimeline();
    }

    private void DropEntry(int id, CropKind crop, int day)
    {
        if (id == 0)
        {
            id = _draft.Count == 0 ? 1 : _draft.Max(e => e.Id) + 1;
            _draft.Add(new CultivationEntry(id, crop, day));
        }
        else
        {
            int index = _draft.FindIndex(e => e.Id == id);
            _draft[index] = new CultivationEntry(id, crop, day);
        }
        SelectEntry(id);
        ValidateDraft();
    }

    private void RemoveEntry()
    {
        if (_selectedEntry == null) { _feedback.Text = "先点击要移除的作物条"; return; }
        _draft.RemoveAll(e => e.Id == _selectedEntry);
        _selectedEntry = null;
        _entryInfo.Text = "作物条已从草稿移除；保存后生效。";
        ValidateDraft();
    }

    private void Save()
    {
        if (_game == null) return;
        CultivationValidation validation = _game.CheckCultivationPlan(Request());
        if (!validation.Success) { _feedback.Text = validation.Error; return; }
        SaveRequested?.Invoke(_planId, Request());
    }

    private void Apply()
    {
        if (_planId is not int id) { _feedback.Text = "先保存或选择一份共享表"; return; }
        Vector2I[] cells = _farmChecks.Where(pair => pair.Value.ButtonPressed).Select(pair => pair.Key).ToArray();
        if (cells.Length == 0) { _feedback.Text = "先勾选至少一块农田"; return; }
        ApplyRequested?.Invoke(id, cells);
    }

    private void ValidateDraft()
    {
        if (_game == null) return;
        CultivationValidation validation = _game.CheckCultivationPlan(Request());
        _feedback.Text = validation.Error ?? (validation.RiskEntryIds.Count > 0
            ? "红框作物条越过禁生边界：存在枯萎清除风险，仍可保存。"
            : "排程可保存：同种可连续，不同作物至少留一天空白。");
        RefreshTimeline(validation);
    }

    private void RefreshTimeline(CultivationValidation? validation = null)
    {
        if (_game == null) return;
        validation ??= _game.CheckCultivationPlan(Request());
        _timeline.Refresh(_draft, validation.RiskEntryIds, _selectedEntry, _game.Calendar.ElapsedDays);
    }

    private static string Date(int day)
    {
        GameDate date = GameCalendar.GetDate((uint)day);
        return $"{date.Month}月{date.Day}日";
    }

    private static Label Text(string text, int size = 12)
    {
        Label label = MakeLabel(text, size, Ink);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        return label;
    }
}
