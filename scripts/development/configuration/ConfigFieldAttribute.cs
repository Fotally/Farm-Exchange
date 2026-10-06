using System;
using System.Collections.Generic;
using System.Reflection;

namespace FarmExchange.Development;

/**
 * <summary>序列化配置字段的中文编辑约定；不标记运行结果或经营对象。</summary>
 */
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigFieldAttribute : Attribute
{
    public ConfigFieldAttribute(string label) => Label = label;
    public string Label { get; }
    public string Description { get; set; } = "";
    public string Group { get; set; } = "";
    public string Unit { get; set; } = "";
    public bool ReadOnly { get; set; }
    public long Minimum { get; set; } = long.MinValue;
    public long Maximum { get; set; } = long.MaxValue;
    public string Editor { get; set; } = "";
    public string[] Values { get; set; } = Array.Empty<string>();
    public string[] Labels { get; set; } = Array.Empty<string>();
    public string VisibleWhenProperty { get; set; } = "";
    public string VisibleWhenValue { get; set; } = "";
}

/**
 * <summary>通用表单支持的值形状。</summary>
 */
public enum ConfigFieldKind { Text, Integer, UnsignedInteger, Number, Boolean, Choice, Object, Array, GameDate, Rate }

/**
 * <summary>保存正式值、显示中文名的选项。</summary>
 */
public sealed record ConfigOption(string Value, string Label);

/**
 * <summary>自动扫描出的只读字段描述；数组Children描述单个元素。</summary>
 */
public sealed class ConfigFieldDescriptor
{
    internal ConfigFieldDescriptor(string name, ConfigFieldAttribute metadata, ConfigFieldKind kind,
        PropertyInfo property, IReadOnlyList<ConfigFieldDescriptor> children, IReadOnlyList<ConfigOption> options)
    {
        Name = name; Metadata = metadata; Kind = kind; Property = property; Children = children; Options = options;
    }
    public string Name { get; }
    public string Label => Metadata.Label;
    public string Description => Metadata.Description;
    public string Group => Metadata.Group;
    public string Unit => Metadata.Unit;
    public bool IsReadOnly => Metadata.ReadOnly;
    public long Minimum => ValueType == typeof(int) ? Math.Max(Metadata.Minimum, int.MinValue) :
        ValueType == typeof(uint) ? Math.Max(Metadata.Minimum, 0) : Metadata.Minimum;
    public long Maximum => ValueType == typeof(int) ? Math.Min(Metadata.Maximum, int.MaxValue) :
        ValueType == typeof(uint) ? Math.Min(Metadata.Maximum, uint.MaxValue) : Metadata.Maximum;
    public ConfigFieldKind Kind { get; }
    public IReadOnlyList<ConfigFieldDescriptor> Children { get; }
    public IReadOnlyList<ConfigOption> Options { get; }
    internal ConfigFieldAttribute Metadata { get; }
    internal PropertyInfo Property { get; }
    internal Type ValueType => Nullable.GetUnderlyingType(Property.PropertyType) ?? Property.PropertyType;
    internal bool IsActive(object owner) => Metadata.VisibleWhenProperty.Length == 0 ||
        Equals(owner.GetType().GetProperty(Metadata.VisibleWhenProperty)!.GetValue(owner)?.ToString(), Metadata.VisibleWhenValue);
}
