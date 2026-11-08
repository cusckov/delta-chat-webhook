using System.Text.Json.Nodes;

namespace DeltaChatWebhookClient.Services;

/// <summary>
/// Converts an inbound JSON payload to the text that will be sent to a Delta Chat chat.
/// </summary>
public interface IMessageFormatter
{
    /// <summary>True if this formatter recognizes the payload shape.</summary>
    bool CanHandle(JsonObject payload);

    /// <summary>
    /// Render the payload to a message string. May return empty string when the
    /// payload is recognized but produces no usable content; the dispatcher will
    /// then try the next formatter.
    /// </summary>
    string Format(JsonObject payload);
}