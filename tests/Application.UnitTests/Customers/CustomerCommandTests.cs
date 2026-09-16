using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Results;
using CleanArchitecture.Application.Customers.Commands;
using CleanArchitecture.Application.Customers.Sources;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Customers;

public class CustomerCommandTests
{
    [TestCase("", "Name", false)]
    [TestCase("bad_id", "Name", false)]
    [TestCase("CUST-1", null, false)]
    [TestCase("CUST-1", " ", false)]
    [TestCase("CUST-1", "שלום", true)]
    public void ValidatesEveryCommand(string id, string? name, bool valid)
    {
        new CreateCustomerCommandValidator().Validate(new CreateCustomerCommand(id, name)).IsValid.ShouldBe(valid);
        new ReplaceCustomerCommandValidator().Validate(new ReplaceCustomerCommand(id, name)).IsValid.ShouldBe(valid);
        new PatchCustomerCommandValidator().Validate(new PatchCustomerCommand(id, name)).IsValid.ShouldBe(valid);
        new DeleteCustomerCommandValidator().Validate(new DeleteCustomerCommand(id)).IsValid.ShouldBe(id is "CUST-1");
    }

    [TestCase(50, 200, true)]
    [TestCase(51, 200, false)]
    [TestCase(50, 201, false)]
    public void EnforcesSourceContractLengths(int idLength, int nameLength, bool valid) =>
        new CreateCustomerCommandValidator().Validate(new CreateCustomerCommand(new('A', idLength), new('N', nameLength))).IsValid.ShouldBe(valid);

    [Test] public async Task PipelineStopsInvalidRequestsAndDispatchesValidCommandsWithCancellation()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddApplicationServices();
        var writer = new RecordingWriter();
        builder.Services.AddSingleton<ICustomerWriteSourceAdapter>(writer);
        builder.Services.AddSingleton<ICorrelationContext, Correlation>();
        using var host = builder.Build();
        var sender = host.Services.GetRequiredService<ISender>();
        var invalid = await sender.Send(new CreateCustomerCommand("bad_id", ""));
        writer.Calls.ShouldBeEmpty();
        invalid.Status.ShouldBe(OperationStatus.Error);
        invalid.Issues.ShouldAllBe(i => i.Category == IssueCategory.Validation && i.CorrelationId == "command-trace");
        invalid.Issues.Select(i => i.Code).ShouldContain("CUSTOMER.INVALID_ID");
        using var cancellation = new CancellationTokenSource();
        var create = await sender.Send(new CreateCustomerCommand("CUST-1", "Name"), cancellation.Token);
        var replace = await sender.Send(new ReplaceCustomerCommand("CUST-1", "Name"), cancellation.Token);
        var patch = await sender.Send(new PatchCustomerCommand("CUST-1", "Name"), cancellation.Token);
        var delete = await sender.Send(new DeleteCustomerCommand("CUST-1"), cancellation.Token);
        writer.Calls.ShouldBe(new[] { "create", "replace", "patch", "delete" });
        writer.Token.ShouldBe(cancellation.Token);
        create.Issues[0].Code.ShouldBe("CUSTOMER.CONFLICT");
        replace.Issues.ShouldBe(create.Issues);
        patch.Issues.ShouldBe(create.Issues);
        delete.Status.ShouldBe(OperationStatus.Success);
    }

    private sealed class Correlation : ICorrelationContext { public string Id => "command-trace"; }
    private sealed class RecordingWriter : ICustomerWriteSourceAdapter
    {
        public List<string> Calls { get; } = [];
        public CancellationToken Token { get; private set; }
        private Task<OperationResult<Customer>> Save(string operation, CancellationToken token)
        {
            Calls.Add(operation);
            Token = token;
            return Task.FromResult(OperationResult<Customer>.Failure(new OperationIssue("CUSTOMER.CONFLICT", "Already exists", IssueCategory.Business, "command-trace")));
        }
        public Task<OperationResult<Customer>> CreateAsync(string id, string name, CancellationToken token) => Save("create", token);
        public Task<OperationResult<Customer>> ReplaceAsync(string id, string name, CancellationToken token) => Save("replace", token);
        public Task<OperationResult<Customer>> PatchAsync(string id, string name, CancellationToken token) => Save("patch", token);
        public Task<OperationResult<NoData>> DeleteAsync(string id, CancellationToken token)
        {
            Calls.Add("delete"); Token = token;
            return Task.FromResult(OperationResult<NoData>.Success(new()));
        }
    }
}
