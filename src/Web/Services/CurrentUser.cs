using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.Extensions.Options;
namespace CleanArchitecture.Web.Services;
public sealed class CurrentUser(IHttpContextAccessor accessor, IOptions<Authentication.AuthenticationOptions> options) : IUser
{
    public string? Id => accessor.HttpContext?.User.FindFirst("sub")?.Value;
    public List<string>? Roles => accessor.HttpContext?.User.FindAll(options.Value.RoleClaimType).Select(c => c.Value).ToList();
}
