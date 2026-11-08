namespace DeltaChatWebhookClient.Dto;

/// <summary>Unified success response for POST /webhook/{chatTarget} and POST /dokploy/{chatTarget}.</summary>
public sealed record SendResult(string Status, int ChatId, int MsgId);