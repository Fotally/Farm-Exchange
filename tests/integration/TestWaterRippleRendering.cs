using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Time;
using FarmExchange.World;

// 只从真实 SubViewport 读回帧；源 mask 用作独立像素分类，不生成或替代截图。
internal static class TestWaterRippleRendering
{
    internal static async Task RunChecksAsync(Node parent, FarmGame game, SimulationDriver driver, string directory)
    {
        if (DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("水纹像素验收必须使用真实图形后端，headless 只验证时间输入");
        bool previousPaused = game.IsPaused;
        double previousRate = driver.Rate;
        var viewport = new SubViewport
        {
            Size = new Vector2I(1920, 1080),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        parent.AddChild(viewport);
        var map = new WorldMap
        {
            Position = new Vector2(960, 540) - MapCoordinates.GridPositionToLocal(new Vector2(192, 209.5f)),
        };
        viewport.AddChild(map);
        map.SetGame(game, driver);
        map.SetProcess(false);
        map.GetNode<Node2D>("DepthSorted").Hide();
        map.GetNode<Node2D>("Soil").Hide();
        map.GetNode<Node2D>("Weather").Hide();
        ShaderMaterial material = map.FindChildren("*", "MeshInstance2D", true, false)
            .OfType<MeshInstance2D>().Select(surface => surface.Material).OfType<ShaderMaterial>().First();
        var maskTexture = (Texture2D)material.GetShaderParameter("water_mask").AsGodotObject();
        var rippleTexture = (Texture2D)material.GetShaderParameter("ripples").AsGodotObject();
        var evidence = new List<object>();
        try
        {
            List<Probe> probes = CreateProbes(game, map);
            if (probes.Count(probe => probe.Water) < 1000 || probes.Count(probe => probe.ShoreDry) < 100)
                throw new InvalidOperationException("水纹像素夹具缺少水面或岸格干地采样");
            game.SetPaused(true);
            map._Process(0);
            using Image first = await Capture(parent, viewport);
            first.SavePng(Path.Combine(directory, "ripples-isolated-before.png"));
            foreach (double rate in new[] { .5, 1, 2, 16 })
            {
                string rateName = rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
                driver.SetDevelopmentRate(rate);
                double beforeSeconds = material.GetShaderParameter("elapsed_seconds").AsDouble();
                using Image before = await Capture(parent, viewport);
                before.SavePng(Path.Combine(directory, $"ripples-isolated-{rateName}-before.png"));
                game.SetPaused(false);
                map._Process(.75 / rate);
                using Image after = await Capture(parent, viewport);
                double afterSeconds = material.GetShaderParameter("elapsed_seconds").AsDouble();
                int[] changedWaterByVariant = new int[3];
                int changedDry = 0;
                int changedShoreDry = 0;
                foreach (Probe probe in probes)
                {
                    if (before.GetPixelv(probe.Screen) == after.GetPixelv(probe.Screen)) continue;
                    if (probe.Water) changedWaterByVariant[probe.Variant]++;
                    else if (probe.ShoreDry) changedShoreDry++;
                    else changedDry++;
                }
                game.SetPaused(true);
                map._Process(2);
                using Image paused = await Capture(parent, viewport);
                bool pauseStable = after.GetData().SequenceEqual(paused.GetData());
                after.SavePng(Path.Combine(directory, $"ripples-isolated-{rateName}.png"));
                paused.SavePng(Path.Combine(directory, $"ripples-isolated-{rateName}-paused.png"));
                bool passed = changedWaterByVariant.All(count => count >= 10) && changedDry == 0 && changedShoreDry == 0 && pauseStable;
                SaveProbeColors(directory, rateName, probes, before, after);
                evidence.Add(new
                {
                    Rate = rate,
                    RealDeltaSeconds = .75 / rate,
                    SimulatedDeltaSeconds = .75,
                    ShaderSecondsBefore = beforeSeconds,
                    ShaderSecondsAfter = afterSeconds,
                    ChangedWaterByVariant = changedWaterByVariant,
                    ChangedDry = changedDry,
                    ChangedShoreDry = changedShoreDry,
                    PausePixelsStable = pauseStable,
                    Passed = passed
                });
                File.WriteAllText(Path.Combine(directory, "ripples-pixels.json"), JsonSerializer.Serialize(new
                {
                    Backend = DisplayServer.GetName(),
                    Renderer = RenderingServer.GetCurrentRenderingMethod(),
                    Viewport = "1920x1080 isolated ground",
                    Shader = material.Shader.ResourcePath,
                    ShaderSha256 = Godot.FileAccess.GetSha256(material.Shader.ResourcePath),
                    WaterMask = maskTexture.ResourcePath,
                    Ripples = rippleTexture.ResourcePath,
                    WaterProbes = probes.Count(probe => probe.Water),
                    WaterProbesByVariant = Enumerable.Range(0, 3).Select(variant => probes.Count(probe => probe.Water && probe.Variant == variant)).ToArray(),
                    ShoreDryProbes = probes.Count(probe => probe.ShoreDry),
                    FullLandProbes = probes.Count(probe => !probe.Water && !probe.ShoreDry),
                    Samples = evidence,
                    AllRequestedRatesCompleted = evidence.Count == 4,
                    Passed = passed && evidence.Count == 4,
                }, new JsonSerializerOptions { WriteIndented = true }));
                if (!passed)
                    throw new InvalidOperationException($"水纹真实输出错误：{rate}× 水变化={string.Join("/", changedWaterByVariant)}，陆地变化={changedDry}，岸格干地变化={changedShoreDry}，暂停稳定={pauseStable}；实际前后帧与逐变体探针已保存");
            }
        }
        finally
        {
            game.SetPaused(previousPaused);
            driver.SetDevelopmentRate(previousRate);
            viewport.QueueFree();
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static List<Probe> CreateProbes(FarmGame game, WorldMap map)
    {
        using Image mask = GD.Load<Texture2D>("res://assets/gameplay/terrain/water_81.png").GetImage();
        var probes = new List<Probe>();
        for (int y = 201; y <= 218; y++)
            for (int x = 182; x <= 203; x++)
            {
                Vector2I cell = new(x, y);
                int code = game.Terrain.GetCell(cell).TernaryCode;
                Vector2 center = map.GetGlobalTransformWithCanvas() * MapCoordinates.CellToLocalCenter(cell);
                int variant = (x * 73 + y * 17) % 3;
                for (int dy = -12; dy <= 12; dy++)
                    for (int dx = -26; dx <= 26; dx++)
                    {
                        // 远离共边重叠与mask采样边界；岸格内部干／水区域仍分别验收。
                        if (Math.Abs(dx) / 32.0 + Math.Abs(dy) / 16.0 > .8) continue;
                        int u = (int)Math.Floor(32 + (dx + .5) * 64 / 65.5);
                        int v = (int)Math.Floor(16 + (dy + .5) * 32 / 33.5);
                        int atlasX = code % 9 * 68 + 2 + u;
                        int atlasY = code / 9 * 36 + 2 + v;
                        bool water = mask.GetPixel(atlasX, atlasY).A > .5;
                        bool interior = true;
                        for (int oy = -2; oy <= 2 && interior; oy++)
                            for (int ox = -2; ox <= 2; ox++)
                                if ((mask.GetPixel(atlasX + ox, atlasY + oy).A > .5) != water) { interior = false; break; }
                        if (!interior) continue;
                        Vector2 source = new(32 + (float)((dx + .5) * 64 / 65.5), 16 + (float)((dy + .5) * 32 / 33.5));
                        Vector2 atlasUv = (new Vector2(code % 9 * 68 + 2, (code / 9 + variant * 9) * 36 + 2) + source) / new Vector2(612, 972);
                        Vector2 maskUv = new(atlasUv.X, (float)(atlasUv.Y * 3 - Math.Floor(atlasUv.Y * 3)));
                        probes.Add(new Probe(new Vector2I((int)center.X + dx, (int)center.Y + dy), water,
                            !water && !game.Terrain.IsFullLand(cell), variant, cell, code,
                            MapCoordinates.CellToLocalCenter(cell) + new Vector2(dx + .5f, dy + .5f),
                            atlasUv, maskUv, new Vector2I(atlasX, atlasY), mask.GetPixel(atlasX, atlasY).A));
                    }
            }
        return probes;
    }

    private static async Task<Image> Capture(Node parent, SubViewport viewport)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return viewport.GetTexture().GetImage();
    }

    private static void SaveProbeColors(string directory, string rate, IReadOnlyList<Probe> probes, Image before, Image after)
    {
        static string Category(Probe probe) => probe.Water ? "Water" : probe.ShoreDry ? "ShoreDry" : "FullLand";
        IEnumerable<Probe> selected = probes.GroupBy(probe => (probe.Variant, Category: Category(probe)))
            .SelectMany(group => group.Take(32))
            .Concat(probes.Where(probe => before.GetPixelv(probe.Screen) != after.GetPixelv(probe.Screen))).Distinct();
        File.WriteAllText(Path.Combine(directory, $"ripples-pixels-{rate}-probes.json"), JsonSerializer.Serialize(new
        {
            SampleRule = "每变体／分类前32个稳定mask探针及全部实际变化探针；RGBA来自GPU帧",
            Population = probes.GroupBy(probe => (probe.Variant, Category: Category(probe)))
                .Select(group => new { Variant = group.Key.Variant, Category = group.Key.Category, Count = group.Count() }),
            Colors = selected.Select(probe => new
            {
                Cell = new[] { probe.Cell.X, probe.Cell.Y },
                probe.Variant,
                probe.Code,
                Category = Category(probe),
                Screen = new[] { probe.Screen.X, probe.Screen.Y },
                ExpectedMapPosition = new[] { probe.MapPosition.X, probe.MapPosition.Y },
                AtlasUv = new[] { probe.AtlasUv.X, probe.AtlasUv.Y },
                MaskUv = new[] { probe.MaskUv.X, probe.MaskUv.Y },
                MaskPixel = new[] { probe.MaskPixel.X, probe.MaskPixel.Y },
                probe.MaskAlpha,
                BeforeRgba = before.GetPixelv(probe.Screen).ToHtml(true),
                AfterRgba = after.GetPixelv(probe.Screen).ToHtml(true),
                Changed = before.GetPixelv(probe.Screen) != after.GetPixelv(probe.Screen),
            }),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private readonly record struct Probe(Vector2I Screen, bool Water, bool ShoreDry, int Variant, Vector2I Cell, int Code,
        Vector2 MapPosition, Vector2 AtlasUv, Vector2 MaskUv, Vector2I MaskPixel, float MaskAlpha);
}
