using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;
#if DEBUG
using FarmExchange.Development;
#endif

public partial class TestScenarioConfigurationLibrary : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks()
    {
#if DEBUG
        string root = Path.Combine(Path.GetTempPath(), "farm-config-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { return CheckPersistence(root) && CheckFailures(root) && CheckPartialDeletion(root) && CheckRoots(root); }
        catch (Exception error) { return Fail(error.ToString()); }
        finally
        {
            if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, true);
        }
#else
        return true;
#endif
    }

#if DEBUG
    private static bool CheckPersistence(string root)
    {
        string user = Path.Combine(root, "user");
        var builtins = new Dictionary<string, byte[]>();
        string fixtures = ProjectSettings.GlobalizePath("res://tests/scenario-configs/buy-process-sell");
        foreach (string path in Directory.GetFiles(fixtures, "*.json")) builtins.Add(Path.GetFileName(path), File.ReadAllBytes(path));
        var library = new ScenarioConfigurationLibrary(builtins, user);
        if (Directory.Exists(user) || library.Flows.Count != 1) return Fail("构造库提前创建目录或缺流程");
        var entries = library.ListConfigurations("buy-process-sell");
        if (entries.Count != 3 || entries.Any(entry => !entry.IsReadOnly)) return Fail("原3JSON未作为只读示例发现");
        foreach (ScenarioConfigurationEntry entry in entries)
        {
            var original = library.Open(entry);
            if (original.IsDirty || original.Errors.Count > 0 || !Throws(() => library.Overwrite(original)) ||
                !Throws(() => library.DeleteConfiguration(entry)) || !Throws(() => library.LoadForRun(original))) return Fail("内置只读保护失效");
        }
        ScenarioConfigurationDraft source = library.Open(entries[0]);
        source.SetValue("parameters.quantity", "2");
        var saved = library.SaveAs(source, "长期中文配置");
        var first = saved.Entry!;
        byte[] firstBytes = File.ReadAllBytes(first.Id);
        if (!source.IsDirty || saved.IsDirty || first.Revision != 1 || first.IsReadOnly || !File.Exists(first.Id)) return Fail("另存状态错误");
        if (library.ListConfigurations("buy-process-sell").Count != 4 || !Throws(() => library.SaveAs(source, "长期中文配置"))) return Fail("重复名称未拒绝");
        ScenarioConfiguration run = library.LoadForRun(saved);
        if (run.Quantity != 2 || run.Sha256 != Hash(firstBytes) || run.SourcePath != first.Id) return Fail("固定执行参数或原始摘要错误");
        saved.SetValue("parameters.quantity", "3");
        if (!Throws(() => library.LoadForRun(saved))) return Fail("未保存草稿可运行");
        var overwritten = library.Overwrite(saved);
        if (overwritten.Entry!.Revision != 2 || overwritten.IsDirty || run.Quantity != 2 || run.Sha256 != Hash(firstBytes)) return Fail("覆盖修订或运行隔离错误");
        var second = library.SaveAs(overwritten, "另一个配置");
        if (second.Entry!.Revision != 1 || library.ListConfigurations("buy-process-sell").Count != 5) return Fail("另存未重置修订");
        if (File.ReadAllBytes(first.Id).SequenceEqual(firstBytes)) return Fail("覆盖未改原文件");
        var reloaded = new ScenarioConfigurationLibrary(builtins, user);
        if (reloaded.ListConfigurations("buy-process-sell").Single(entry => entry.CaseId == "长期中文配置").Revision != 2) return Fail("持久修订未发现");
        string reports = Path.Combine(root, "reports"); Directory.CreateDirectory(reports);
        string report = Path.Combine(reports, "report.json"); File.WriteAllText(report, "historical");
        reloaded.DeleteConfiguration(second.Entry);
        if (File.Exists(second.Entry.Id) || !File.Exists(first.Id) || !File.Exists(report)) return Fail("单配置删除范围错误");
        if (!Throws(() => reloaded.Open(second.Entry))) return Fail("已删除选择仍能打开");
        reloaded.DeleteFlow("buy-process-sell");
        if (File.Exists(first.Id) || reloaded.Flows.Count != 0 || !File.Exists(report) || builtins.Count != 3) return Fail("流程删除范围错误");
        if (new ScenarioConfigurationLibrary(builtins, user).Flows.Count != 0 || !Throws(() => reloaded.ListConfigurations("buy-process-sell"))) return Fail("流程移除未持久");
        // 编辑器目录入口读取同一权威JSON；不改变它们的实际文件。
        var directoryLibrary = new ScenarioConfigurationLibrary(fixtures, Path.Combine(root, "directory-user"));
        if (directoryLibrary.ListConfigurations("buy-process-sell").Count != 3) return Fail("目录自动发现失败");
        GD.Print("配置库持久保存、原字节摘要与删除检查通过");
        return true;
    }

    private static bool CheckFailures(string root)
    {
        byte[] bytes = ScenarioConfigurationSchema.BuyProcessSell.Write(new ScenarioEditableConfiguration());
        var builtin = new Dictionary<string, byte[]> { ["sample.json"] = bytes };
        string user = Path.Combine(root, "failure-user");
        var library = new ScenarioConfigurationLibrary(builtin, user);
        var source = library.Open(library.ListConfigurations("buy-process-sell")[0]);
        var saved = library.SaveAs(source, "正常配置");
        byte[] original = File.ReadAllBytes(saved.Entry!.Id);
        saved.SetValue("parameters.quantity", "bad");
        if (!Throws(() => library.Overwrite(saved)) || !File.ReadAllBytes(saved.Entry.Id).SequenceEqual(original)) return Fail("校验失败损坏原文件");
        saved.SetValue("parameters.quantity", "3");
        File.WriteAllText(saved.Entry.Id, Encoding.UTF8.GetString(original).Replace("\"quantity\": 1", "\"quantity\": 4"));
        if (!Throws(() => library.Overwrite(saved)) || !Throws(() => library.DeleteConfiguration(saved.Entry))) return Fail("外部修改未重新校验");
        var fresh = library.Open(library.ListConfigurations("buy-process-sell").Single(entry => !entry.IsReadOnly));
        string invalidFile = Path.Combine(user, "invalid.json"); File.WriteAllText(invalidFile, "{}");
        if (library.ListConfigurations("buy-process-sell").Count != 2 || library.DiscoveryErrors.Count != 1 || !Throws(() => library.DeleteFlow("buy-process-sell"))) return Fail("非法文件未明确反馈或流程范围假成功");
        File.Delete(invalidFile);
        string outside = Path.Combine(root, "outside.json"); File.WriteAllBytes(outside, bytes);
        var fake = new ScenarioConfigurationEntry(outside, "新配置", 1, "buy-process-sell", false);
        if (!Throws(() => library.DeleteConfiguration(fake)) || !File.Exists(outside)) return Fail("外部路径可删除");
        string blocker = Path.Combine(root, "blocked"); File.WriteAllText(blocker, "not-directory");
        var blocked = new ScenarioConfigurationLibrary(builtin, Path.Combine(blocker, "user"));
        var blockedDraft = blocked.Open(blocked.ListConfigurations("buy-process-sell")[0]);
        if (!Throws(() => blocked.SaveAs(blockedDraft, "写入失败")) || !File.Exists(blocker) || !blockedDraft.Entry!.IsReadOnly) return Fail("保存失败未保持草稿或偷偷换位置");
        byte[] maximum = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"revision\": 1", "\"revision\": 2147483647"));
        string maximumPath = Path.Combine(user, "max.json"); File.WriteAllBytes(maximumPath, maximum);
        var maxDraft = library.Open(library.ListConfigurations("buy-process-sell").Single(entry => entry.Id == maximumPath));
        if (!Throws(() => library.Overwrite(maxDraft)) || !File.ReadAllBytes(maximumPath).SequenceEqual(maximum)) return Fail("修订上限溢出或损坏原文件");
        if (!Throws(() => library.ListConfigurations("unknown"))) return Fail("未知流程未拒绝");
        return true;
    }

    private static bool CheckRoots(string root)
    {
        if (ScenarioConfigurationLibrary.ResolveUserDirectory(root, Path.Combine(root, "FarmExchange.exe"), false) != Path.Combine(root, "tests", "scenario-configs", "user") ||
            ScenarioConfigurationLibrary.ResolveUserDirectory(root, Path.Combine(root, "FarmExchange.exe"), true) != Path.Combine(root, "configs", "scenario-configs") ||
            ScenarioConfigurationLibrary.ResolveUserDirectory(root, Path.Combine(root, "FarmExchange.app", "Contents", "MacOS", "FarmExchange"), true) != Path.Combine(root, "configs", "scenario-configs")) return Fail("Editor/Windows/macOS长期目录错误");
        return true;
    }

    private static bool CheckPartialDeletion(string root)
    {
        // Windows允许读取但禁止删除的句柄可检验实际失败；POSIX允许删除已打开文件。
        if (!OperatingSystem.IsWindows()) return true;
        byte[] bytes = ScenarioConfigurationSchema.BuyProcessSell.Write(new ScenarioEditableConfiguration());
        string user = Path.Combine(root, "partial-user");
        var library = new ScenarioConfigurationLibrary(new Dictionary<string, byte[]> { ["sample.json"] = bytes }, user);
        var source = library.Open(library.ListConfigurations("buy-process-sell")[0]);
        var first = library.SaveAs(source, "可删除配置");
        var blocked = library.SaveAs(source, "正在被其他程序占用");
        using (var handle = new FileStream(blocked.Entry!.Id, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
        {
            if (!Throws(() => library.DeleteFlow("buy-process-sell")) || library.Flows.Count != 1 || !File.Exists(blocked.Entry.Id)) return Fail("删除失败被误报为移除流程成功");
            var actual = library.ListConfigurations("buy-process-sell").Where(entry => !entry.IsReadOnly).ToArray();
            int files = Directory.GetFiles(user, "*.json").Length;
            if (actual.Length != files || actual.Any(entry => !File.Exists(entry.Id))) return Fail("删除失败后列表未反映实际剩余文件");
            // 同一锁也使覆盖失败；不能更新修订或破坏原文件。
            byte[] before = File.ReadAllBytes(blocked.Entry.Id);
            blocked.SetValue("parameters.quantity", "2");
            if (!Throws(() => library.Overwrite(blocked)) || !File.ReadAllBytes(blocked.Entry.Id).SequenceEqual(before) || blocked.Entry.Revision != 1) return Fail("写入失败修改了原文件或修订");
        }
        library.DeleteFlow("buy-process-sell");
        if (library.Flows.Count != 0 || File.Exists(blocked.Entry!.Id) || File.Exists(first.Entry!.Id)) return Fail("释放锁后未完成实际删除");
        return true;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool Throws(Action action)
    {
        try { action(); return false; } catch (ScenarioConfigurationException) { return true; }
    }
#endif
    private static bool Fail(string message) { GD.PushError("配置库检查失败：" + message); return false; }
}
