namespace DeltaChatWebhookClient.Dto;

/// <summary>
/// Documentation-only type for the Dokploy webhook payload contract.
/// The runtime pipeline operates on <see cref="System.Text.Json.Nodes.JsonObject"/>
/// to remain consistent with the generic /webhook endpoint. This record exists so the
/// public API contract is documented with a real type.
/// </summary>
public sealed record DokployPayload(string? Title, string? Message, DateTimeOffset? Timestamp);