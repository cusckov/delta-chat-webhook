using System.Security.Cryptography;
using System.Text;
using DeltaChatWebhookClient.Dto;

namespace DeltaChatWebhookClient.Infrastructure;

/// <summary>
/// Проверяет кастомный заголовок (по умолчанию X-Webhook-Token):
///   • в POST-запросах к /webhook/* и /dokploy/* — там, что реально доставляют
///     сообщение в чат;
///   • в GET /chats — там отдаются имена чатов, состав групп и chat_id,
///     то есть метаданные аккаунта и готовый список целей для рассылки.
///
/// Открытыми остаются /qr, /status и GET-версии /webhook и /dokploy: они
/// ничего не отправляют и не раскрывают метаданных аккаунта.
///
/// Если WEBHOOK_AUTH_TOKEN не задан — проверка отключена (обратная совместимость).
/// Сравнение токена — constant-time, чтобы нельзя было подобрать его замером времени.
/// </summary>
public sealed class WebhookAuthMiddleware : IMiddleware
{
    private static readonly string[] ProtectedPrefixes = ["/webhook", "/dokploy"];
    private const string ProtectedExactPath = "/chats";

    private readonly WebhookAuthOptions _options;
    private readonly ILogger<WebhookAuthMiddleware> _logger;

    public WebhookAuthMiddleware(WebhookAuthOptions options, ILogger<WebhookAuthMiddleware> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!_options.IsEnabled || !IsProtected(context.Request))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(_options.HeaderName, out var provided) ||
            provided.Count == 0 || string.IsNullOrEmpty(provided[0]))
        {
            _logger.LogWarning(
                "Rejected {Method} {Path}: missing header {Header}",
                context.Request.Method, context.Request.Path, _options.HeaderName);

            await RejectAsync(context, $"Missing required header '{_options.HeaderName}'");
            return;
        }

        var expected = Encoding.UTF8.GetBytes(_options.Token);
        var actual = Encoding.UTF8.GetBytes(provided[0]!);

        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            _logger.LogWarning(
                "Rejected {Method} {Path}: invalid value in header {Header}",
                context.Request.Method, context.Request.Path, _options.HeaderName);

            await RejectAsync(context, $"Invalid value in header '{_options.HeaderName}'");
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Защищаются POST-эндпоинты, отправляющие сообщение в чат, и GET /chats
    /// с метаданными аккаунта.
    /// </summary>
    private static bool IsProtected(HttpRequest request)
    {
        var path = request.Path.Value ?? "";

        // /chats — метаданные аккаунта (имена чатов, состав групп, chat_id).
        if (path.Equals(ProtectedExactPath, StringComparison.OrdinalIgnoreCase) ||
            path.Equals(ProtectedExactPath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!HttpMethods.IsPost(request.Method)) return false;

        return ProtectedPrefixes.Any(prefix =>
            path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task RejectAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new ApiError("error", 401, message));
    }
}
