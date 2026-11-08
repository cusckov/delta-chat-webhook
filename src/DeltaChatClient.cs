using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// JSON-RPC 2.0 клиент для deltachat-rpc-server (общение через stdin/stdout).
/// Один singleton на весь процесс, потокобезопасный.
/// </summary>
public sealed class DeltaChatRpcClient : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly Dictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();

    private Process? _process;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private Task? _readLoop;
    private CancellationTokenSource _cts = new();
    private int _nextId = 0;

    private int _accountId = -1;
    private bool _initialized;

    public DeltaChatRpcClient(ILogger<DeltaChatRpcClient> logger) => _logger = logger;

    // ────────────────────────────────────────────────
    // Запуск процесса
    // ────────────────────────────────────────────────

    public async Task StartAsync()
    {
        var accountsPath = Environment.GetEnvironmentVariable("DC_ACCOUNTS_PATH") ?? "/data/accounts";
        Directory.CreateDirectory(accountsPath);

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "deltachat-rpc-server",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        _process.StartInfo.EnvironmentVariables["DC_ACCOUNTS_PATH"] = accountsPath;
        _process.Start();

        _writer = _process.StandardInput;
        _reader = _process.StandardOutput;
        _readLoop = Task.Run(ReadLoopAsync);

        // stderr → лог на Information, чтобы SMTP/IMAP/auth ошибки были видны в проде
        _ = Task.Run(async () =>
        {
            try
            {
                while (await _process.StandardError.ReadLineAsync() is { } line)
                {
                    _logger.LogInformation("[dc-rpc] {Line}", line);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "stderr reader stopped");
            }
        });

        _logger.LogInformation("deltachat-rpc-server process started (PID {Pid})", _process.Id);
    }

    // ────────────────────────────────────────────────
    // Инициализация аккаунта (один раз)
    // ────────────────────────────────────────────────

    public async Task<int> EnsureAccountAsync()
    {
        if (_initialized)
        {
            // Self-heal: re-call start_io on every subsequent call in case the
            // IO loop died (e.g. after SMTP auth failure, mail server disconnect,
            // or DC RPC internal error). start_io is idempotent in DC core.
            try { await CallAsync("start_io", new object[] { _accountId }); }
            catch (Exception ex) { _logger.LogWarning(ex, "start_io re-call failed"); }
            return _accountId;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return _accountId;

            var email = Environment.GetEnvironmentVariable("DC_EMAIL")
                           ?? throw new InvalidOperationException("DC_EMAIL not set");
            var password = Environment.GetEnvironmentVariable("DC_PASSWORD")
                           ?? throw new InvalidOperationException("DC_PASSWORD not set");

            // Получаем существующие аккаунты
            var ids = await CallAsync("get_all_account_ids");
            var accountIds = ids?.AsArray().Select(n => n!.GetValue<int>()).ToList() ?? [];

            if (accountIds.Count > 0)
            {
                _accountId = accountIds[0];
                _logger.LogInformation("Reusing existing DC account id={Id}", _accountId);
            }
            else
            {
                var newId = await CallAsync("add_account");
                _accountId = newId!.GetValue<int>();
                _logger.LogInformation("Created new DC account id={Id}", _accountId);
            }

            // Проверяем, настроен ли аккаунт
            var configured = await CallAsync("get_config", new object[] { _accountId, "configured" });
            if (configured?.GetValue<string>() != "1")
            {
                _logger.LogInformation("Configuring account {Email}...", email);

                // IMAP/SMTP серверы — через env vars (можно подменить relay без пересборки).
                // Defaults — beget, для локальной разработки.
                var imapHost = Environment.GetEnvironmentVariable("IMAP_HOST") ?? "imap.beget.com";
                var imapPort = int.TryParse(Environment.GetEnvironmentVariable("IMAP_PORT"), out var ip) ? ip : 993;
                var imapSec  = Environment.GetEnvironmentVariable("IMAP_SECURITY") ?? "ssl";
                var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.beget.com";
                var smtpPort = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var sp) ? sp : 465;
                var smtpSec  = Environment.GetEnvironmentVariable("SMTP_SECURITY") ?? "ssl";

                _logger.LogInformation("Transport: imap={Host}:{Port}/{Sec} smtp={Host}:{Port}/{Sec}",
                    imapHost, imapPort, imapSec, smtpHost, smtpPort, smtpSec);

                // Ручная настройка IMAP/SMTP серверов ( чтобы не зависал на auto-discovery)
                await CallAsync("set_config", new object[] { _accountId, "addr", email });
                await CallAsync("set_config", new object[] { _accountId, "mail_pw", password });
                await CallAsync("add_or_update_transport", new object[] { _accountId, new
                {
                    addr         = email,
                    password     = password,
                    imapServer   = imapHost,
                    imapPort     = imapPort,
                    imapSecurity = imapSec,
                    smtpServer   = smtpHost,
                    smtpPort     = smtpPort,
                    smtpSecurity = smtpSec
                }});
            }
            else
            {
                _logger.LogInformation("Account already configured");
            }

            await CallAsync("start_io", new object[] { _accountId });
            _logger.LogInformation("Account IO started");

            _initialized = true;
            return _accountId;
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ────────────────────────────────────────────────
    // Публичные методы
    // ────────────────────────────────────────────────

    /// <summary>
    /// Генерирует SecureJoin QR код для добавления этого аккаунта в контакты.
    /// Используется чтобы другой Delta Chat клиент мог добавить этот аккаунт.
    /// </summary>
    public async Task<string> GetSecureJoinQrCodeAsync()
    {
        var accountId = await EnsureAccountAsync();
        // Передаём null как chat_id чтобы получить setup-contact QR (не group)
        var result = await CallAsync("get_chat_securejoin_qr_code_svg", new object[] { accountId, null! });
        // Результат - массив [code, svg]
        var arr = result?.AsArray();
        return arr?.LastOrDefault()?.GetValue<string>() ?? "";
    }

    /// <summary>
    /// Разрешает target → chat_id.
    /// target может быть: числовой chat_id или email-адрес.
    /// </summary>
    public async Task<int> ResolveChatIdAsync(string target)
    {
        var accountId = await EnsureAccountAsync();

        if (int.TryParse(target, out var chatId))
        {
            _logger.LogInformation("Using numeric chat_id={ChatId}", chatId);
            return chatId;
        }

        // Email → создать/найти контакт → чат
        if (target.Contains('@'))
        {
            _logger.LogInformation("Resolving email {Email} to chat...", target);
            var contactId = await CallAsync("create_contact", new object[] { accountId, target, null! });
            var cId = await CallAsync("create_chat_by_contact_id", new object[] { accountId, contactId!.GetValue<int>() });
            var resolvedChatId = cId!.GetValue<int>();
            _logger.LogInformation("Resolved {Email} -> chat_id={ChatId}", target, resolvedChatId);
            return resolvedChatId;
        }

        throw new ArgumentException($"Invalid chat target '{target}'. Use numeric chat_id or email address.");
    }

    /// <summary>Отправить текстовое сообщение в чат.</summary>
    public async Task<int> SendMessageAsync(int chatId, string text)
    {
        var accountId = await EnsureAccountAsync();
        var msgPayload = new { text };
        var msgId = await CallAsync("send_msg", new object[] { accountId, chatId, msgPayload });
        var result = msgId!.GetValue<int>();
        _logger.LogInformation("send_msg chat_id={ChatId} -> msg_id={MsgId} (queued, not yet delivered)", chatId, result);
        return result;
    }

    /// <summary>Список активных чатов аккаунта.</summary>
    public async Task<List<ChatInfo>> GetChatsAsync()
    {
        var accountId = await EnsureAccountAsync();

        // get_chatlist_entries returns flat array of chat IDs (not array of arrays)
        var entries = await CallAsync("get_chatlist_entries", new object[] { accountId, 0, null!, null! });
        var chatIds = entries?.AsArray()
            ?.Select(e => e?.GetValue<int>())
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList() ?? [];

        var result = new List<ChatInfo>();
        foreach (var cid in chatIds)
        {
            try
            {
                var info = await CallAsync("get_full_chat_by_id", new object[] { accountId, cid });
                if (info is JsonObject obj)
                {
                    result.Add(new ChatInfo
                    {
                        ChatId = cid,
                        Name = obj["name"]?.GetValue<string>() ?? "",
                        Type = obj["chat_type"]?.GetValue<string>() ?? "",
                        IsGroup = obj["is_group"]?.GetValue<bool>() ?? false,
                        Members = obj["member_count"]?.GetValue<int>() ?? 0,
                        Archived = obj["archived"]?.GetValue<bool>() ?? false,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get chat info for chat_id={ChatId}", cid);
            }
        }

        return result;
    }

    // ────────────────────────────────────────────────
    // JSON-RPC ядро
    // ────────────────────────────────────────────────

    public async Task<JsonNode?> CallAsync(string method, object? @params = null)
    {
        var id = Interlocked.Increment(ref _nextId);

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = @params != null
                ? JsonNode.Parse(JsonSerializer.Serialize(@params))
                : new JsonArray()
        };

        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[id] = tcs;

        await _writeLock.WaitAsync();
        try
        {
            await _writer!.WriteLineAsync(request.ToJsonString());
            await _writer.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        timeout.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

        var response = await tcs.Task;

        if (response?["error"] is JsonNode err)
        {
            var msg = err["message"]?.GetValue<string>() ?? "Unknown RPC error";
            throw new InvalidOperationException($"DC RPC [{method}] error: {msg}");
        }

        return response?["result"];
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = await _reader!.ReadLineAsync(_cts.Token);
                if (line == null) break;

                try
                {
                    var node = JsonNode.Parse(line);
                    var id = node?["id"]?.GetValue<int>();
                    if (id.HasValue)
                    {
                        TaskCompletionSource<JsonNode?>? tcs;
                        lock (_pending)
                        {
                            _pending.TryGetValue(id.Value, out tcs);
                            _pending.Remove(id.Value);
                        }
                        tcs?.TrySetResult(node);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse RPC line: {Line}", line);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_process is { HasExited: false })
        {
            _process.Kill();
            _process.Dispose();
        }
    }

    // ────────────────────────────────────────────────
    // Diagnostic helpers (used by /status endpoint)
    // ────────────────────────────────────────────────

    /// <summary>
    /// True if the deltachat-rpc-server subprocess is still running.
    /// Cheap — does not touch the wire.
    /// </summary>
    public bool IsRpcAlive(out int? pid)
    {
        pid = null;
        if (_process is null) return false;
        pid = _process.HasExited ? null : (int?)_process.Id;
        return !_process.HasExited;
    }

    /// <summary>
    /// Lists all configured DC account IDs (does NOT initialize anything new).
    /// </summary>
    public async Task<List<int>> GetAccountIdsAsync()
    {
        var ids = await CallAsync("get_all_account_ids");
        return ids?.AsArray()
                   ?.Select(n => n?.GetValue<int>() ?? 0)
                   .Where(id => id > 0)
                   .ToList() ?? [];
    }

    /// <summary>
    /// Reads a boolean-ish config flag (configured, e2ee_enabled, …) for the account.
    /// </summary>
    public async Task<bool> GetConfigBoolAsync(int accountId, string key)
    {
        var value = await CallAsync("get_config", new object[] { accountId, key });
        return value?.GetValue<string>() is "1";
    }

    /// <summary>
    /// Best-effort IO probe. We can't ask DC RPC for IO state directly, so
    /// we use a benign RPC call that always exists for a configured account.
    /// If RPC responds within the timeout, the IO loop is at minimum responsive.
    /// </summary>
    public async Task<bool> CheckIoRunningAsync(int accountId)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var task = CallAsync("get_config", new object[] { accountId, "configured" });
            var done = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
            cts.Cancel();
            return done == task && !task.IsFaulted;
        }
        catch
        {
            return false;
        }
    }
}

public record ChatInfo
{
    public int ChatId { get; init; }
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public bool IsGroup { get; init; }
    public int Members { get; init; }
    public bool Archived { get; init; }
}