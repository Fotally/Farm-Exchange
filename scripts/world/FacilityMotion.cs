using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Gameplay;

namespace FarmExchange.World;

// WorldMap 的内部表现：只为可见实例创建，时间、真实状态和成功结果由地图统一输入。
internal sealed partial class FacilityMotion : Node2D
{
    private static readonly Vector2 BuildingPivot = new(128, 176);
    private static readonly Vector2[,] Roots =
    {
        { new(5, 12), new(10, 27), new(12, 41) },
        { new(6, 15), new(13, 32), new(17, 52) },
        { new(6, 9), new(9, 19), new(14, 29) },
    };
    private static readonly float[,] Amplitudes = { { .35f, .65f, .9f }, { .4f, .9f, 1.3f }, { .25f, .5f, .65f } };
    private static readonly string[] CropNames = { "wheat", "sugarcane", "radish" };
    private readonly Node2D _art = new() { Name = "Art" };
    private readonly ProductionBurst _burst = new() { Name = "Output" };
    private readonly SteamPuffs _steam = new() { Name = "Steam", Position = new Vector2(30, -130) };
    private (BuildingKind, CropKind, CropStage, int)? _appearance;
    private Sprite2D? _sails;
    private ShaderMaterial? _wind;
    private ProductionResult? _lastResult;
    private double _phase;
    private double _windTime;
    private bool _sugarworks;

    internal FacilityMotion()
    {
        Name = "FacilityMotion";
        TextureFilter = TextureFilterEnum.Nearest;
        AddChild(_art);
        AddChild(_steam);
        AddChild(_burst);
    }

    internal static bool ReplacesSurface(PlotSnapshot plot) =>
        plot.Building == BuildingKind.Processor && plot.CropKind is CropKind.Wheat or CropKind.Sugarcane ||
        plot.Building == BuildingKind.Farm && plot.Crop == CropStage.Growing &&
            plot.CropKind is CropKind.Wheat or CropKind.Sugarcane or CropKind.Radish;

    internal void Configure(PlotSnapshot plot, int growthStage)
    {
        var appearance = (plot.Building, plot.CropKind, plot.Crop, growthStage);
        if (_appearance == appearance) return;
        _appearance = appearance;
        foreach (Node child in _art.GetChildren()) child.Free();
        _sails = null;
        _wind = null;
        _sugarworks = plot.Building == BuildingKind.Processor && plot.CropKind == CropKind.Sugarcane;
        _steam.Clear();
        YSortEnabled = _art.YSortEnabled = plot.Building == BuildingKind.Farm;
        if (!ReplacesSurface(plot)) return;
        if (plot.Building == BuildingKind.Processor)
        {
            bool mill = plot.CropKind == CropKind.Wheat;
            _art.AddChild(new Sprite2D
            {
                Name = "Body",
                Centered = false,
                Offset = -BuildingPivot,
                Texture = LoadBuilding(mill ? "windmill_body" : "sugarworkshop_body"),
            });
            if (mill)
            {
                _sails = new Sprite2D
                {
                    Name = "Sails",
                    Texture = LoadBuilding("windmill_sails_256"),
                    Hframes = 16,
                    Vframes = 16,
                    Position = new Vector2(-3, -72),
                };
                _art.AddChild(_sails);
                _art.AddChild(new Sprite2D
                {
                    Name = "Hub",
                    Texture = LoadBuilding("windmill_hub"),
                    Position = _sails.Position,
                });
                UpdateSails();
            }
            return;
        }
        int crop = plot.CropKind == CropKind.Wheat ? 0 : plot.CropKind == CropKind.Sugarcane ? 1 : 2;
        Vector2 sourceRoot = Roots[crop, growthStage];
        _wind = new ShaderMaterial { Shader = GD.Load<Shader>("res://scripts/world/rooted_wind.gdshader") };
        _wind.SetShaderParameter("root_y_px", sourceRoot.Y);
        _wind.SetShaderParameter("fixed_base_px", crop == 2 ? new[] { 1, 7, 12 }[growthStage] : 1);
        _wind.SetShaderParameter("amplitude_px", Amplitudes[crop, growthStage]);
        _wind.SetShaderParameter("phase", (Position.X + Position.Y) * .013f);
        _wind.SetShaderParameter("visual_time", _windTime);
        Texture2D plant = GD.Load<Texture2D>($"res://assets/gameplay/crops/motion/{CropNames[crop]}_growing_{growthStage + 1:D2}.png");
        var positions = new List<Vector2>(15);
        for (int u = -1; u <= 1; u++)
            for (int halfV = -2; halfV <= 2; halfV++)
            {
                float v = halfV * .5f;
                positions.Add(new Vector2(32 * (u - v), 16 * (u + v)));
            }
        positions.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        foreach (Vector2 root in positions)
            _art.AddChild(new Sprite2D
            {
                Name = "Plant",
                Position = root,
                Centered = false,
                Offset = -sourceRoot - new Vector2(4, 0),
                Texture = plant,
                Material = _wind,
            });
    }

    internal void Advance(double seconds, bool processing)
    {
        if (seconds <= 0) return;
        if (_sails != null && processing)
        {
            _phase = (_phase + seconds * .53) % Math.Tau;
            UpdateSails();
        }
        if (_wind != null)
        {
            // 两个波形的共同周期，避免长时间运行降低 float 精度。
            _windTime = (_windTime + seconds) % (Math.Tau / .35);
            _wind.SetShaderParameter("visual_time", _windTime);
        }
        _steam.Advance(seconds, _sugarworks && processing);
        _burst.Advance(seconds);
    }

    internal void ShowResult(ProductionResult result)
    {
        if (result.Kind is not (ProductionResultKind.Harvest or ProductionResultKind.Product) ||
            result == _lastResult) return;
        _lastResult = result;
        Vector2 origin = result.Kind == ProductionResultKind.Harvest ? new Vector2(0, -10) :
            result.CropKind == CropKind.Wheat ? new Vector2(-11, -8) :
            result.CropKind == CropKind.Sugarcane ? new Vector2(-16, -7) : new Vector2(0, -8);
        _burst.Show(result.Quantity, origin, result.Kind == ProductionResultKind.Harvest);
    }

    internal void ClearResult() => _burst.Clear();

    private void UpdateSails() => _sails!.Frame = (int)(_phase / Math.Tau * 256) % 256;
    private static Texture2D LoadBuilding(string name) =>
        GD.Load<Texture2D>($"res://assets/gameplay/buildings/motion/{name}.png");

    private sealed partial class SteamPuffs : Node2D
    {
        private readonly List<float> _ages = new(4);
        private double _pulse;

        internal void Clear() { _ages.Clear(); _pulse = 0; QueueRedraw(); }

        internal void Advance(double seconds, bool emitting)
        {
            if (!emitting && _ages.Count == 0 && _pulse == 0) return;
            for (int i = _ages.Count - 1; i >= 0; i--)
            {
                _ages[i] += (float)seconds;
                if (_ages[i] >= 1.5f) _ages.RemoveAt(i);
            }
            if (emitting)
            {
                _pulse += seconds;
                if (_pulse >= .38)
                {
                    _pulse %= .38;
                    if (_ages.Count < 4) _ages.Add((float)_pulse);
                }
            }
            else _pulse = 0;
            QueueRedraw();
        }

        public override void _Draw()
        {
            foreach (float age in _ages)
            {
                int radius = 2 + (int)(age * 3);
                Vector2 p = new Vector2(MathF.Sin(age * 3) * 3, -age * 10).Round();
                Color tint = new(.88f, .91f, .83f, (1 - age / 1.5f) * .58f);
                DrawRect(new Rect2(p - new Vector2(radius, radius - 1), new Vector2(radius * 2, radius * 2 - 2)), tint);
                DrawRect(new Rect2(p - new Vector2(radius - 1, radius), new Vector2(radius * 2 - 2, radius * 2)), tint);
            }
        }
    }

    private sealed partial class ProductionBurst : Node2D
    {
        private const float Lifetime = .85f;
        private float _age = Lifetime;
        private string _text = "";
        private Vector2 _origin;
        private bool _harvest;

        internal void Show(int quantity, Vector2 origin, bool harvest)
        {
            _age = 0;
            _text = $"+{quantity}";
            _origin = origin;
            _harvest = harvest;
            QueueRedraw();
        }

        internal void Clear() { _age = Lifetime; QueueRedraw(); }
        internal void Advance(double seconds)
        {
            if (_age >= Lifetime) return;
            _age += (float)seconds;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_age >= Lifetime) return;
            float fade = 1 - _age / Lifetime;
            Color tint = new(.94f, .86f, .57f, fade);
            if (_harvest)
                for (int i = 0; i < 8; i++)
                {
                    Vector2 velocity = new(-13 + i * 26f / 7, -24 + (i % 3) * 5);
                    Vector2 p = (_origin + velocity * _age + new Vector2(0, 35 * _age * _age)).Round();
                    DrawRect(new Rect2(p, new Vector2(2, 3)), tint);
                    DrawRect(new Rect2(p + new Vector2(1, -1), new Vector2(2, 1)), tint);
                }
            Font font = ThemeDB.FallbackFont;
            Vector2 size = font.GetStringSize(_text, fontSize: 13);
            Vector2 textPosition = (_origin + new Vector2(-size.X / 2, -_age * 17 - 12)).Round();
            DrawRect(new Rect2(textPosition + new Vector2(-3, -14), new Vector2(size.X + 6, 19)),
                new Color(.22f, .28f, .17f, fade * .85f));
            DrawString(font, textPosition, _text, fontSize: 13, modulate: tint);
        }
    }
}
