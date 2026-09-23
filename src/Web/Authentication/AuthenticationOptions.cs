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
    public string PublicOrigin { get; set; } = "";
    public int MaximumAgeSeconds { get; set; } = 300;
    public string? CaBundlePath { get; set; }
    public string? DataProtectionKeysPath { get; set; }
    public string? DataProtectionCertificatePath { get; set; }
    public string? DataProtectionApplicationName { get; set; }
    public string[] TrustedProxies { get; set; } = [];
    public bool EnableRuntimeApiReference { get; set; } = true;
}
