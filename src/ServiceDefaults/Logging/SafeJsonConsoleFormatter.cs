using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace CleanArchitecture.ServiceDefaults.Logging;

public sealed class SafeJsonConsoleFormatter(ServiceLogIdentity identity) : ConsoleFormatter("safe-json")
{
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var content = SafeLogContent.Project(logEntry.State as IEnumerable<KeyValuePair<string, object?>>,
            logEntry.Exception, SafeLogContent.CurrentTraceId);
        // Do not serialize arbitrary scopes, state objects, exception messages, or stack traces.
        textWriter.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Timestamp"] = DateTimeOffset.UtcNow, ["Level"] = logEntry.LogLevel.ToString(),
            ["Category"] = logEntry.Category, ["EventId"] = logEntry.EventId.Id,
            ["service.name"] = identity.Name, ["service.version"] = identity.Version,
            ["service.instance.id"] = identity.InstanceId, ["deployment.environment.name"] = identity.Environment,
            ["TraceId"] = content.Attributes["TraceId"], ["CorrelationId"] = content.Attributes["CorrelationId"],
            ["Message"] = content.Message, ["Attributes"] = content.Attributes
        }));
    }
}
