using CleanArchitecture.Shared;
var builder = DistributedApplication.CreateBuilder(args);
var web = builder.AddProject<Projects.Web>(Services.WebApi)
    .WithExternalHttpEndpoints()
    .WithAspNetCoreEnvironment();
#if (UseAngular)
if (builder.ExecutionContext.IsRunMode)
    builder.AddJavaScriptApp(Services.WebFrontend, "../Web/ClientApp")
        .WithRunScript("start").WithReference(web).WaitFor(web)
        .WithHttpEndpoint(env: "PORT").WithExternalHttpEndpoints();
#endif
builder.Build().Run();
