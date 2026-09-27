using System;
using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class ProcessorDetailsPanel : VBoxContainer
{
    private readonly Label _status;
    private readonly Label _relation;
    private readonly Label _processing;
    private readonly Label _price;
    private readonly Label _stock;

    public event Action? RemoveRequested;

    public ProcessorDetailsPanel()
    {
        AddThemeConstantOverride("separation", 9);
        AddChild(MakeInfoCard("", out _status));
        AddChild(MakeInfoCard("", out _relation));
        AddChild(MakeInfoCard("", out _processing));
        AddChild(MakeInfoCard("", out _price));
        AddChild(MakeInfoCard("", out _stock));
        Button remove = MakeButton("移除加工场地", new Color(0.66f, 0.36f, 0.31f), 0, 43);
        remove.Name = "RemoveButton";
        remove.Pressed += () => RemoveRequested?.Invoke();
        AddChild(remove);
    }

    public void Refresh(ProcessorDetailsSnapshot details)
    {
        string status = details.Status == ProcessorStatus.Processing
            ? "加工中" : $"等待{details.Crop.CropName}";
        _status.Text = $"{details.Crop.BuildingName} · {status}";
        _relation.Text = $"{details.Crop.CropName} → {details.Crop.ProductName}";
        _processing.Text = $"加工周期：投入原料后 {details.Crop.ProcessingTicks} 秒完成";
        _price.Text = $"{details.Crop.ProductName}加工品售价：{FormatCoins(details.ProductPriceCents)} 金币";
        _stock.Text = $"{details.Crop.ProductName}加工品库存：{details.ProductStock}";
    }
}
