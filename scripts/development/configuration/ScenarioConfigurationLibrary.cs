using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace FarmExchange.Development;

/**
 * <summary>已登记真实流程的中文目录与配置描述。</summary>
 */
public sealed record ScenarioFlowDefinition(string Id, string Name, string Description, string Category, ScenarioConfigurationSchema Schema);

/**
 * <summary>配置目录条目；Id只用于库操作，界面显示CaseId和Revision。</summary>
 */
public sealed record ScenarioConfigurationEntry(string Id, string CaseId, int Revision, string FlowId, bool IsReadOnly)
{
    internal string Sha256 { get; init; } = "";
}

/**
 * <summary>唯一维护配置发现、修订、受限文件操作及持久流程可见性。</summary>
 */
public sealed class ScenarioConfigurationLibrary
{
    private const string CatalogFileName = ".hidden-flows.json";
    private static readonly ScenarioFlowDefinition Definition = new("buy-process-sell", "买入 → 加工 → 委托卖出",
        "买入原料，经指定场地加工，再以一次委托卖出，核对资源与成交。", "生产与交易", ScenarioConfigurationSchema.BuyProcessSell);
    private readonly Dictionary<string, byte[]> _builtins;
    private readonly string _userDirectory;
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private readonly List<string> _discoveryErrors = new();
    public IReadOnlyList<string> DiscoveryErrors => _discoveryErrors.AsReadOnly();
    public IReadOnlyList<ScenarioFlowDefinition> Flows => _hidden.Contains(Definition.Id) ? Array.Empty<ScenarioFlowDefinition>() : new[] { Definition };

    /**
     * <summary>注入只读示例字节与唯一可写目录；不创建目录或替换失败路径。</summary>
     * <param name="builtinConfigurations">由权威示例生成的包内资源；键为文件名。</param>
     * <param name="userDirectory">长期可写配置目录，允许纳入Git。</param>
     */
    public ScenarioConfigurationLibrary(IReadOnlyDictionary<string, byte[]> builtinConfigurations, string userDirectory)
    {
        _userDirectory = Path.GetFullPath(userDirectory);
        _builtins = builtinConfigurations.ToDictionary(pair => "builtin:" + pair.Key, pair => (byte[])pair.Value.Clone(), StringComparer.Ordinal);
        foreach (byte[] bytes in _builtins.Values) ScenarioConfigurationSchema.BuyProcessSell.Read(bytes);
        FileOperation(() =>
        {
            CheckUserDirectoryLinks(_userDirectory);
            string catalog = Path.Combine(_userDirectory, CatalogFileName);
            if (File.Exists(catalog))
            {
                CheckFile(catalog);
                string[] hidden = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(catalog)) ?? throw new ScenarioConfigurationException("流程目录记录须为数组");
                foreach (string flow in hidden)
                {
                    if (flow != Definition.Id || !_hidden.Add(flow)) throw new ScenarioConfigurationException("流程目录记录包含未知或重复流程");
                }
            }
        });
    }

    /**
     * <summary>从编辑器示例目录自动读取只读JSON，排除用户目录。</summary>
     * <param name="builtinDirectory">权威示例目录。</param>
     * <param name="userDirectory">用户长期配置目录。</param>
     */
    public ScenarioConfigurationLibrary(string builtinDirectory, string userDirectory)
        : this(ReadBuiltins(builtinDirectory, userDirectory), userDirectory) { }

    /**
     * <summary>集中定义编辑器和两平台开发包的用户配置位置。</summary>
     * <param name="projectRoot">编辑器项目绝对目录。</param>
     * <param name="executablePath">开发包可执行文件绝对路径。</param>
     * <param name="exported">是否为已导出程序。</param>
     * <returns>编辑器仓库子目录，或exe/.app旁的configs目录。</returns>
     */
    public static string ResolveUserDirectory(string projectRoot, string executablePath, bool exported)
    {
        if (!exported) return Path.GetFullPath(Path.Combine(projectRoot, "tests", "scenario-configs", "user"));
        DirectoryInfo directory = new(Path.GetDirectoryName(Path.GetFullPath(executablePath))!);
        for (DirectoryInfo? current = directory; current != null; current = current.Parent)
            if (current.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(current.Parent!.FullName, "configs", "scenario-configs");
        return Path.Combine(directory.FullName, "configs", "scenario-configs");
    }

    /**
     * <summary>自动发现指定可见流程的只读示例与用户配置。</summary>
     * <param name="flowId">已登记且可见流程标识。</param>
     * <returns>只读目录快照；非法文件详情记录于DiscoveryErrors。</returns>
     */
    public IReadOnlyList<ScenarioConfigurationEntry> ListConfigurations(string flowId)
    {
        RequireFlow(flowId);
        return Discover().OrderBy(entry => entry.IsReadOnly ? 0 : 1).ThenBy(entry => entry.CaseId, StringComparer.Ordinal).ToArray();
    }

    /**
     * <summary>从目录条目读取一次，返回独立草稿。</summary>
     * <param name="entry">当前目录中的条目。</param>
     * <returns>编辑值与运行参数相互独立的草稿。</returns>
     */
    public ScenarioConfigurationDraft Open(ScenarioConfigurationEntry entry)
    {
        RequireFlow(entry.FlowId);
        byte[] bytes = ReadCurrent(entry);
        return new ScenarioConfigurationDraft(Definition.Schema, bytes, entry);
    }

    /**
     * <summary>显式重选时按稳定文件身份重新发现并打开最新版本，不改变旧草稿凭据。</summary>
     * <param name="entry">用户明确选择重新加载的原目录条目。</param>
     * <returns>最新合法目录和独立草稿；文件失效时返回空草稿及明确错误。</returns>
     */
    public ScenarioConfigurationSelection ReloadSelection(ScenarioConfigurationEntry entry)
    {
        IReadOnlyList<ScenarioConfigurationEntry> entries = ListConfigurations(entry.FlowId);
        ScenarioConfigurationEntry? current = entries.FirstOrDefault(item => item.Id == entry.Id);
        if (current == null)
            return new ScenarioConfigurationSelection(entries, null, "所选配置已删除或不再合法，请从最新目录选择配置。");
        try { return new ScenarioConfigurationSelection(entries, Open(current), null); }
        catch (ScenarioConfigurationException error)
        {
            return new ScenarioConfigurationSelection(Array.AsReadOnly(entries.Where(item => item.Id != entry.Id).ToArray()), null,
                "重新加载失败：" + error.Message);
        }
    }

    /**
     * <summary>以新名称另存为修订1；原文件和草稿保持。</summary>
     * <param name="draft">本库所选流程的合法草稿。</param>
     * <param name="newCaseId">新配置名称。</param>
     * <returns>成功写入后的新草稿；失败不替换位置或假报成功。</returns>
     */
    public ScenarioConfigurationDraft SaveAs(ScenarioConfigurationDraft draft, string newCaseId)
    {
        RequireDraft(draft);
        var value = (ScenarioEditableConfiguration)Definition.Schema.Read(draft.GetBytes());
        value.CaseId = newCaseId; value.Revision = 1;
        byte[] bytes = Definition.Schema.Write(value);
        if (Discover().Any(entry => entry.CaseId == newCaseId)) throw new ScenarioConfigurationException("已有同名配置，请输入新名称");
        string filename = Guid.NewGuid().ToString("N") + ".json";
        string path = Path.Combine(_userDirectory, filename);
        SaveFile(path, bytes, false);
        return SavedDraft(path, bytes);
    }

    /**
     * <summary>重验当前可写条目，覆盖成功后修订加一。</summary>
     * <param name="draft">从可写条目打开的草稿。</param>
     * <returns>成功后的草稿；原字节改变、容量或写入失败明确拒绝。</returns>
     */
    public ScenarioConfigurationDraft Overwrite(ScenarioConfigurationDraft draft)
    {
        RequireDraft(draft);
        ScenarioConfigurationEntry entry = draft.Entry ?? throw new ScenarioConfigurationException("尚未选择持久配置");
        if (entry.IsReadOnly) throw new ScenarioConfigurationException("内置示例只读，请另存为自己的配置");
        ReadCurrent(entry);
        if (entry.Revision == int.MaxValue) throw new ScenarioConfigurationException("配置修订已达到int32上限");
        var value = (ScenarioEditableConfiguration)Definition.Schema.Read(draft.GetBytes());
        value.Revision = entry.Revision + 1;
        if (Discover().Any(other => other.Id != entry.Id && other.CaseId == value.CaseId)) throw new ScenarioConfigurationException("已有同名配置");
        byte[] bytes = Definition.Schema.Write(value);
        SaveFile(entry.Id, bytes, true);
        return SavedDraft(entry.Id, bytes);
    }

    /**
     * <summary>重验目录归属后，仅删除指定可写文件；历史报告不参与操作。</summary>
     * <param name="entry">确认弹窗所对应的当前条目。</param>
     */
    public void DeleteConfiguration(ScenarioConfigurationEntry entry)
    {
        RequireFlow(entry.FlowId);
        if (entry.IsReadOnly) throw new ScenarioConfigurationException("内置示例不能删除");
        ReadCurrent(entry);
        FileOperation(() => { CheckFile(entry.Id); File.Delete(entry.Id); });
    }

    /**
     * <summary>删除归属流程的全部可写配置，再持久移除目录项；代码、内置示例和报告保留。</summary>
     * <param name="flowId">用户已确认删除的流程标识。</param>
     * <remarks>遇到实际文件失败停止并反馈，已完成的文件删除不会伪装成回滚。</remarks>
     */
    public void DeleteFlow(string flowId)
    {
        RequireFlow(flowId);
        var entries = Discover().Where(entry => !entry.IsReadOnly).ToArray();
        if (_discoveryErrors.Count > 0) throw new ScenarioConfigurationException("存在无法识别的用户配置，不能确认删除范围：\n" + string.Join("\n", _discoveryErrors));
        foreach (ScenarioConfigurationEntry entry in entries) ReadCurrent(entry);
        foreach (ScenarioConfigurationEntry entry in entries) DeleteConfiguration(entry);
        SaveFile(Path.Combine(_userDirectory, CatalogFileName), JsonSerializer.SerializeToUtf8Bytes(new[] { flowId }), true);
        _hidden.Add(flowId);
    }

    /**
     * <summary>从已保存的可写配置固定运行参数，拒绝草稿或只读资源直接执行。</summary>
     * <param name="draft">准备执行的草稿。</param>
     * <returns>一次读取字节所得的固定参数和SHA-256。</returns>
     */
    public ScenarioConfiguration LoadForRun(ScenarioConfigurationDraft draft)
    {
        RequireDraft(draft);
        ScenarioConfigurationEntry entry = draft.Entry ?? throw new ScenarioConfigurationException("请先保存配置");
        if (entry.IsReadOnly) throw new ScenarioConfigurationException("内置示例请先另存为自己的配置");
        if (draft.IsDirty) throw new ScenarioConfigurationException("配置存在未保存修改，请先保存");
        byte[] bytes = ReadCurrent(entry);
        return ScenarioConfiguration.FromBytes(entry.Id, bytes);
    }

    private List<ScenarioConfigurationEntry> Discover()
    {
        _discoveryErrors.Clear();
        var entries = _builtins.Select(pair => Describe(pair.Key, pair.Value, true)).ToList();
        FileOperation(() =>
        {
            CheckUserDirectoryLinks(_userDirectory);
            if (!Directory.Exists(_userDirectory)) return;
            foreach (string path in EnumerateJson(_userDirectory))
            {
                if (path == Path.Combine(_userDirectory, CatalogFileName)) continue;
                try { CheckFile(path); entries.Add(Describe(path, File.ReadAllBytes(path), false)); }
                catch (ScenarioConfigurationException exception) { _discoveryErrors.Add(Path.GetFileName(path) + "：" + exception.Message); }
            }
        });
        return entries;
    }

    private static ScenarioConfigurationEntry Describe(string id, byte[] bytes, bool readOnly)
    {
        var value = (ScenarioEditableConfiguration)Definition.Schema.Read(bytes);
        return new ScenarioConfigurationEntry(id, value.CaseId, value.Revision, value.Flow, readOnly) { Sha256 = Hash(bytes) };
    }

    private byte[] ReadCurrent(ScenarioConfigurationEntry entry)
    {
        byte[]? bytes = null;
        FileOperation(() =>
        {
            if (entry.IsReadOnly)
            {
                if (!_builtins.TryGetValue(entry.Id, out bytes)) throw new ScenarioConfigurationException("此内置条目不属于配置库");
            }
            else
            {
                CheckFile(entry.Id);
                if (!entry.Id.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(entry.Id) == CatalogFileName)
                    throw new ScenarioConfigurationException("此文件不是配置条目");
                bytes = File.ReadAllBytes(entry.Id);
            }
        });
        ScenarioConfigurationEntry current = Describe(entry.Id, bytes!, entry.IsReadOnly);
        if (current.CaseId != entry.CaseId || current.FlowId != entry.FlowId || current.Revision != entry.Revision || current.Sha256 != entry.Sha256)
            throw new ScenarioConfigurationException("配置文件已改变，请重新选择后操作");
        return bytes!;
    }

    private ScenarioConfigurationDraft SavedDraft(string path, byte[] bytes) => new(Definition.Schema, bytes, Describe(path, bytes, false));
    private void RequireFlow(string flowId)
    {
        if (flowId != Definition.Id || _hidden.Contains(flowId)) throw new ScenarioConfigurationException("流程不在当前目录中");
    }
    private void RequireDraft(ScenarioConfigurationDraft draft)
    {
        RequireFlow(Definition.Id);
        if (draft.Schema != Definition.Schema) throw new ScenarioConfigurationException("草稿不属于已登记流程");
    }

    private void SaveFile(string path, byte[] bytes, bool overwrite)
    {
        FileOperation(() =>
        {
            CheckFile(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                CheckFile(path);
                File.Move(temporary, path, overwrite);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        });
    }

    private void CheckFile(string path)
    {
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(_userDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison))
            throw new ScenarioConfigurationException("文件不属于用户配置目录");
        CheckUserDirectoryLinks(Path.GetDirectoryName(full)!);
        if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new ScenarioConfigurationException("用户配置不能通过符号链接操作");
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private void CheckUserDirectoryLinks(string path)
    {
        for (DirectoryInfo? directory = new(Path.GetFullPath(path)); directory != null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ScenarioConfigurationException("配置目录不能通过符号链接操作");
            if (string.Equals(directory.FullName.TrimEnd(Path.DirectorySeparatorChar), _userDirectory.TrimEnd(Path.DirectorySeparatorChar), PathComparison)) break;
        }
    }
    private static IEnumerable<string> EnumerateJson(string directory)
    {
        foreach (string path in Directory.EnumerateFiles(directory, "*.json")) yield return Path.GetFullPath(path);
        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if ((new DirectoryInfo(child).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ScenarioConfigurationException("配置子目录不能通过符号链接操作");
            foreach (string path in EnumerateJson(child)) yield return path;
        }
    }
    private static IReadOnlyDictionary<string, byte[]> ReadBuiltins(string builtinDirectory, string userDirectory)
    {
        var result = new Dictionary<string, byte[]>();
        FileOperation(() =>
        {
            string root = Path.GetFullPath(builtinDirectory);
            string user = Path.GetFullPath(userDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string path in EnumerateJson(root))
                if (!path.StartsWith(user, PathComparison)) result.Add(Path.GetRelativePath(root, path), File.ReadAllBytes(path));
        });
        return result;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void FileOperation(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { throw new ScenarioConfigurationException("配置文件操作失败：" + exception.Message, exception); }
    }
}
