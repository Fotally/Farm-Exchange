using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FarmExchange.Development;

/**
 * <summary>独立编辑值、字段路径、非法输入与未保存状态的唯一拥有者。</summary>
 */
public sealed class ScenarioConfigurationDraft
{
    private readonly object _value;
    private readonly byte[] _original;
    private readonly Dictionary<string, string> _inputErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _invalidInputs = new(StringComparer.Ordinal);
    public ScenarioConfigurationSchema Schema { get; }
    public ScenarioConfigurationEntry? Entry { get; }
    public IReadOnlyDictionary<string, string> Errors => Validate();
    public bool IsDirty
    {
        get
        {
            if (_inputErrors.Count > 0) return true;
            try { return !Schema.Write(_value).SequenceEqual(_original); }
            catch (ScenarioConfigurationException) { return true; }
        }
    }

    /**
     * <summary>由原始字节构造独立草稿；可用于未登记为真实流程的字段形状测试。</summary>
     * <param name="schema">配置描述。</param>
     * <param name="bytes">合法JSON原字节。</param>
     * <param name="entry">配置库条目；独立形状测试可省略。</param>
     */
    public ScenarioConfigurationDraft(ScenarioConfigurationSchema schema, byte[] bytes, ScenarioConfigurationEntry? entry = null)
    {
        Schema = schema; Entry = entry; _value = schema.Read(bytes); _original = schema.Write(_value);
    }

    /**
     * <summary>读取标量或数组长度，不向UI泄漏可变对象；非法输入原文优先返回。</summary>
     * <param name="path">点式字段路径，数组元素使用[索引]。</param>
     * <returns>标量值；数组为int长度，嵌套对象为null。</returns>
     */
    public object? GetValue(string path)
    {
        if (_invalidInputs.TryGetValue(path, out string? invalid)) return invalid;
        var (owner, field) = Resolve(path);
        object? value = field.Property.GetValue(owner);
        return field.Kind == ConfigFieldKind.Array ? ((IList)value!).Count : field.Kind == ConfigFieldKind.Object ? null : value;
    }

    /**
     * <summary>按原类型设置标量，非法输入保留以供修正，不写入文件。</summary>
     * <param name="path">字段路径。</param>
     * <param name="text">原始编辑文本，整数不经过浮点转换。</param>
     * <returns>本字段输入是否有效；完整草稿仍需Validate。</returns>
     */
    public bool SetValue(string path, string text)
    {
        var (owner, field) = Resolve(path);
        if (field.IsReadOnly || !IsVisible(path) || field.Kind is ConfigFieldKind.Object or ConfigFieldKind.Array)
            throw ScenarioConfigurationSchema.Error(path, "字段不能直接编辑");
        try
        {
            field.Property.SetValue(owner, ScenarioConfigurationSchema.ParseInput(field, text, path));
            _inputErrors.Remove(path); _invalidInputs.Remove(path);
            // 条件切换后，已隐藏字段的非法输入不影响保存，已解析值仍保留供再次切换。
            foreach (string key in _inputErrors.Keys.ToArray())
                if (!IsVisible(key)) { _inputErrors.Remove(key); _invalidInputs.Remove(key); }
            return true;
        }
        catch (ScenarioConfigurationException error)
        {
            _inputErrors[path] = error.Message; _invalidInputs[path] = text; return false;
        }
    }

    /**
     * <summary>查询条件字段在当前草稿中的可见性。</summary>
     * <param name="path">字段路径。</param>
     * <returns>当前条件是否允许显示及输出该字段。</returns>
     */
    public bool IsVisible(string path)
    {
        string[] components = path.Split('.');
        for (int index = 0; index < components.Length; index++)
        {
            string prefix = string.Join('.', components.Take(index + 1));
            int bracket = prefix.LastIndexOf('[');
            if (prefix.EndsWith(']') && bracket >= 0) prefix = prefix[..bracket];
            var (owner, field) = Resolve(prefix);
            if (!field.IsActive(owner)) return false;
        }
        return true;
    }

    /**
     * <summary>校验全部已输入值及时间顺序，按字段路径返回错误。</summary>
     * <returns>独立只读错误快照；空集合表示可保存。</returns>
     */
    public IReadOnlyDictionary<string, string> Validate()
    {
        var errors = new Dictionary<string, string>(_inputErrors, StringComparer.Ordinal);
        try { Schema.Write(_value); }
        catch (ScenarioConfigurationException error)
        {
            int separator = error.Message.IndexOf('：');
            string path = separator >= 0 ? error.Message[..separator] : "";
            errors.TryAdd(path, error.Message);
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(errors);
    }

    /**
     * <summary>向有序对象数组加入其已登记类型的默认行。</summary>
     * <param name="path">数组字段路径。</param>
     */
    public void AddArrayItem(string path)
    {
        var (owner, field) = ResolveArray(path);
        ((IList)field.Property.GetValue(owner)!).Add(Activator.CreateInstance(field.ValueType.GetGenericArguments()[0])!);
    }

    /**
     * <summary>删除有序数组行，保留其余行输入与错误。</summary>
     * <param name="path">数组字段路径。</param>
     * <param name="index">从零开始的行索引。</param>
     */
    public void RemoveArrayItem(string path, int index)
    {
        var (owner, field) = ResolveArray(path);
        var list = (IList)field.Property.GetValue(owner)!;
        CheckIndex(list, index); list.RemoveAt(index);
        ReindexInputs(path, old => old == index ? -1 : old > index ? old - 1 : old);
    }

    /**
     * <summary>移动有序数组行，错误随原行移动。</summary>
     * <param name="path">数组字段路径。</param>
     * <param name="index">原索引。</param>
     * <param name="target">目标索引。</param>
     */
    public void MoveArrayItem(string path, int index, int target)
    {
        var (owner, field) = ResolveArray(path);
        var list = (IList)field.Property.GetValue(owner)!;
        CheckIndex(list, index); CheckIndex(list, target);
        object item = list[index]!; list.RemoveAt(index); list.Insert(target, item);
        ReindexInputs(path, old => old == index ? target : index < target && old > index && old <= target ? old - 1 :
            index > target && old >= target && old < index ? old + 1 : old);
    }

    internal byte[] GetBytes()
    {
        var errors = Validate();
        if (errors.Count > 0) throw new ScenarioConfigurationException(string.Join("\n", errors.Values));
        return Schema.Write(_value);
    }

    private (object Owner, ConfigFieldDescriptor Field) ResolveArray(string path)
    {
        var result = Resolve(path);
        if (result.Field.Kind != ConfigFieldKind.Array || result.Field.IsReadOnly || !IsVisible(path))
            throw ScenarioConfigurationSchema.Error(path, "须为可编辑数组");
        return result;
    }

    private (object Owner, ConfigFieldDescriptor Field) Resolve(string path)
    {
        object owner = _value;
        IReadOnlyList<ConfigFieldDescriptor> fields = Schema.Fields;
        string[] components = path.Split('.');
        for (int position = 0; position < components.Length; position++)
        {
            string component = components[position];
            int bracket = component.IndexOf('[');
            string name = bracket < 0 ? component : component[..bracket];
            ConfigFieldDescriptor field = fields.FirstOrDefault(item => item.Name == name) ?? throw ScenarioConfigurationSchema.Error(path, "未知字段路径");
            if (position == components.Length - 1 && bracket < 0) return (owner, field);
            object next = field.Property.GetValue(owner)!;
            if (bracket >= 0)
            {
                if (field.Kind != ConfigFieldKind.Array || !component.EndsWith(']') ||
                    !int.TryParse(component[(bracket + 1)..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                    throw ScenarioConfigurationSchema.Error(path, "数组路径无效");
                var list = (IList)next; CheckIndex(list, index); next = list[index]!;
            }
            else if (field.Kind != ConfigFieldKind.Object) throw ScenarioConfigurationSchema.Error(path, "字段不是嵌套对象");
            owner = next; fields = field.Children;
        }
        throw ScenarioConfigurationSchema.Error(path, "须指向具体字段");
    }

    private void ReindexInputs(string path, Func<int, int> map)
    {
        foreach (Dictionary<string, string> dictionary in new[] { _inputErrors, _invalidInputs })
        {
            var snapshot = dictionary.Where(pair => pair.Key.StartsWith(path + "[", StringComparison.Ordinal)).ToArray();
            foreach (var pair in snapshot) dictionary.Remove(pair.Key);
            foreach (var pair in snapshot)
            {
                int closing = pair.Key.IndexOf(']', path.Length);
                int old = int.Parse(pair.Key[(path.Length + 1)..closing], CultureInfo.InvariantCulture);
                int mapped = map(old);
                if (mapped >= 0) dictionary[path + "[" + mapped + pair.Key[closing..]] = pair.Value;
            }
        }
    }

    private static void CheckIndex(IList list, int index)
    {
        if (index < 0 || index >= list.Count) throw new ScenarioConfigurationException("数组行索引超出范围");
    }
}
