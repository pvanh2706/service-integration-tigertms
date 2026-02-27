using Microsoft.AspNetCore.Mvc;
using ServiceIntegration.Core.Abstractions;

namespace ServiceIntegration.Endpoints;

public static class PmsEndpoints
{
    public static IEndpointRouteBuilder MapPmsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { ok = true }));

        // Demo endpoint: PMS callback
        app.MapPost("/pms/callback", ([FromBody] PmsCallbackRequest req) =>
        {
            return Results.Ok(new { ok = true, req.EventId, req.TigerStatus });
        });

        return app;
    }
}
