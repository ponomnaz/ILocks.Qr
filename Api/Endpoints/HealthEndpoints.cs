namespace Api.Endpoints;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "ILocks.Qr.Api",
            environment = app.Environment.EnvironmentName
        }))
            .WithName("Health")
            .WithSummary("Health check")
            .WithDescription("Basic API health endpoint.")
            .Produces(StatusCodes.Status200OK)
            .WithOpenApi();

        return app;
    }
}
