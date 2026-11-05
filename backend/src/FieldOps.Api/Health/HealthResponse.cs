namespace FieldOps.Api.Health;

public sealed record HealthResponse(string Status, IReadOnlyList<HealthCheckResponse> Checks);

public sealed record HealthCheckResponse(string Name, string Status);
