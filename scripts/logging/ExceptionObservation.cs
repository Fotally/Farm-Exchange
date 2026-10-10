using System;
using System.Collections.Generic;

namespace FarmExchange.Logging;

/**
 * <summary>保存本次观察与其活跃父调用已接收的异常，不保存跨调用异常表。</summary>
 */
internal sealed class ExceptionObservation
{
    private readonly GameLog _context;
    private HashSet<Exception>? _observed;
    private bool _closed;

    internal ExceptionObservation(GameLog context, ExceptionObservation? parent)
    { _context = context; Parent = parent; }

    internal ExceptionObservation? Parent { get; }
    internal bool IsClosed => _closed;

    internal void Faulted(Exception error, LogEventDescriptor description, string message,
        Func<Dictionary<string, object?>> fields)
    {
        if (_closed || _observed?.Contains(error) == true) return;
        // 提交前标记；投影或文件失败仍是已观察，外层不得换上下文重试。
        for (ExceptionObservation? current = this; current != null; current = current.Parent)
        {
            if (current._closed) continue;
            (current._observed ??= new(ReferenceEqualityComparer.Instance)).Add(error);
        }
        _context.Observe(() =>
        {
            var projected = fields();
            ExceptionProjection.Add(projected, error);
            _context.Output.Submit(description, message, projected);
        });
    }

    internal void Close()
    {
        _closed = true;
        _observed = null;
    }
}
