using System;
using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class FarmDetailsPanel : VBoxContainer
{
    private readonly Label _status;
    private readonly Label _growth;
    private readonly Label _price;
    private readonly Label _stock;

    public event Action? ChangeCropRequested;
    public event Action? RemoveRequested;

    public FarmDetailsPanel()
    {
        AddThemeConstantOverride("separation", 9);
        AddChild(MakeInfoCard("", out _status));
        AddChild(MakeInfoCard("", out _growth));
        AddChild(MakeInfoCard("", out _price));
        AddChild(MakeInfoCard("", out _stock));
        Button change = MakeButton("更换作物 · 查看价格与库存", Mid, 0, 43);
        change.Name = "ChangeCropButton";
        change.Pressed += () => ChangeCropRequested?.Invoke();
        AddChild(change);
        AddChild(MakeLabel("改种会清除当前未收获作物。", 12, Ink));
        Button remove = MakeButton("移除农田", new Color(0.66f, 0.36f, 0.31f), 0, 43);
        remove.Name = "RemoveButton";
        remove.Pressed += () => RemoveRequested?.Invoke();
        AddChild(remove);
    }

    public void Refresh(FarmDetailsSnapshot details)
    {
        string status = details.Status switch
        {
            FarmStatus.WaitingForWater => "等待浇水",
            FarmStatus.Growing => "生长中",
            _ => "等待工人照料",
        };
        _status.Text = $"{details.Crop.CropName}农田 · {status}";
        _growth.Text = $"生长周期：浇水后 {details.Crop.GrowthTicks} 秒成熟";
        _price.Text = $"{details.Crop.CropName}原材料售价：{FormatCoins(details.RawPriceCents)} 金币";
        _stock.Text = $"{details.Crop.CropName}原料库存：{details.RawStock}";
    }
}
