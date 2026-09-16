using System.Text.Json.Serialization;

namespace CleanArchitecture.Web.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCustomerRequest(string CustomerId, string? DisplayName);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceCustomerRequest(string? DisplayName);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatchCustomerRequest(string? DisplayName);

public sealed record CustomerResponse(string CustomerId, string DisplayName, DateTimeOffset ObservedAt);
public sealed record CustomerDeletionResponse(string CustomerId);
