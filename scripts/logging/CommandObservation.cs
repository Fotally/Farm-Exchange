using System;
using System.Collections.Generic;
using FarmExchange.Gameplay;
using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>共用命令观察，唯一维护收到、一次终结与异常关联。</summary>
 * <remarks>投影回调仅生成日志事实；业务执行和异常传播始终由经营模块负责。</remarks>
 */
internal sealed class CommandObservation
{
    private readonly GameLog _context;
    private readonly long _id;
    private readonly CommandDescription _description;
    private readonly CommandOrigin _origin;
    private bool _finished;

    internal CommandObservation(GameLog context, long id, CommandDescription description, CommandOrigin origin)
    {
        _context = context;
        _id = id;
        _description = description;
        _origin = origin;
    }

    internal void Received(Dictionary<string, object?> arguments, Dictionary<string, object?>? metadata = null)
    {
        var fields = Fields();
        fields["CommandArguments"] = arguments;
        if (metadata != null)
            foreach (var field in metadata) fields[field.Key] = field.Value;
        _context.Output.Submit(new(5, "CommandReceived", _description.Source), _description.ReceivedMessage, fields);
    }

    internal void Complete(Func<Dictionary<string, object?>, CommandOutcome> project)
    {
        if (_finished) return;
        _finished = true;
        _context.Observe(() =>
        {
            var fields = Fields();
            CommandOutcome outcome = project(fields);
            if (outcome.Event != null)
                _context.Output.Submit(outcome.Event, outcome.Message!, fields);
            var finished = Fields();
            finished["CommandStatus"] = outcome.Success ? "Succeeded" : "Rejected";
            finished["RejectionReason"] = outcome.RejectionReason;
            _context.Output.Submit(new(6, "CommandFinished", _description.Source), _description.FinishedMessage, finished);
        });
        _context.Production?.CheckBoundary();
    }

    internal void Faulted(Exception error)
    {
        if (_finished) return;
        _finished = true;
        _context.Observe(() =>
        {
            var fields = Fields();
            fields["ExceptionType"] = error.GetType().FullName;
            fields["Exception"] = error.ToString();
            _context.Output.Submit(new(8, "BusinessException", _description.Source, LogLevel.Error),
                _description.FaultMessage, fields);
            var finished = Fields();
            finished["CommandStatus"] = "Faulted";
            _context.Output.Submit(new(6, "CommandFinished", _description.Source), _description.FaultFinishedMessage, finished);
        });
        _context.Production?.CheckBoundary();
    }

    private Dictionary<string, object?> Fields()
    {
        var fields = _context.Context("Command");
        fields["CommandId"] = _id;
        fields["CommandName"] = _description.Name;
        fields["CommandOrigin"] = _origin.ToString();
        return fields;
    }
}

internal sealed record CommandDescription(string Name, string Source, string ReceivedMessage,
    string FinishedMessage, string FaultMessage, string FaultFinishedMessage);

internal readonly record struct CommandOutcome(LogEventDescriptor? Event, string? Message,
    bool Success, string? RejectionReason);
