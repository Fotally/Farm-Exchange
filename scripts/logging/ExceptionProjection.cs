using System;
using System.Collections.Generic;

namespace FarmExchange.Logging;

/**
 * <summary>共用原异常字段投影；调用方在既有日志故障隔离内使用。</summary>
 */
internal static class ExceptionProjection
{
    internal static void Add(Dictionary<string, object?> fields, Exception error)
    {
        fields["ExceptionType"] = error.GetType().FullName;
        fields["Exception"] = error.ToString();
    }
}
