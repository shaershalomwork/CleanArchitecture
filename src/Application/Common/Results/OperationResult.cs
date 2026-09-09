namespace CleanArchitecture.Application.Common.Results;

public enum OperationStatus { Success, Warning, Error }
public enum IssueCategory { Validation, Business, Technical, Authorization }
public readonly record struct NoData;

public sealed record OperationIssue(string Code, string Message, IssueCategory Category, string CorrelationId, string? Target = null);

public interface IOperationResult
{
    OperationStatus Status { get; }
    IReadOnlyList<OperationIssue> Issues { get; }
}

public interface IOperationResult<TSelf> : IOperationResult where TSelf : IOperationResult<TSelf>
{
    static abstract TSelf Failure(IEnumerable<OperationIssue> issues);
}

public sealed class OperationResult<T> : IOperationResult<OperationResult<T>>
{
    private readonly T? _data;
    public OperationStatus Status { get; }
    public bool HasData => Status != OperationStatus.Error;
    public T Data => HasData ? _data! : throw new InvalidOperationException("An error result has no data.");
    public IReadOnlyList<OperationIssue> Issues { get; }

    private OperationResult(OperationStatus status, T? data, IEnumerable<OperationIssue> issues)
    {
        var copy = issues.ToArray();
        if (status != OperationStatus.Error && data is null)
            throw new ArgumentNullException(nameof(data));
        if ((status == OperationStatus.Success) != (copy.Length == 0))
            throw new ArgumentException("Success must have no issues; warning and error require issues.", nameof(issues));
        if (copy.Any(i => i is null || string.IsNullOrWhiteSpace(i.Code) || string.IsNullOrWhiteSpace(i.Message) || string.IsNullOrWhiteSpace(i.CorrelationId)))
            throw new ArgumentException("Issues require a code, safe message and correlation ID.", nameof(issues));
        Status = status;
        _data = data;
        Issues = Array.AsReadOnly(copy);
    }

    public static OperationResult<T> Success(T data) => new(OperationStatus.Success, data, []);
    public static OperationResult<T> Warning(T data, IEnumerable<OperationIssue> issues) => new(OperationStatus.Warning, data, issues);
    public static OperationResult<T> Failure(IEnumerable<OperationIssue> issues) => new(OperationStatus.Error, default, issues);
    public static OperationResult<T> Failure(OperationIssue issue) => Failure([issue]);

    public OperationResult<TOutput> Map<TOutput>(Func<T, TOutput> map) => Status switch
    {
        OperationStatus.Success => OperationResult<TOutput>.Success(map(Data)),
        OperationStatus.Warning => OperationResult<TOutput>.Warning(map(Data), Issues),
        _ => OperationResult<TOutput>.Failure(Issues)
    };
}
