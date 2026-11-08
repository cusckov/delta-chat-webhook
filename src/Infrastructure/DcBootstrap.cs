namespace DeltaChatWebhookClient.Infrastructure;

/// <summary>
/// Startup orchestration for the Delta Chat RPC client.
/// Keeps Program.cs free of lifecycle glue.
/// </summary>
public static class DcBootstrap
{
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger)
    {
        var dc = services.GetRequiredService<DeltaChatRpcClient>();

        // Start the RPC subprocess. If the binary is missing (e.g. local dev without Docker),
        // log and continue — the HTTP server stays up so health probes and error paths work.
        try { await dc.StartAsync(); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start deltachat-rpc-server; HTTP server continues without DC connectivity");
            return;
        }

        // Account init in the background so app startup isn't blocked.
        _ = Task.Run(async () =>
        {
            try { await dc.EnsureAccountAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Failed to initialize Delta Chat account on startup"); }
        });
    }
}