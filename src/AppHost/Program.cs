using CleanArchitecture.Shared;
using CleanArchitecture.AppHost;
var builder = DistributedApplication.CreateBuilder(args);
var web = builder.AddProject<Projects.Web>(Services.WebApi)
    .WithExternalHttpEndpoints()
    .WithAspNetCoreEnvironment();
using var logging = DevelopmentLogging.Configure(builder, web);
#if (UseAngular)
if (builder.ExecutionContext.IsRunMode)
    builder.AddJavaScriptApp(Services.WebFrontend, "../Web/ClientApp")
        .WithRunScript("start").WithReference(web).WaitFor(web).WithDeveloperCertificateTrust(true)
        .WithHttpEndpoint(env: "PORT").WithExternalHttpEndpoints();
#endif
builder.Build().Run();
