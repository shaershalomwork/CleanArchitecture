using CleanArchitecture.Infrastructure.Execution;
using CleanArchitecture.Infrastructure.IntegrationTests.Execution;
namespace CleanArchitecture.Database.Tests.Sources;
public sealed class OracleExecutionTests
{
    [TestCase(1013, "TIMEOUT", true)]
    [TestCase(1017, "AUTHENTICATION_FAILED", false)]
    [TestCase(1031, "AUTHENTICATION_FAILED", false)]
    [TestCase(3113, "UNAVAILABLE", true)]
    [TestCase(12541, "UNAVAILABLE", true)]
    [TestCase(12154, "UNAVAILABLE", false)]
    [TestCase(942, "INVALID_RESPONSE", false)]
    public async Task OracleErrorsUseSafeCodesAndNeverRetryWrites(int number, string suffix, bool transient)
    {
        var classified = OracleExecution.Classify(number);
        classified.Suffix.ShouldBe(suffix);
        classified.Transient.ShouldBe(transient);
        var calls = 0;
        var result = await SourceExecutorTests.Create().ExecuteAsync<int>(new("CUSTOMER", "WriteOracle"), _ =>
        {
            calls++;
            throw classified;
        }, default);
        calls.ShouldBe(1);
        result.Issues[0].Code.ShouldBe("CUSTOMER." + (suffix is "TIMEOUT" or "UNAVAILABLE" ? "OUTCOME_UNKNOWN" : suffix));
        result.Issues[0].Message.ShouldNotContain("ORA-");
    }
}
