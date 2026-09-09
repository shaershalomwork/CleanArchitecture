namespace CleanArchitecture.Web.Authentication;
public sealed class AuthenticationOptions
{
    public string Mode { get; set; } = "External";
    public string Authority { get; set; } = "";
    public string Audience { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string NameClaimType { get; set; } = "name";
    public string RoleClaimType { get; set; } = "roles";
}
