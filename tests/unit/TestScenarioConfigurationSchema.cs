using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using Godot;
#if DEBUG
using FarmExchange.Development;
#endif

public partial class TestScenarioConfigurationSchema : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks()
    {
#if DEBUG
        try
        {
            var schema = ScenarioConfigurationSchema.BuyProcessSell;
            byte[] bytes = schema.Write(new ScenarioEditableConfiguration());
            var draft = new ScenarioConfigurationDraft(schema, bytes);
            if (draft.IsDirty || draft.Errors.Count != 0 || schema.Fields.Any(field => field.Name is "SourcePath" or "Sha256" or "ProductCommodity")) return Fail("运行属性泄漏或初始状态错误");
            if (!draft.SetValue("parameters.quantity", int.MaxValue.ToString()) || (int)draft.GetValue("parameters.quantity")! != int.MaxValue) return Fail("int32上限丢失");
            if (!draft.SetValue("parameters.processingWaitLimitTicks", uint.MaxValue.ToString()) || (uint)draft.GetValue("parameters.processingWaitLimitTicks")! != uint.MaxValue) return Fail("uint32上限丢失");
            if (!draft.SetValue("parameters.processorAnchor.x", int.MinValue.ToString()) || (int)draft.GetValue("parameters.processorAnchor.x")! != int.MinValue) return Fail("int32负数丢失");
            foreach (string invalid in new[] { "2147483648", "1.5", "1e2", "", "0", "-1" })
            {
                if (draft.SetValue("parameters.quantity", invalid) || !Equals(draft.GetValue("parameters.quantity"), invalid) || !draft.Errors.ContainsKey("parameters.quantity")) return Fail("非法整数未保持输入");
            }
            draft.SetValue("parameters.quantity", "2");
            foreach (string invalid in new[] { "-1", "4294967296", "1.2", "0" })
                if (draft.SetValue("parameters.processingWaitLimitTicks", invalid)) return Fail("非法uint32被接受");
            draft.SetValue("parameters.processingWaitLimitTicks", "4294967295");
            foreach (string invalid in new[] { "00-01-01", "01-13-01", "01-01-29", "01-1-01", "1a-01-01", "" })
                if (draft.SetValue("execution.timePlan[0].endDate", invalid)) return Fail("非法游戏日期被接受");
            draft.SetValue("execution.timePlan[0].endDate", "01-02-01");
            foreach (string invalid in new[] { "1.5", "0", "-1", "NaN", "Infinity" })
                if (draft.SetValue("execution.timePlan[0].rate", invalid)) return Fail("非法倍率被接受");
            draft.SetValue("execution.timePlan[0].rate", "0.5");
            if (draft.SetValue("run.seed", "bad") || !draft.Errors.ContainsKey("run.seed")) return Fail("种子错误未定位");
            draft.SetValue("run.target", "current");
            if (draft.IsVisible("run.seed") || draft.Errors.ContainsKey("run.seed")) return Fail("条件种子错误未清理");
            draft.SetValue("run.target", "independent");
            if (!draft.IsVisible("run.seed")) return Fail("种子未重新显示");
            if (!Throws(() => draft.SetValue("revision", "3")) || !Throws(() => draft.SetValue("missing", "x"))) return Fail("只读或未知字段可修改");
            draft.AddArrayItem("execution.timePlan");
            draft.SetValue("execution.timePlan[1].endDate", "01-03-01");
            if (!draft.SetValue("execution.timePlan[1].rate", "20") || draft.Errors.Count != 0) return Fail("数组新行不可编辑");
            draft.SetValue("execution.timePlan[1].rate", "bad");
            draft.MoveArrayItem("execution.timePlan", 1, 0);
            if (!Equals(draft.GetValue("execution.timePlan[0].rate"), "bad") || !draft.Errors.ContainsKey("execution.timePlan[0].rate")) return Fail("移动未保留行错误");
            draft.SetValue("execution.timePlan[0].rate", "20");
            if (!draft.Errors.ContainsKey("execution.timePlan[1].endDate")) return Fail("非递增数组未拒绝");
            draft.MoveArrayItem("execution.timePlan", 0, 1);
            draft.RemoveArrayItem("execution.timePlan", 0);
            if ((int)draft.GetValue("execution.timePlan")! != 1 || draft.Errors.Count != 0) return Fail("数组删除状态错误");
            draft.RemoveArrayItem("execution.timePlan", 0);
            if (!draft.Errors.ContainsKey("execution.timePlan")) return Fail("空时间数组未拒绝");
            if (!Throws(() => draft.RemoveArrayItem("execution.timePlan", 0))) return Fail("超界数组行未拒绝");
            return CheckAlternateShape() && CheckStrictInput(schema, bytes);
        }
        catch (Exception error) { return Fail(error.ToString()); }
#else
        return true;
#endif
    }

#if DEBUG
    private static bool CheckStrictInput(ScenarioConfigurationSchema schema, byte[] bytes)
    {
        string valid = Encoding.UTF8.GetString(bytes);
        foreach (string invalid in new[]
        {
            valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"),
            valid.Replace("\"caseId\":", "\"unknown\": 1,\"caseId\":"),
            valid.Replace("\"caseId\":", "\"caseId\": \"dup\",\"caseId\":"),
            valid.Replace("\"revision\": 1,", ""),
            valid.Replace("\"quantity\": 1", "\"quantity\": \"1\""),
            valid.Replace("\"seed\": 12345", "\"seed\": null"),
            valid.Replace("independent", "current"), "[]", "null", "{",
        }) if (!Throws(() => schema.Read(Encoding.UTF8.GetBytes(invalid)))) return Fail("严格JSON读取接受非法输入：" + invalid);
        if (!Throws(() => schema.Read(new byte[] { 0xff }))) return Fail("非法UTF-8被接受");
        byte[] bom = new byte[bytes.Length + 3]; bom[0] = 0xef; bom[1] = 0xbb; bom[2] = 0xbf; bytes.CopyTo(bom, 3);
        if (schema.Read(bom) is not ScenarioEditableConfiguration) return Fail("UTF-8 BOM兼容失败");
        GD.Print("配置描述与草稿检查通过");
        return true;
    }

    private static bool CheckAlternateShape()
    {
        var schema = new ScenarioConfigurationSchema(typeof(AlternateConfiguration));
        var draft = new ScenarioConfigurationDraft(schema, schema.Write(new AlternateConfiguration()));
        ConfigFieldDescriptor mode = schema.Fields.Single(field => field.Name == "mode");
        if (mode.Options.Count != 2 || mode.Options[0] != new ConfigOption("First", "第一种") ||
            mode.Options[1] != new ConfigOption("Second", "第二种")) return Fail("枚举正式值与中文标签映射错误");
        if (!draft.SetValue("enabled", "False") || (bool)draft.GetValue("enabled")! ||
            !draft.SetValue("nested.title", "新的中文名") || !draft.SetValue("mode", "Second")) return Fail("第二字段形状未由同描述处理");
        foreach (string invalid in new[] { "Unknown", "second", "0", "第二种" })
            if (draft.SetValue("mode", invalid)) return Fail("枚举接受非正式成员名");
        draft.SetValue("mode", "Second");
        byte[] encoded = schema.Write(new AlternateConfiguration { Mode = FixtureMode.Second });
        if (((AlternateConfiguration)schema.Read(encoded)).Mode != FixtureMode.Second ||
            !Encoding.UTF8.GetString(encoded).Contains("\"mode\": \"Second\"", StringComparison.Ordinal)) return Fail("枚举正式值无损读写失败");
        draft.AddArrayItem("rows");
        draft.SetValue("rows[1].title", "第二行");
        draft.SetValue("rows[1].count", "3");
        draft.MoveArrayItem("rows", 1, 0);
        if (!Equals(draft.GetValue("rows[0].title"), "第二行") || draft.Errors.Count != 0) return Fail("第二数组形状无法调整顺序");
        draft.RemoveArrayItem("rows", 1);
        if (!Throws(() => new ScenarioConfigurationSchema(typeof(DictionaryConfiguration))) ||
            !Throws(() => new ScenarioConfigurationSchema(typeof(RecursiveConfiguration))) ||
            !Throws(() => new ScenarioConfigurationSchema(typeof(MissingMetadataConfiguration))) ||
            !Throws(() => new ScenarioConfigurationSchema(typeof(MissingEnumLabelsConfiguration))) ||
            !Throws(() => new ScenarioConfigurationSchema(typeof(MismatchedEnumLabelsConfiguration))) ||
            !Throws(() => new ScenarioConfigurationSchema(typeof(UnknownEnumOptionConfiguration))) ||
            !Throws(() => new ScenarioConfigurationSchema(typeof(DuplicateEnumOptionConfiguration)))) return Fail("不支持或非法元数据配置未明确拒绝");
        string encodedText = Encoding.UTF8.GetString(encoded);
        if (!Throws(() => schema.Read(Encoding.UTF8.GetBytes(encodedText.Replace("\"Second\"", "\"Unknown\"", StringComparison.Ordinal)))))
            return Fail("JSON读取接受未知枚举成员");
        return true;
    }

    public enum FixtureMode { First, Second }
    public sealed class AlternateConfiguration
    {
        [JsonPropertyName("enabled"), ConfigField("启用")]
        public bool Enabled { get; set; } = true;
        [JsonPropertyName("mode"), ConfigField("模式", Values = new[] { "First", "Second" }, Labels = new[] { "第一种", "第二种" })]
        public FixtureMode Mode { get; set; }
        [JsonPropertyName("nested"), ConfigField("详情")]
        public AlternateRow Nested { get; set; } = new();
        [JsonPropertyName("rows"), ConfigField("行列表")]
        public List<AlternateRow> Rows { get; set; } = new() { new() };
    }
    public sealed class AlternateRow
    {
        [JsonPropertyName("title"), ConfigField("名称")]
        public string Title { get; set; } = "示例";
        [JsonPropertyName("count"), ConfigField("数量", Minimum = 0)]
        public int Count { get; set; }
    }
    public sealed class DictionaryConfiguration
    {
        [JsonPropertyName("values"), ConfigField("不支持的字典")]
        public Dictionary<string, int> Values { get; set; } = new();
    }
    public sealed class RecursiveConfiguration
    {
        [JsonPropertyName("child"), ConfigField("递归对象")]
        public RecursiveConfiguration? Child { get; set; }
    }
    public sealed class MissingMetadataConfiguration
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "无元数据";
    }
    public sealed class MissingEnumLabelsConfiguration
    {
        [JsonPropertyName("mode"), ConfigField("模式")]
        public FixtureMode Mode { get; set; }
    }
    public sealed class MismatchedEnumLabelsConfiguration
    {
        [JsonPropertyName("mode"), ConfigField("模式", Values = new[] { "First", "Second" }, Labels = new[] { "第一种" })]
        public FixtureMode Mode { get; set; }
    }
    public sealed class UnknownEnumOptionConfiguration
    {
        [JsonPropertyName("mode"), ConfigField("模式", Values = new[] { "Unknown" }, Labels = new[] { "未知" })]
        public FixtureMode Mode { get; set; }
    }
    public sealed class DuplicateEnumOptionConfiguration
    {
        [JsonPropertyName("mode"), ConfigField("模式", Values = new[] { "First", "First" }, Labels = new[] { "第一种", "重复" })]
        public FixtureMode Mode { get; set; }
    }
    private static bool Throws(Action action)
    {
        try { action(); return false; } catch (ScenarioConfigurationException) { return true; }
    }
#endif
    private static bool Fail(string message) { GD.PushError("配置描述检查失败：" + message); return false; }
}
