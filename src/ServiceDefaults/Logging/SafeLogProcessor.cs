using OpenTelemetry;
using OpenTelemetry.Logs;

namespace CleanArchitecture.ServiceDefaults.Logging;

public sealed class SafeLogProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        var content = SafeLogContent.Project(data.Attributes, data.Exception,
            data.TraceId == default ? null : data.TraceId.ToHexString());
        data.Body = content.Message;
        data.FormattedMessage = content.Message;
        data.Exception = null;
        data.TraceState = null;
        data.Attributes = content.Attributes.ToArray();
    }
}
