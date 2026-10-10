using System;
using System.Collections.Generic;
using Godot;

namespace FarmExchange.Logging;

/**
 * <summary>指定一次有界诊断的事件集合、真实实例及明确坐标范围。</summary>
 * <remarks>Anchors 绑定开始时的实例；Cells 只表示坐标筛选。请求由开始入口复制，之后修改原集合不影响采集。</remarks>
 */
public sealed record DiagnosticCaptureRequest(
    IReadOnlyList<string> EventNames,
    IReadOnlyList<Vector2I>? Anchors = null,
    IReadOnlyList<int>? OrderIds = null,
    IReadOnlyList<int>? WorkerNumbers = null,
    long DurationLimitMs = 120000,
    int EventLimit = 5000,
    IReadOnlyList<Vector2I>? Cells = null,
    bool IncludeGameEvents = false);
