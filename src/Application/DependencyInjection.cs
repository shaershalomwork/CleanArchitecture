using System.Reflection;
using CleanArchitecture.Application.Common.Behaviours;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;
public static class DependencyInjection
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        builder.Services.AddMediatR(cfg =>
        {
            cfg.LicenseKey = builder.Configuration["MediatR:LicenseKey"];
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            cfg.AddOpenBehavior(typeof(LoggingBehaviour<,>));
            cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
        });
    }
}
