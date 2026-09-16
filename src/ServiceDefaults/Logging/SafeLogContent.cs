using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CleanArchitecture.ServiceDefaults.Logging;

/// <summary>One data policy for stdout and OTLP. Extend templates only after reviewing their fields.</summary>
public static partial class SafeLogContent
{
    private static readonly HashSet<string> Templates =
    [
        "Use case {UseCase} completed with {Outcome} in {ElapsedMs} ms",
        "Source {Source} operation {Operation} failed with {Code}; correlation {CorrelationId}",
        "Unexpected failure in {UseCase}; code {Code}; exception {ExceptionType}; correlation {CorrelationId}",
        "Unhandled HTTP failure; code {Code}; exception {ExceptionType}; correlation {CorrelationId}",
        "HTTP request completed with {StatusCode} in {ElapsedMs} ms",
        "Application started. Press Ctrl+C to shut down.",
        "Application is shutting down..."
    ];

    public static (string Message, Dictionary<string, object?> Attributes) Project(
        IEnumerable<KeyValuePair<string, object?>>? state, Exception? exception, string? traceId)
    {
        var input = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is not null) foreach (var pair in state) input[pair.Key] = pair.Value;
        var fields = new Dictionary<string, object?>();
        var template = input.GetValueOrDefault("{OriginalFormat}") as string;
        var approved = template is not null && Templates.Contains(template);
        if (approved)
        {
            foreach (var (key, value) in input)
            {
                if (key is "UseCase" or "Outcome" or "Source" or "Operation" or "Code" or "ExceptionType" &&
                    value is string token && Token().IsMatch(token)) fields[key] = token;
                if (key == "ElapsedMs" && value is double or long or int) fields[key] = value;
                if (key == "StatusCode" && value is int code && code is >= 100 and <= 599) fields[key] = code;
            }
        }
        if (exception is not null) fields["ExceptionType"] = exception.GetType().Name;
        var correlation = traceId ?? (input.GetValueOrDefault("CorrelationId") is string id && Trace().IsMatch(id) ? id : null);
        fields["TraceId"] = traceId;
        fields["CorrelationId"] = correlation;
        var message = approved
            ? Placeholder().Replace(template!, match => Convert.ToString(fields.GetValueOrDefault(match.Groups[1].Value), CultureInfo.InvariantCulture) ?? "[unavailable]")
            : "Log content suppressed by data policy.";
        return (message, fields);
    }

    public static string? CurrentTraceId => Activity.Current is { } activity && activity.TraceId != default
        ? activity.TraceId.ToHexString() : null;

    [GeneratedRegex("^[A-Za-z0-9_.+`-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
    [GeneratedRegex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Trace();
    [GeneratedRegex("\\{([A-Za-z]+)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
