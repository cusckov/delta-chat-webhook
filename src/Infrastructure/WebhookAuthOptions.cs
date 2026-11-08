namespace DeltaChatWebhookClient.Infrastructure;

/// <summary>
/// Настройки проверки кастомного заголовка на входящих вебхуках.
/// Читаются из переменных окружения (.env):
///   WEBHOOK_AUTH_HEADER — имя заголовка (по умолчанию X-Webhook-Token);
///   WEBHOOK_AUTH_TOKEN  — ожидаемое значение заголовка.
/// Если токен не задан — проверка отключена (эндпоинты остаются открытыми).
/// </summary>
public sealed class WebhookAuthOptions
{
    public const string DefaultHeaderName = "X-Webhook-Token";

    /// <summary>Имя заголовка, который обязан присутствовать в запросе.</summary>
    public string HeaderName { get; init; } = DefaultHeaderName;

    /// <summary>Ожидаемое значение заголовка. Пустое — аутентификация отключена.</summary>
    public string Token { get; init; } = "";

    /// <summary>Включена ли проверка заголовка.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Token);

    /// <summary>Собирает настройки из переменных окружения.</summary>
    public static WebhookAuthOptions FromEnvironment()
    {
        var headerName = Environment.GetEnvironmentVariable("WEBHOOK_AUTH_HEADER")?.Trim();

        return new WebhookAuthOptions
        {
            HeaderName = string.IsNullOrWhiteSpace(headerName) ? DefaultHeaderName : headerName,
            // Токен не триммим: сравнение идёт с точностью до символа.
            Token = Environment.GetEnvironmentVariable("WEBHOOK_AUTH_TOKEN") ?? ""
        };
    }
}
