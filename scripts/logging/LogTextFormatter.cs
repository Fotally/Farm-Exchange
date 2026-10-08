using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace FarmExchange.Logging;

internal sealed class LogTextFormatter : ITextFormatter
{
    private static readonly JsonSerializerOptions StringOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly HashSet<string> HeaderFields = new()
    {
        "SchemaVersion", "SessionId", "Sequence", "UptimeMs", "SourceContext", "EventName", "Message", "EventId", "RuntimeIncluded",
    };

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var truncated = ReadProjectedTruncation(logEvent);
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        Header(logEvent, text, StringValue(logEvent, "EventName"), StringValue(logEvent, "Message"), truncated);
        text.Write(" | { ");
        bool first = true;
        foreach (var property in logEvent.Properties)
        {
            if (HeaderFields.Contains(property.Key) || property.Key is "Truncated" or "TruncatedFields" or "TruncatedOriginalCounts") continue;
            if (!first) text.Write(", ");
            first = false;
            text.Write(property.Key + ": ");
            Value(property.Value, property.Key, text, truncated);
        }
        if (truncated.Count > 0)
        {
            if (!first) text.Write(", ");
            text.Write("Truncated: true, TruncatedFields: [");
            text.Write(string.Join(", ", truncated.Keys.Select(Quote)));
            text.Write("], TruncatedOriginalCounts: { ");
            bool firstCount = true;
            foreach (var field in truncated)
            {
                if (!firstCount) text.Write(", ");
                firstCount = false;
                text.Write(field.Key + ": { ");
                bool firstDimension = true;
                WriteCount("ScalarCount", field.Value.ScalarCount);
                WriteCount("Utf8Bytes", field.Value.Utf8Bytes);
                WriteCount("LineCount", field.Value.LineCount);
                WriteCount("ItemCount", field.Value.ItemCount);
                text.Write(" }");

                void WriteCount(string name, long? value)
                {
                    if (!value.HasValue) return;
                    if (!firstDimension) text.Write(", ");
                    firstDimension = false;
                    text.Write(name + ": " + value.Value.ToString(CultureInfo.InvariantCulture));
                }
            }
            text.Write(" }");
        }
        text.Write(" }");
        string line = text.ToString();
        if (Encoding.UTF8.GetByteCount(line) > 32 * 1024)
        {
            using var rejected = new StringWriter(CultureInfo.InvariantCulture);
            Header(logEvent, rejected, "EventPayloadRejected", "原事件无法完整输出", new(), LogEventLevel.Warning);
            rejected.Write(" | { OriginalEventName: " + Quote(StringValue(logEvent, "EventName")));
            foreach (string key in new[] { "GameInstanceId", "SimulationSeconds", "CommandId" })
                if (logEvent.Properties.TryGetValue(key, out var value))
                {
                    rejected.Write(", " + key + ": ");
                    Value(value, key, rejected, new());
                }
            rejected.Write(", RejectionReason: \"EventSizeLimit\" }");
            line = rejected.ToString();
        }
        output.WriteLine(line);
    }

    private static Dictionary<string, OriginalCounts> ReadProjectedTruncation(LogEvent entry)
    {
        var result = new Dictionary<string, OriginalCounts>();
        if (!entry.Properties.TryGetValue("TruncatedOriginalCounts", out var projected)) return result;
        foreach (var field in ((DictionaryValue)projected).Elements)
        {
            var counts = ((DictionaryValue)field.Value).Elements;
            result.Add((string)field.Key.Value!, new(Read("ScalarCount"), Read("Utf8Bytes"), Read("LineCount"), Read("ItemCount")));
            long? Read(string name) => counts.TryGetValue(new ScalarValue(name), out var count)
                ? Convert.ToInt64(((ScalarValue)count).Value, CultureInfo.InvariantCulture) : null;
        }
        return result;
    }

    private static void Header(LogEvent entry, TextWriter writer, string eventName, string message,
        Dictionary<string, OriginalCounts> truncated,
        LogEventLevel? overrideLevel = null)
    {
        string level = (overrideLevel ?? entry.Level) switch
        {
            LogEventLevel.Verbose => "VRB",
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            _ => "FTL",
        };
        writer.Write(entry.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
        writer.Write(" [" + level + "]");
        foreach (string key in new[] { "SchemaVersion", "SessionId", "Sequence", "UptimeMs", "SourceContext" })
            writer.Write(" " + key + "=" + ScalarText(entry.Properties[key]));
        writer.Write(" EventName=" + eventName + " " + Quote(Limit(message, "Message", 256, truncated))[1..^1]);
    }

    private static string StringValue(LogEvent entry, string key) => (string)((ScalarValue)entry.Properties[key]).Value!;
    private static string ScalarText(LogEventPropertyValue value) =>
        Convert.ToString(((ScalarValue)value).Value, CultureInfo.InvariantCulture)!;
    private static string Quote(string value) => JsonSerializer.Serialize(value, StringOptions);

    private static void Value(LogEventPropertyValue value, string path, TextWriter writer, Dictionary<string, OriginalCounts> truncated)
    {
        switch (value)
        {
            case ScalarValue scalar when scalar.Value == null: writer.Write("null"); break;
            case ScalarValue scalar when scalar.Value is string text:
                if (path == "Exception") text = LimitException(text, truncated);
                else text = Limit(text, path, 512, truncated);
                writer.Write(Quote(text));
                break;
            case ScalarValue scalar when scalar.Value is bool boolean: writer.Write(boolean ? "true" : "false"); break;
            case ScalarValue scalar: writer.Write(Convert.ToString(scalar.Value, CultureInfo.InvariantCulture)); break;
            case SequenceValue sequence:
                writer.Write("[");
                for (int i = 0; i < sequence.Elements.Count; i++)
                {
                    if (i > 0) writer.Write(", ");
                    Value(sequence.Elements[i], path + "[" + i + "]", writer, truncated);
                }
                writer.Write("]");
                break;
            case DictionaryValue dictionary:
                writer.Write("{ ");
                int index = 0;
                foreach (var pair in dictionary.Elements)
                {
                    if (index++ > 0) writer.Write(", ");
                    string name = Convert.ToString(pair.Key.Value, CultureInfo.InvariantCulture)!;
                    writer.Write(name + ": ");
                    Value(pair.Value, path + "." + name, writer, truncated);
                }
                writer.Write(" }");
                break;
            case StructureValue structure:
                writer.Write("{ ");
                for (int i = 0; i < structure.Properties.Count; i++)
                {
                    if (i > 0) writer.Write(", ");
                    var property = structure.Properties[i];
                    writer.Write(property.Name + ": ");
                    Value(property.Value, path + "." + property.Name, writer, truncated);
                }
                writer.Write(" }");
                break;
            default: throw new InvalidOperationException("未登记的日志值类型");
        }
    }

    private static string Limit(string text, string path, int scalars, Dictionary<string, OriginalCounts> truncated)
    {
        int count = 0, length = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (count++ == scalars)
            {
                truncated[path] = CountOriginal(text, exception: false);
                return text[..length];
            }
            length += rune.Utf16SequenceLength;
        }
        return text;
    }

    private static string LimitException(string text, Dictionary<string, OriginalCounts> truncated)
    {
        int lines = 1, bytes = 0, length = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 16 * 1024 || rune.Value == '\n' && lines++ == 64)
            {
                truncated["Exception"] = CountOriginal(text, exception: true);
                return text[..length];
            }
            bytes += rune.Utf8SequenceLength;
            length += rune.Utf16SequenceLength;
        }
        return text;
    }

    private static OriginalCounts CountOriginal(string text, bool exception)
    {
        long scalars = 0, bytes = 0, lines = 1;
        foreach (Rune rune in text.EnumerateRunes())
        {
            scalars++;
            if (!exception) continue;
            bytes += rune.Utf8SequenceLength;
            if (rune.Value == '\n') lines++;
        }
        return exception ? new(scalars, bytes, lines) : new(scalars, null, null);
    }

    private readonly record struct OriginalCounts(long? ScalarCount, long? Utf8Bytes, long? LineCount, long? ItemCount = null);
}
