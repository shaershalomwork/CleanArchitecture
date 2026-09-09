using CleanArchitecture.Application.Common.Results;
using NUnit.Framework;
using Shouldly;
namespace CleanArchitecture.Application.UnitTests.Common.Results;
public class OperationResultTests
{
    private static readonly OperationIssue Issue = new("TEST.FAILURE", "Safe message", IssueCategory.Technical, "trace");
    [Test] public void ErrorDoesNotExposeDefaultData()
    {
        var result = OperationResult<int>.Failure(Issue);
        result.HasData.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => _ = result.Data);
    }
    [Test] public void FactoriesEnforceIntegrity()
    {
        Should.Throw<ArgumentNullException>(() => OperationResult<string>.Success(null!));
        Should.Throw<ArgumentException>(() => OperationResult<int>.Warning(0, []));
        Should.Throw<ArgumentException>(() => OperationResult<int>.Failure(Array.Empty<OperationIssue>()));
        OperationResult<int>.Success(0).Data.ShouldBe(0);
        OperationResult<int[]>.Success([]).Data.ShouldBeEmpty();
    }
    [Test] public void MappingPreservesIssuesAndDoesNotInvokeMapperForFailure()
    {
        OperationResult<int>.Failure(Issue).Map<string>(_ => throw new Exception()).Issues.ShouldBe([Issue]);
        var mapped = OperationResult<int>.Warning(2, [Issue]).Map(x => x.ToString());
        mapped.Status.ShouldBe(OperationStatus.Warning);
        mapped.Data.ShouldBe("2");
        mapped.Issues.ShouldBe([Issue]);
    }
}
