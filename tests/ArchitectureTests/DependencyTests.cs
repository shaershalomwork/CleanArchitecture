using System.Reflection;
using CleanArchitecture.Application.Common.Results;
using CleanArchitecture.Application.Customers.Queries.GetCustomerOverview;
using CleanArchitecture.Infrastructure.Execution;
using NUnit.Framework;
using Shouldly;
namespace CleanArchitecture.ArchitectureTests;
public class DependencyTests
{
    [Test] public void InnerAssembliesDoNotReferenceTransports()
    {
        var application = typeof(OperationResult<>).Assembly;
        var forbidden = new[] { "EntityFramework", "Dapper", "SqlClient", "Sqlite", "Oracle", "Infrastructure", "System.Net.Http", "AspNetCore" };
        application.GetReferencedAssemblies().ShouldNotContain(a => forbidden.Any(f => a.Name!.Contains(f, StringComparison.Ordinal)));
        var domain = Assembly.Load(application.GetReferencedAssemblies().FirstOrDefault(a => a.Name!.EndsWith(".Domain", StringComparison.Ordinal))
            ?? new AssemblyName(application.GetName().Name!.Replace(".Application", ".Domain", StringComparison.Ordinal)));
        domain.GetReferencedAssemblies().ShouldNotContain(a => a.Name!.Contains("MediatR", StringComparison.Ordinal));
        domain.GetReferencedAssemblies().ShouldNotContain(a => forbidden.Any(f => a.Name!.Contains(f, StringComparison.Ordinal)));
    }
    [Test] public void HandlersAndAdaptersCannotNestMediatorDispatch()
    {
        var types = typeof(GetCustomerOverviewQueryHandler).Assembly.GetTypes()
            .Concat(typeof(SourceExecutor).Assembly.GetTypes())
            .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal) || t.Name.EndsWith("Adapter", StringComparison.Ordinal));
        foreach (var type in types)
            type.GetConstructors().SelectMany(c => c.GetParameters()).ShouldNotContain(p =>
                p.ParameterType == typeof(MediatR.ISender) || p.ParameterType == typeof(MediatR.IMediator));
    }
}
