using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Endpoints;

internal static class ProblemResponses
{
    public static TBuilder WithProblemResponses<TBuilder>(this TBuilder builder, params int[] statuses)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(statuses.Select(status =>
            new ProducesResponseTypeMetadata(status, typeof(ProblemDetails), ["application/problem+json"])).ToArray());
}
