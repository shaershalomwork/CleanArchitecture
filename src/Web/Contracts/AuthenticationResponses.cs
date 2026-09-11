namespace CleanArchitecture.Web.Contracts;

public sealed record SessionCapabilities(bool CanReadCustomers, bool CanWriteCustomers);
public sealed record CurrentSessionResponse(string? Id, string? Name, string[] Roles, string[] Permissions, SessionCapabilities Capabilities);
public sealed record DevelopmentProfileResponse(string Id, string Label);
public sealed record AuthenticationUiOptionsResponse(bool LoginAvailable, DevelopmentProfileResponse[] DevelopmentProfiles);
public sealed record AntiforgeryResponse(string? Token);
