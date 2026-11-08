using System.Text.Json;
using DeltaChatWebhookClient.Dto;

namespace DeltaChatWebhookClient.Infrastructure;

/// <summary>
/// IEndpointFilter that converts known exceptions thrown by handlers into unified ApiError JSON.
/// Applied to endpoint groups via .AddEndpointFilter&lt;EndpointErrorFilter&gt;().
/// Removes try/catch boilerplate from individual endpoint handlers.
/// </summary>
public sealed class EndpointErrorFilter : IEndpointFilter
{
    private readonly ILogger<EndpointErrorFilter> _logger;

    public EndpointErrorFilter(ILogger<EndpointErrorFilter> logger) => _logger = logger;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        try
        {
            return await next(ctx);
        }
        catch (JsonException ex)
        {
            return Results.Json(new ApiError("error", 400, $"Invalid JSON: {ex.Message}"), statusCode: 400);
        }
        catch (ArgumentException ex)
        {
            return Results.Json(new ApiError("error", 400, ex.Message), statusCode: 400);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in endpoint {Path}", ctx.HttpContext.Request.Path);
            return Results.Json(new ApiError("error", 500, ex.Message), statusCode: 500);
        }
    }
}