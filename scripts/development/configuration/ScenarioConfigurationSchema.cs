using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FarmExchange.Gameplay;
using FarmExchange.Time;

namespace FarmExchange.Development;

/**
 * <summary>扫描已登记的序列化类型，统一严格JSON读取与表单基础校验。</summary>
 */
public sealed class ScenarioConfigurationSchema
{
    public static ScenarioConfigurationSchema BuyProcessSell { get; } = new(typeof(ScenarioEditableConfiguration));
    private readonly Type _type;
    public IReadOnlyList<ConfigFieldDescriptor> Fields { get; }

    /**
     * <summary>扫描具备中文元数据的JSON字段；不支持的形状立即拒绝。</summary>
     * <param name="type">已登记的具体配置类型。</param>
     */
    public ScenarioConfigurationSchema(Type type)
    {
        _type = type;
        Fields = Scan(type, new HashSet<Type>());
    }

    /**
     * <summary>严格解码并构造独立的编辑值，拒绝未知、重复、缺失及错误类型。</summary>
     * <param name="bytes">UTF-8原文件字节。</param>
     * <returns>独立配置对象；格式错误抛ScenarioConfigurationException。</returns>
     */
    public object Read(byte[] bytes)
    {
        try
        {
            string json = new UTF8Encoding(false, true).GetString(bytes);
            if (json.Length > 0 && json[0] == '\uFEFF') json = json[1..];
            using JsonDocument document = JsonDocument.Parse(json);
            object value = ReadObject(document.RootElement, _type, Fields, "");
            ValidateSequence(value);
            return value;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw new ScenarioConfigurationException("配置编码或JSON格式错误：" + exception.Message, exception);
        }
    }

    /**
     * <summary>按同一字段描述校验并输出UTF-8；条件隐藏字段不输出。</summary>
     * <param name="value">本描述对应的编辑对象。</param>
     * <returns>可由严格读取器原样读取的字节。</returns>
     */
    public byte[] Write(object value)
    {
        if (value.GetType() != _type) throw Error("", "配置类型不匹配");
        ValidateSequence(value);
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            WriteObject(writer, value, Fields, "");
        return stream.ToArray();
    }

    internal static uint ParseGameDate(string date)
    {
        if (date.Length != 8 || date[2] != '-' || date[5] != '-' ||
            new[] { 0, 1, 3, 4, 6, 7 }.Any(index => date[index] < '0' || date[index] > '9'))
            throw Error("", "须为游戏yy-MM-dd日期");
        int year = int.Parse(date[..2], CultureInfo.InvariantCulture);
        int month = int.Parse(date[3..5], CultureInfo.InvariantCulture);
        int day = int.Parse(date[6..8], CultureInfo.InvariantCulture);
        if (year < 1 || month < 1 || month > 12 || day < 1 || day > 28)
            throw Error("", "游戏日期年为01至99、月为01至12、日为01至28");
        long days = (year - 1L) * 336 + (month - 1L) * 28 + day - 1L;
        return (uint)((days * GameTimeUnits.PerDay + GameTimeUnits.PerSecond - 1) / GameTimeUnits.PerSecond);
    }

    private static void ValidateSequence(object value)
    {
        if (value is not ScenarioEditableConfiguration configuration) return;
        uint previous = 0;
        for (int index = 0; index < configuration.Execution.TimePlan.Count; index++)
        {
            uint end;
            try { end = ParseGameDate(configuration.Execution.TimePlan[index].EndDate); }
            catch (ScenarioConfigurationException error) { throw Error("execution.timePlan[" + index + "].endDate", error.Message); }
            if (end <= previous) throw Error("execution.timePlan[" + index + "].endDate", "时间区间终点须严格递增");
            previous = end;
        }
    }

    internal static object ParseInput(ConfigFieldDescriptor field, string text, string path)
    {
        Type type = field.ValueType;
        object value;
        if (type == typeof(string)) value = text;
        else if (type == typeof(int) && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int integer)) value = integer;
        else if (type == typeof(uint) && uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out uint unsigned)) value = unsigned;
        else if (type == typeof(double) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) value = number;
        else if (type == typeof(bool) && bool.TryParse(text, out bool boolean)) value = boolean;
        else if (type.IsEnum && Enum.GetNames(type).Contains(text, StringComparer.Ordinal) &&
            Enum.TryParse(type, text, false, out object? option) && Enum.IsDefined(type, option!)) value = option!;
        else throw Error(path, "输入类型无效");
        ValidateScalar(field, value, path);
        return value;
    }

    private static IReadOnlyList<ConfigFieldDescriptor> Scan(Type type, HashSet<Type> ancestors)
    {
        if (!type.IsClass || !type.IsSealed || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null || !ancestors.Add(type))
            throw Error(type.Name, "不支持抽象、多态或递归配置定义");
        var fields = new List<ConfigFieldDescriptor>();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            JsonPropertyNameAttribute? serialized = property.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (serialized == null) continue;
            ConfigFieldAttribute metadata = property.GetCustomAttribute<ConfigFieldAttribute>() ?? throw Error(serialized.Name, "序列化字段缺少中文元数据");
            if (!property.CanRead || property.SetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0 || fields.Any(field => field.Name == serialized.Name))
                throw Error(serialized.Name, "字段不可读写或序列化名重复");
            if (metadata.VisibleWhenProperty.Length > 0 && type.GetProperty(metadata.VisibleWhenProperty)?.PropertyType != typeof(string))
                throw Error(serialized.Name, "条件字段须引用同一对象的字符串字段");
            Type valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            ConfigFieldKind kind;
            IReadOnlyList<ConfigFieldDescriptor> children = Array.Empty<ConfigFieldDescriptor>();
            var options = new List<ConfigOption>();
            if (valueType == typeof(string)) kind = ConfigFieldKind.Text;
            else if (valueType == typeof(int)) kind = ConfigFieldKind.Integer;
            else if (valueType == typeof(uint)) kind = ConfigFieldKind.UnsignedInteger;
            else if (valueType == typeof(double)) kind = ConfigFieldKind.Number;
            else if (valueType == typeof(bool)) kind = ConfigFieldKind.Boolean;
            else if (valueType.IsEnum)
            {
                kind = ConfigFieldKind.Choice;
                if (metadata.Values.Length == 0) throw Error(serialized.Name, "枚举须显式声明允许成员及中文标签");
            }
            else if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(List<>))
            {
                kind = ConfigFieldKind.Array;
                children = Scan(valueType.GetGenericArguments()[0], ancestors);
            }
            else
            {
                kind = ConfigFieldKind.Object;
                children = Scan(valueType, ancestors);
            }
            if (metadata.Editor == "game-date" && valueType == typeof(string)) kind = ConfigFieldKind.GameDate;
            else if (metadata.Editor == "rate" && valueType == typeof(double)) kind = ConfigFieldKind.Rate;
            else if (metadata.Editor == "raw-commodity" && valueType == typeof(string))
            {
                kind = ConfigFieldKind.Choice;
                options.AddRange(FarmGame.Crops.Select(crop => new ConfigOption(crop.Kind + ".Raw", crop.CropName)));
            }
            else if (metadata.Editor.Length > 0) throw Error(serialized.Name, "编辑方式与字段类型不匹配或不受支持");
            if (metadata.Values.Length > 0 || metadata.Labels.Length > 0)
            {
                if ((valueType != typeof(string) && !valueType.IsEnum) || metadata.Values.Length != metadata.Labels.Length ||
                    metadata.Values.Any(string.IsNullOrWhiteSpace) || metadata.Labels.Any(string.IsNullOrWhiteSpace) ||
                    metadata.Values.Distinct(StringComparer.Ordinal).Count() != metadata.Values.Length)
                    throw Error(serialized.Name, "选项值与中文名须一一对应");
                if (valueType.IsEnum && metadata.Values.Any(value => !Enum.GetNames(valueType).Contains(value, StringComparer.Ordinal)))
                    throw Error(serialized.Name, "枚举选项须为已声明的正式成员名");
                kind = ConfigFieldKind.Choice;
                options.AddRange(metadata.Values.Select((value, index) => new ConfigOption(value, metadata.Labels[index])));
            }
            fields.Add(new ConfigFieldDescriptor(serialized.Name, metadata, kind, property, children, options.AsReadOnly()));
        }
        ancestors.Remove(type);
        if (fields.Count == 0) throw Error(type.Name, "不支持字典或没有序列化字段的配置类型");
        foreach (ConfigFieldDescriptor field in fields)
            if (field.Metadata.VisibleWhenProperty.Length > 0 &&
                !fields.Any(target => target.Property.Name == field.Metadata.VisibleWhenProperty && target.Metadata.VisibleWhenProperty.Length == 0))
                throw Error(field.Name, "条件须引用同一对象中已登记的非条件字符串字段");
        return fields.AsReadOnly();
    }

    private static object ReadObject(JsonElement element, Type type, IReadOnlyList<ConfigFieldDescriptor> fields, string path)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Error(path, "须为对象");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw Error(Join(path, property.Name), "重复字段");
            if (!fields.Any(field => field.Name == property.Name)) throw Error(Join(path, property.Name), "未知字段");
        }
        object owner = Activator.CreateInstance(type)!;
        foreach (ConfigFieldDescriptor field in fields.OrderBy(field => field.Metadata.VisibleWhenProperty.Length > 0))
        {
            string fieldPath = Join(path, field.Name);
            if (!field.IsActive(owner))
            {
                if (names.Contains(field.Name)) throw Error(fieldPath, "当前条件不接受此字段");
                continue;
            }
            if (!element.TryGetProperty(field.Name, out JsonElement value)) throw Error(fieldPath, "缺少字段");
            object parsed;
            if (field.Kind == ConfigFieldKind.Object) parsed = ReadObject(value, field.ValueType, field.Children, fieldPath);
            else if (field.Kind == ConfigFieldKind.Array)
            {
                if (value.ValueKind != JsonValueKind.Array) throw Error(fieldPath, "须为数组");
                var list = (IList)Activator.CreateInstance(field.ValueType)!;
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                    list.Add(ReadObject(item, field.ValueType.GetGenericArguments()[0], field.Children, fieldPath + "[" + index++ + "]"));
                if (list.Count < field.Minimum || list.Count > field.Maximum) throw Error(fieldPath, "数组条目数量超出允许范围");
                parsed = list;
            }
            else
            {
                Type scalar = field.ValueType;
                if ((scalar == typeof(string) || scalar.IsEnum) && value.ValueKind == JsonValueKind.String) parsed = ParseInput(field, value.GetString()!, fieldPath);
                else if (scalar == typeof(int) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int integer)) parsed = integer;
                else if (scalar == typeof(uint) && value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint unsigned)) parsed = unsigned;
                else if (scalar == typeof(double) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number)) parsed = number;
                else if (scalar == typeof(bool) && value.ValueKind is JsonValueKind.True or JsonValueKind.False) parsed = value.GetBoolean();
                else throw Error(fieldPath, "JSON类型或数值容量无效");
                ValidateScalar(field, parsed, fieldPath);
            }
            field.Property.SetValue(owner, parsed);
        }
        return owner;
    }

    private static void WriteObject(Utf8JsonWriter writer, object owner, IReadOnlyList<ConfigFieldDescriptor> fields, string path)
    {
        writer.WriteStartObject();
        foreach (ConfigFieldDescriptor field in fields)
        {
            if (!field.IsActive(owner)) continue;
            string fieldPath = Join(path, field.Name);
            object value = field.Property.GetValue(owner) ?? throw Error(fieldPath, "字段不能为空");
            writer.WritePropertyName(field.Name);
            if (field.Kind == ConfigFieldKind.Object) WriteObject(writer, value, field.Children, fieldPath);
            else if (field.Kind == ConfigFieldKind.Array)
            {
                var list = (IList)value;
                if (list.Count < field.Minimum || list.Count > field.Maximum) throw Error(fieldPath, "数组条目数量超出允许范围");
                writer.WriteStartArray();
                for (int index = 0; index < list.Count; index++)
                    WriteObject(writer, list[index]!, field.Children, fieldPath + "[" + index + "]");
                writer.WriteEndArray();
            }
            else
            {
                ValidateScalar(field, value, fieldPath);
                if (value is int integer) writer.WriteNumberValue(integer);
                else if (value is uint unsigned) writer.WriteNumberValue(unsigned);
                else if (value is double number) writer.WriteNumberValue(number);
                else if (value is bool boolean) writer.WriteBooleanValue(boolean);
                else writer.WriteStringValue(value.ToString());
            }
        }
        writer.WriteEndObject();
    }

    private static void ValidateScalar(ConfigFieldDescriptor field, object value, string path)
    {
        if (value is string text && string.IsNullOrWhiteSpace(text)) throw Error(path, "不能为空");
        if (value is int integer && (integer < field.Minimum || integer > field.Maximum) ||
            value is uint unsigned && (unsigned < field.Minimum || unsigned > field.Maximum))
            throw Error(path, "整数超出允许范围");
        if (value is double number && !double.IsFinite(number)) throw Error(path, "须为有限数值");
        if (field.Kind == ConfigFieldKind.Rate && !SimulationDriver.IsDevelopmentRateAllowed((double)value))
            throw Error(path, "须为0.5、1、2或1～16的整数倍率");
        if (field.Options.Count > 0 && !field.Options.Any(option => option.Value == value.ToString())) throw Error(path, "不是合法选项");
        if (field.Kind == ConfigFieldKind.GameDate)
            try { ParseGameDate((string)value); }
            catch (ScenarioConfigurationException exception) { throw Error(path, exception.Message); }
    }

    internal static string Join(string parent, string child) => parent.Length == 0 ? child : parent + "." + child;
    internal static ScenarioConfigurationException Error(string path, string message) => new((path.Length > 0 ? path + "：" : "") + message);
}
