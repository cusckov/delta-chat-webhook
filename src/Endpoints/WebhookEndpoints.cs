using System.Text.Json;
using System.Text.Json.Nodes;
using DeltaChatWebhookClient.Dto;
using DeltaChatWebhookClient.Infrastructure;
using DeltaChatWebhookClient.Services;

namespace DeltaChatWebhookClient.Endpoints;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("")
                       .AddEndpointFilter<EndpointErrorFilter>();

        // ── GET /qr ────────────────────────────────────────────────────────────
        group.MapGet("/qr", async (DeltaChatRpcClient dc) =>
        {
            var svg = await dc.GetSecureJoinQrCodeAsync();
            if (string.IsNullOrEmpty(svg))
                throw new InvalidOperationException("Failed to generate QR code");

            var svgBytes = System.Text.Encoding.UTF8.GetBytes(svg);
            return Results.Bytes(svgBytes, "image/svg+xml");
        });

        // ── GET /chats ─────────────────────────────────────────────────────────
        group.MapGet("/chats", async (DeltaChatRpcClient dc) =>
        {
            var chats = await dc.GetChatsAsync();
            return Results.Json(new
            {
                status = "ok",
                count  = chats.Count,
                chats  = chats.Select(c => new
                {
                    chat_id      = c.ChatId,
                    name         = c.Name,
                    type         = c.Type,
                    is_group     = c.IsGroup,
                    member_count = c.Members,
                    archived     = c.Archived,
                    webhook_url  = $"/webhook/{c.ChatId}"
                })
            });
        });

        // ── GET /webhook/{chatTarget} ──────────────────────────────────────────
        group.MapGet("/webhook/{chatTarget}", (string chatTarget, WebhookAuthOptions auth) =>
        {
            var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };
            if (auth.IsEnabled) headers[auth.HeaderName] = "<WEBHOOK_AUTH_TOKEN>";

            return Results.Json(new
            {
                status  = "ready",
                message = "Send POST request with JSON body to this endpoint",
                example = new
                {
                    method  = "POST",
                    body    = new { text = "Your message with **Markdown**" },
                    headers
                },
                supported_formats = new[] { "markdown", "plain" },
                chat_target       = chatTarget,
                required_header   = auth.IsEnabled ? auth.HeaderName : null,
                hint              = "chat_target can be numeric chat_id (see GET /chats) or email address"
            });
        });

        // ── POST /webhook/{chatTarget} ─────────────────────────────────────────
        group.MapPost("/webhook/{chatTarget}", async (
            string chatTarget,
            HttpContext context,
            DeltaChatRpcClient dc,
            MessageDispatcher dispatcher,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("DeltaChatWebhook.Webhook");

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

            var message = dispatcher.Format(data); // throws ArgumentException if no formatter can produce text

            logger.LogInformation("POST /webhook/{ChatTarget} message: {Preview}", chatTarget, Preview(message, 100));

            var chatId = await dc.ResolveChatIdAsync(chatTarget);
            var msgId  = await dc.SendMessageAsync(chatId, message);

            logger.LogInformation("Message sent to chat_id={ChatId}, msg_id={MsgId}", chatId, msgId);

            return Results.Json(new SendResult("sent", chatId, msgId));
        });

        return app;
    }

    private static string Preview(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "...";
}