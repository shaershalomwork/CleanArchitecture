using System.ComponentModel.DataAnnotations;
namespace CleanArchitecture.Infrastructure.Configuration;

public enum SourceMode { Live, Fake }
public enum CustomerDatabaseProvider { SqlServer, SQLite }
public sealed class CustomerRegistryOptions
{
    public SourceMode Mode { get; set; } = SourceMode.Live;
    public CustomerDatabaseProvider Provider { get; set; } = CustomerDatabaseProvider.SqlServer;
    public string ConnectionName { get; set; } = "CustomerRegistry";
}
public sealed class BillingOptions
{
    public SourceMode Mode { get; set; } = SourceMode.Live;
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
}
public sealed class SourceExecutionOptions
{
    [Range(0.1, 60)] public double AttemptTimeoutSeconds { get; set; } = 3;
    [Range(0.1, 120)] public double TotalTimeoutSeconds { get; set; } = 8;
    [Range(0, 3)] public int ReadRetries { get; set; } = 1;
    [Range(0, 5000)] public int RetryDelayMilliseconds { get; set; } = 200;
    [Range(0.01, 1)] public double FailureRatio { get; set; } = 0.5;
    [Range(2, 1000)] public int MinimumThroughput { get; set; } = 10;
    [Range(1, 120)] public double SamplingSeconds { get; set; } = 30;
    [Range(1, 120)] public double BreakSeconds { get; set; } = 15;
}
