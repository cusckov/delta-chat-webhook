using System.Text.Json;
using System.Text.Json.Nodes;
using DeltaChatWebhookClient.Dto;
using DeltaChatWebhookClient.Infrastructure;
using DeltaChatWebhookClient.Services;

namespace DeltaChatWebhookClient.Endpoints;

public static class DokployEndpoints
{
    public static IEndpointRouteBuilder MapDokployEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("")
                       .AddEndpointFilter<EndpointErrorFilter>();

        // ── GET /dokploy/{chatTarget} ──────────────────────────────────────────
        group.MapGet("/dokploy/{chatTarget}", (string chatTarget, WebhookAuthOptions auth) =>
        {
            var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };
            if (auth.IsEnabled) headers[auth.HeaderName] = "<WEBHOOK_AUTH_TOKEN>";

            return Results.Json(new
            {
                status  = "ready",
                message = "Send POST request with Dokploy-format JSON body to this endpoint",
                example = new
                {
                    method = "POST",
                    body   = new
                    {
                        title     = "Test Notification",
                        message   = "Hi, From Dokploy 👋",
                        timestamp = "2025-12-07T19:41:53.470Z"
                    },
                    headers
                },
                chat_target     = chatTarget,
                required_header = auth.IsEnabled ? auth.HeaderName : null,
                hint            = "chat_target can be numeric chat_id (see GET /chats) or email address. Title and message will be rendered with timestamp footer."
            });
        });

        // ── POST /dokploy/{chatTarget} ─────────────────────────────────────────
        group.MapPost("/dokploy/{chatTarget}", async (
            string chatTarget,
            HttpContext context,
            DeltaChatRpcClient dc,
            DokployMessageFormatter formatter,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("DeltaChatWebhook.Dokploy");
            JsonNode? bodyJson;
            try { bodyJson = await JsonNode.ParseAsync(context.Request.Body); }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Invalid JSON: {ex.Message}");
            }

            if (bodyJson is null)
                throw new ArgumentException("Empty request body");

            if (bodyJson is not JsonObject data)
                throw new ArgumentException("JSON body must be an object");

            if (!formatter.CanHandle(data))
                throw new ArgumentException(
                    "Payload must include at least one of: 'title', 'message'.");

            var message = formatter.Format(data);
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException(
                    "Payload produced an empty message. Provide non-empty 'title' or 'message'.");

            logger.LogInformation("POST /dokploy/{ChatTarget} message: {Preview}", chatTarget, Preview(message, 100));

            var chatId = await dc.ResolveChatIdAsync(chatTarget);
            var msgId  = await dc.SendMessageAsync(chatId, message);

            logger.LogInformation("Dokploy message sent to chat_id={ChatId}, msg_id={MsgId}", chatId, msgId);

            return Results.Json(new SendResult("sent", chatId, msgId));
        });

        return app;
    }

    private static string Preview(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "...";
}