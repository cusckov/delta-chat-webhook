namespace DeltaChatWebhookClient.Infrastructure;

public static class CorsMiddleware
{
    /// <summary>
    /// Sets permissive CORS headers and short-circuits OPTIONS preflight with 200 + empty JSON.
    /// Behavior is identical to the inline handler previously in Program.cs.
    /// </summary>
    /// <param name="additionalHeaderName">
    /// Кастомный заголовок (например X-Webhook-Token), который должен быть разрешён
    /// в Access-Control-Allow-Headers, иначе браузерный отправитель не пройдёт preflight.
    /// </param>
    public static IApplicationBuilder UseDeltaChatCors(this IApplicationBuilder app, string? additionalHeaderName = null)
    {
        var allowedHeaders = "Content-Type";
        if (!string.IsNullOrWhiteSpace(additionalHeaderName) &&
            !additionalHeaderName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
        {
            allowedHeaders += ", " + additionalHeaderName;
        }

        return app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("Access-Control-Allow-Origin",  "*");
            context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            context.Response.Headers.Append("Access-Control-Allow-Headers", allowedHeaders);

            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode  = StatusCodes.Status200OK;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{}");
                return;
            }

            await next();
        });
    }
}