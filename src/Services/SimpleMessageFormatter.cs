using System.Text.Json.Nodes;

namespace DeltaChatWebhookClient.Services;

/// <summary>
/// Fallback formatter. Accepts any payload and extracts `text`, `message`, or `body`
/// (in that order). Used when no other formatter handles the payload.
/// </summary>
public sealed class SimpleMessageFormatter : IMessageFormatter
{
    public bool CanHandle(JsonObject payload) => true;

    public string Format(JsonObject payload) =>
        payload["text"]?.GetValue<string>()
        ?? payload["message"]?.GetValue<string>()
        ?? payload["body"]?.GetValue<string>()
        ?? string.Empty;
}