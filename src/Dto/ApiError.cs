namespace DeltaChatWebhookClient.Dto;

/// <summary>Unified error-response shape used by all 4xx/5xx responses.</summary>
public sealed record ApiError(string Status, int Code, string Message);