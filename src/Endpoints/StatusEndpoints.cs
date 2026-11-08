using System.Diagnostics;
using DeltaChatWebhookClient.Infrastructure;

namespace DeltaChatWebhookClient.Endpoints;

public static class StatusEndpoints
{
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("")
                       .AddEndpointFilter<EndpointErrorFilter>();

        // ── GET /status ────────────────────────────────────────────────────────
        // Cheap health-and-state probe. Surfaces DC RPC subprocess + account state
        // so we can tell apart "app broken", "DC dead", "account misconfigured",
        // "delivery queue backed up" without shell access to the container.
        group.MapGet("/status", async (DeltaChatRpcClient dc, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("DeltaChatWebhook.Status");
            var sw = Stopwatch.StartNew();

            var snapshot = new Dictionary<string, object?>
            {
                ["rpc_subprocess_alive"] = false,
                ["rpc_subprocess_pid"]   = null,
                ["account_id"]           = null,
                ["account_configured"]   = false,
                ["io_running"]           = false,
                ["error"]                = null,
                ["elapsed_ms"]           = 0
            };

            try
            {
                snapshot["rpc_subprocess_alive"] = dc.IsRpcAlive(out var pid);
                snapshot["rpc_subprocess_pid"]   = pid;

                if (dc.IsRpcAlive(out _))
                {
                    // Cheap RPC calls — they don't trigger SMTP/IMAP network IO
                    // and won't hang if the account is broken.
                    var ids = await dc.GetAccountIdsAsync();
                    if (ids.Count > 0)
                    {
                        var accountId = ids[0];
                        snapshot["account_id"] = accountId;
                        snapshot["account_configured"] = await dc.GetConfigBoolAsync(accountId, "configured");
                        // Probe IO state by timing a benign RPC call.
                        snapshot["io_running"] = await dc.CheckIoRunningAsync(accountId);
                    }
                }
            }
            catch (Exception ex)
            {
                snapshot["error"] = ex.Message;
                logger.LogWarning(ex, "Status check failed");
            }
            finally
            {
                sw.Stop();
                snapshot["elapsed_ms"] = sw.ElapsedMilliseconds;
                logger.LogInformation("Status: {Rpc} pid={Pid} account={Account} configured={Configured}",
                    snapshot["rpc_subprocess_alive"], snapshot["rpc_subprocess_pid"],
                    snapshot["account_id"], snapshot["account_configured"]);
            }

            return Results.Json(snapshot);
        });

        return app;
    }
}