using System.Text.Json.Nodes;

namespace DeltaChatWebhookClient.Services;

/// <summary>
/// Routes an inbound JSON payload through registered <see cref="IMessageFormatter"/>s
/// in registration order, returning the first non-empty result.
/// Throws <see cref="ArgumentException"/> when no formatter can produce a non-empty message.
/// Preserves the original fallback semantics from Program.cs ExtractMessage:
/// if Dokploy fields are present but produce no output, the dispatcher tries Simple.
/// </summary>
public sealed class MessageDispatcher
{
    private readonly IEnumerable<IMessageFormatter> _formatters;
    private readonly ILogger<MessageDispatcher> _logger;

    public MessageDispatcher(IEnumerable<IMessageFormatter> formatters, ILogger<MessageDispatcher> logger)
    {
        _formatters = formatters;
        _logger = logger;
    }

    public string Format(JsonObject payload)
    {
        foreach (var formatter in _formatters)
        {
            if (!formatter.CanHandle(payload)) continue;

            string result;
            try { result = formatter.Format(payload); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Formatter {Formatter} threw, trying next", formatter.GetType().Name);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(result))
            {
                _logger.LogDebug("Payload routed via {Formatter}", formatter.GetType().Name);
                return result;
            }
        }

        throw new ArgumentException(
            "No message provided. Supported formats:\n" +
            "1. {\"text\": \"message\"}\n" +
            "2. {\"message\": \"text\"}\n" +
            "3. {\"body\": \"text\"}\n" +
            "4. {\"title\": \"...\", \"message\": \"...\", \"timestamp\": \"...\"}");
    }
}