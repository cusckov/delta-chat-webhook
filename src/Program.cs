using DeltaChatWebhookClient.Dto;
using DeltaChatWebhookClient.Endpoints;
using DeltaChatWebhookClient.Infrastructure;
using DeltaChatWebhookClient.Services;

// Port is configured via standard .NET env vars (HTTP_PORTS / ASPNETCORE_URLS),
// not via custom UseUrls() — that was conflicting with Dokploy's HTTP_PORTS=8080 default.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "yyyy-MM-dd HH:mm:ss "; });

builder.Services.AddSingleton<DeltaChatRpcClient>();
builder.Services.AddSingleton<DokployMessageFormatter>();
builder.Services.AddSingleton<SimpleMessageFormatter>();
builder.Services.AddSingleton<IMessageFormatter>(sp => sp.GetRequiredService<DokployMessageFormatter>());
builder.Services.AddSingleton<IMessageFormatter>(sp => sp.GetRequiredService<SimpleMessageFormatter>());
builder.Services.AddSingleton<MessageDispatcher>();
builder.Services.AddSingleton<EndpointErrorFilter>();

// Кастомный заголовок для входящих вебхуков (имя + токен из .env).
var webhookAuth = WebhookAuthOptions.FromEnvironment();
builder.Services.AddSingleton(webhookAuth);
builder.Services.AddSingleton<WebhookAuthMiddleware>();

var app = builder.Build();
var logger = app.Logger;

var dcEmail = Environment.GetEnvironmentVariable("DC_EMAIL") ?? string.Empty;
logger.LogInformation("Starting Delta Chat Webhook Proxy");
logger.LogInformation("DC Account: {Email}", string.IsNullOrWhiteSpace(dcEmail) ? "(not set)" : dcEmail);
if (string.IsNullOrWhiteSpace(dcEmail)) logger.LogWarning("DC_EMAIL not set!");

if (webhookAuth.IsEnabled)
    logger.LogInformation(
        "Webhook auth enabled: header '{Header}' is required for POST /webhook/*, POST /dokploy/* and GET /chats",
        webhookAuth.HeaderName);
else
    logger.LogWarning(
        "WEBHOOK_AUTH_TOKEN is not set — POST /webhook/*, POST /dokploy/* and GET /chats accept requests without a token");

// CORS идёт первым: он короткозамыкает OPTIONS-префлайт, который не должен
// требовать заголовок авторизации.
app.UseDeltaChatCors(webhookAuth.HeaderName);
app.UseMiddleware<WebhookAuthMiddleware>();

await DcBootstrap.InitializeAsync(app.Services, logger);

app.MapWebhookEndpoints();
app.MapDokployEndpoints();
app.MapStatusEndpoints();

app.MapFallback(() => Results.Json(
    new ApiError("error", 400,
        "Invalid path. Available: GET /chats, GET|POST /webhook/<chat_id|email>, POST /dokploy/<chat_id|email>"),
    statusCode: 400));

await app.RunAsync();