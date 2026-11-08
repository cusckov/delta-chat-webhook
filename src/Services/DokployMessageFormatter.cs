using System.Globalization;
using System.Text.Json.Nodes;

namespace DeltaChatWebhookClient.Services;

/// <summary>
/// Formats payloads with the Dokploy shape: { title?, message?, timestamp?, ...extras }.
/// Recognized by the presence of either the `title` or `message` key.
/// Renders title as bold, lists other keys as `**key**: value`, formats ISO-8601 timestamps.
/// Logic is ported verbatim from the original ExtractMessage + TryFormatTimestamp helpers.
/// </summary>
public sealed class DokployMessageFormatter : IMessageFormatter
{
    private readonly ILogger<DokployMessageFormatter> _logger;

    public DokployMessageFormatter(ILogger<DokployMessageFormatter> logger) => _logger = logger;

    public bool CanHandle(JsonObject payload) =>
        payload.ContainsKey("title") || payload.ContainsKey("message");

    public string Format(JsonObject payload)
    {
        var title     = payload["title"]?.GetValue<string>()     ?? string.Empty;
        var message   = payload["message"]?.GetValue<string>()   ?? string.Empty;
        var timestamp = payload["timestamp"]?.GetValue<string>() ?? string.Empty;

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(title))     parts.Add($"**{title}**");
        if (!string.IsNullOrWhiteSpace(message))   parts.Add(message);

        foreach (var property in payload)
        {
            if (property.Key is "title" or "message" or "timestamp") continue;
            var value = property.Value?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add($"**{property.Key}**: {value}");
        }

        if (!string.IsNullOrWhiteSpace(timestamp))
        {
            parts.Add(TryFormatTimestamp(timestamp, out var fmt)
                ? $"*Время: {fmt}*"
                : $"*Время: {timestamp}*");
        }

        _logger.LogInformation("Dokploy format: title='{Title}', timestamp='{Ts}'", title, timestamp);

        return string.Join("\n", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static bool TryFormatTimestamp(string timestamp, out string formatted)
    {
        if (DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed))
        {
            formatted = parsed.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return true;
        }
        formatted = string.Empty;
        return false;
    }
}