using System.Text;
using StealthEpubReader.Models;

namespace StealthEpubReader.Services;

public static class CamouflageService
{
    public static IReadOnlyList<(string Key, string Label)> Modes { get; } = new[]
    {
        ("None", "無偽裝 (純小說)"),
        ("SystemLog", "系統 Log 偽裝外殼"),
        ("CSharpCode", "C# 程式碼註解偽裝")
    };

    private static readonly string[] LogModules =
    {
        "OrderPipeline", "TokenValidator", "DbConnectionPool", "GrpcChannel",
        "CacheCoordinator", "MetricsCollector", "QueueDispatcher", "AuthMiddleware"
    };

    private static readonly string[] LogLevels = { "INF", "DBG", "INF", "INF", "TRC" };

    private static readonly string[] CodeSnippets =
    {
        "var scope = _serviceProvider.CreateAsyncScope();",
        "await _telemetry.RecordLatencyAsync( correlationId, elapsedMs ).ConfigureAwait(false);",
        "if (context.CancellationToken.IsCancellationRequested) return OperationResult.Cancelled;",
        "var snapshot = Interlocked.CompareExchange(ref _activeState, nextState, currentState);",
        "using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);",
        "logger.LogDebug(\"Validating payload checksum for partition {PartitionId}\", partitionId);",
        "var buffer = ArrayPool<byte>.Shared.Rent(4096);",
        "if (!TryResolveDescriptor(request.Header, out var descriptor)) throw new InvalidOperationException();"
    };

    public static string FormatChapterContent(ChapterContent chapter, string camouflageMode)
    {
        if (string.IsNullOrEmpty(camouflageMode) || camouflageMode == "None" || chapter.Paragraphs.Count == 0)
        {
            return chapter.FullText;
        }

        if (camouflageMode == "SystemLog")
        {
            return FormatAsSystemLog(chapter);
        }

        if (camouflageMode == "CSharpCode")
        {
            return FormatAsCSharpCode(chapter);
        }

        return chapter.FullText;
    }

    private static string FormatAsSystemLog(ChapterContent chapter)
    {
        var sb = new StringBuilder();
        var baseTime = DateTime.Now.AddMinutes(-15);

        sb.AppendLine($"[================================================================================]");
        sb.AppendLine($"[HOST-DIAGNOSTIC] Session={Guid.NewGuid().ToString("N")[..8]} | Module={chapter.Title}");
        sb.AppendLine($"[================================================================================]");

        for (int i = 0; i < chapter.Paragraphs.Count; i++)
        {
            var timestamp = baseTime.AddSeconds(i * 1.7 + (i % 3) * 0.23).ToString("HH:mm:ss.fff");
            string level = LogLevels[i % LogLevels.Length];
            string module = LogModules[i % LogModules.Length];
            int threadId = ((i * 7 + chapter.Index) % 24) + 4;

            sb.AppendLine($"[{timestamp} {level}] [T-{threadId:00}] {module}: {chapter.Paragraphs[i]}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatAsCSharpCode(ChapterContent chapter)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine();
        sb.AppendLine($"namespace Internal.Core.Diagnostics // {chapter.Title}");
        sb.AppendLine("{");
        sb.AppendLine($"    public sealed class RuntimeStateEvaluator_{chapter.Index + 1:00}");
        sb.AppendLine("    {");

        for (int i = 0; i < chapter.Paragraphs.Count; i++)
        {
            if (i % 3 == 0)
            {
                int methodNum = (i / 3) + 1;
                sb.AppendLine($"        public async ValueTask<bool> VerifySegment_{methodNum:00}Async(CancellationToken ct)");
                sb.AppendLine("        {");
            }

            sb.AppendLine($"            // {chapter.Paragraphs[i]}");
            sb.AppendLine($"            {CodeSnippets[(i + chapter.Index) % CodeSnippets.Length]}");

            if (i % 3 == 2 || i == chapter.Paragraphs.Count - 1)
            {
                sb.AppendLine("            return true;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 產生逼真的長篇多頁 C# / SQL / EF Core / Redis / gRPC Exception StackTrace 與診斷日誌（支援滑鼠滾輪長距離上下滾動檢視）
    /// </summary>
    public static string GenerateFakeExceptionScreen()
    {
        var sb = new StringBuilder();
        var baseTime = DateTime.Now.AddMinutes(-4);

        string Ts(double addSec) => baseTime.AddSeconds(addSec).ToString("yyyy-MM-dd HH:mm:ss.fff");

        sb.AppendLine($"[{Ts(0.12)} INF] [HostMonitor] Runtime Diagnostics Stream attached (PID={Environment.ProcessId}, .NET {Environment.Version}, x64)");
        sb.AppendLine($"[{Ts(0.45)} DBG] [ConfigLoader] Loaded connection policy: MaxPoolSize=128, ConnectTimeout=15s, CommandTimeout=30s, EnableRetryOnFailure=true");
        sb.AppendLine($"[{Ts(1.08)} INF] [EfCore.Command] Executed DbCommand (14ms) [Parameters=[@p0='ORD-20261109-8841', @p1='2'], CommandType='Text']");
        sb.AppendLine("      SELECT TOP(50) [o].[OrderId], [o].[CustomerId], [o].[Status], [o].[RowVersion], [o].[UpdatedAtUtc]");
        sb.AppendLine("      FROM [dbo].[OrderTransactions] AS [o] WITH (READPAST, UPDLOCK)");
        sb.AppendLine("      WHERE [o].[Status] = @p1 AND [o].[PartitionKey] = @p0 ORDER BY [o].[UpdatedAtUtc] ASC;");
        sb.AppendLine();

        sb.AppendLine($"[{Ts(4.82)} WRN] [EfCore.Command] Slow query detected (2,845ms) on shard [sql-prod-tw-02.database.windows.net]:");
        sb.AppendLine("      UPDATE [dbo].[InventoryLedger] SET [ReservedQty] = [ReservedQty] + @p_delta, [LastModifiedUtc] = SYSUTCDATETIME()");
        sb.AppendLine("      WHERE [SkuCode] = @p_sku AND ([AvailableQty] - [ReservedQty]) >= @p_delta;");
        sb.AppendLine($"[{Ts(34.91)} ERR] [ConnectionPool-12] Unhandled exception in background worker pipeline (CorrelationId: 7f8c39a1-4b20-4e9d-8812-90c4e17a3301):");
        sb.AppendLine("Microsoft.Data.SqlClient.SqlException (0x80131904): Execution Timeout Expired. The timeout period elapsed prior to completion of the operation or the server is not responding.");
        sb.AppendLine(" ---> System.ComponentModel.Win32Exception (258): The wait operation timed out.");
        sb.AppendLine("   at Microsoft.Data.SqlClient.SqlConnection.OnError(SqlException exception, Boolean breakConnection, Action`1 wrapCloseInAction)");
        sb.AppendLine("   at Microsoft.Data.SqlClient.SqlInternalConnection.OnError(SqlException exception, Boolean breakConnection, Action`1 wrapCloseInAction)");
        sb.AppendLine("   at Microsoft.Data.SqlClient.TdsParser.ThrowExceptionAndWarning(TdsParserStateObject stateObj, Boolean callerHasConnectionLock, Boolean asyncClose)");
        sb.AppendLine("   at Microsoft.Data.SqlClient.TdsParserStateObject.ReadSniError(TdsParserStateObject stateObj, UInt32 error)");
        sb.AppendLine("   at Microsoft.Data.SqlClient.TdsParserStateObject.ReadSniSyncOverAsync()");
        sb.AppendLine("   at Microsoft.Data.SqlClient.TdsParserStateObject.TryReadNetworkPacket()");
        sb.AppendLine("   at Microsoft.Data.SqlClient.TdsParserStateObject.TryPrepareBuffer()");
        sb.AppendLine("   at Microsoft.Data.SqlClient.TdsParser.TryRun(RunBehavior runBehavior, SqlCommand cmdHandler, SqlDataReader dataStream, BulkCopySimpleResultSet bulkCopyHandler, TdsParserStateObject stateObj, Boolean& dataReady)");
        sb.AppendLine("   at Microsoft.Data.SqlClient.SqlCommand.InternalExecuteNonQuery(TaskCompletionSource`1 completion, Boolean sendToPipe, Int32 timeout, Boolean asyncWrite, String methodName)");
        sb.AppendLine("   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteNonQueryAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)");
        sb.AppendLine("   at Internal.Infrastructure.Persistence.SqlRepository`1.ExecuteBatchWithRetryAsync(IReadOnlyList`1 entities, CancellationToken ct) in D:\\src\\Core.Infrastructure\\Persistence\\SqlRepository.cs:line 184");
        sb.AppendLine("   at Internal.Services.OrderProcessing.BatchSyncWorker.ProcessPendingQueueAsync(CancellationToken stoppingToken) in D:\\src\\Core.Services\\Workers\\BatchSyncWorker.cs:line 92");
        sb.AppendLine("ClientConnectionId: 4a9e18f0-7c2b-4d3e-9a11-08d9f4c21e03");
        sb.AppendLine("Error Number: -2, State: 0, Class: 11");
        sb.AppendLine();

        sb.AppendLine($"[{Ts(35.04)} WRN] [Polly.RetryPolicy] Transient fault captured on attempt 1/3. Backoff delay: 1,250ms (Jitter=+142ms).");
        sb.AppendLine($"[{Ts(36.42)} INF] [RedisCluster] Multiplexer health check: Endpoints=[10.24.8.14:6379(Primary), 10.24.8.15:6379(Replica)], Connected=True, OpsPerSec=4,192");
        sb.AppendLine($"[{Ts(38.19)} ERR] [RedisCacheProvider] Failed to acquire distributed lock 'lock:settlement:batch:20261109':");
        sb.AppendLine("StackExchange.Redis.RedisTimeoutException: Timeout performing EVAL (5000ms), next:HMGETsession:usr:99210, inst: 14, qu: 0, qs: 19, aw: False, bw: SpinningDown, rs: ReadAsync, ws: Idle");
        sb.AppendLine("   at StackExchange.Redis.ConnectionMultiplexer.ExecuteSyncImpl[T](Message message, ResultProcessor`1 processor, ServerEndPoint server, T defaultValue)");
        sb.AppendLine("   at StackExchange.Redis.RedisDatabase.ScriptEvaluateAsync(String script, RedisKey[] keys, RedisValue[] values, CommandFlags flags)");
        sb.AppendLine("   at Internal.Infrastructure.Caching.DistributedLockManager.TryAcquireLockAsync(String resourceKey, TimeSpan ttl, CancellationToken ct) in D:\\src\\Core.Infrastructure\\Caching\\DistributedLockManager.cs:line 67");
        sb.AppendLine("   at Internal.Services.Settlement.SettlementOrchestrator.ExecuteReconciliationStepAsync(ReconciliationContext ctx, CancellationToken ct) in D:\\src\\Core.Services\\Settlement\\SettlementOrchestrator.cs:line 143");
        sb.AppendLine();

        for (int i = 1; i <= 14; i++)
        {
            double sec = 40.0 + i * 2.35;
            int partition = (i * 3) % 16;
            int latency = 120 + (i * 83) % 1900;
            string status = latency > 1100 ? "WRN" : "DBG";
            sb.AppendLine($"[{Ts(sec)} {status}] [GrpcChannel-Pool] Subchannel 10.24.16.{20 + partition}:50051 state=READY | StreamId={1040 + i * 2} | Rtt={latency}ms | WindowSize=65535");
            if (i % 3 == 0)
            {
                sb.AppendLine($"      -> Header: x-envoy-upstream-service-time={latency - 4}ms, traceparent=00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
            }
        }
        sb.AppendLine();

        sb.AppendLine($"[{Ts(76.50)} ERR] [Kestrel.Http2] HttpRequestException during downstream gateway aggregation (Endpoint: https://internal-gateway.corp.local/v2/ledger/verify):");
        sb.AppendLine("System.Net.Http.HttpRequestException: The SSL connection could not be established, see inner exception.");
        sb.AppendLine(" ---> System.IO.IOException: Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host.");
        sb.AppendLine(" ---> System.Net.Sockets.SocketException (10054): An existing connection was forcibly closed by the remote host.");
        sb.AppendLine("   --- End of inner exception stack trace ---");
        sb.AppendLine("   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)");
        sb.AppendLine("   at System.Net.Security.SslStream.ReadAsyncInternal[TIOAdapter](ValueTask`1 writeTask, Memory`1 buffer)");
        sb.AppendLine("   at System.Net.Http.HttpConnection.SendAsyncCore(HttpRequestMessage request, Boolean async, CancellationToken cancellationToken)");
        sb.AppendLine("   at System.Net.Http.HttpClient.<SendAsync>g__Core|83_0(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationTokenSource cts, Boolean disposeCts, CancellationTokenSource pendingRequestsCts, CancellationToken originalCancellationToken)");
        sb.AppendLine("   at Internal.Gateways.LedgerGatewayClient.VerifyTransactionSignatureAsync(LedgerVerifyRequest payload, CancellationToken ct) in D:\\src\\Core.Gateways\\LedgerGatewayClient.cs:line 118");
        sb.AppendLine("   at Internal.Api.Controllers.LedgerAuditController.PostAuditBatchAsync(AuditBatchDto request, CancellationToken ct) in D:\\src\\Core.Api\\Controllers\\LedgerAuditController.cs:line 79");
        sb.AppendLine("   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskOfIActionResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)");
        sb.AppendLine("   at Microsoft.AspNetCore.Routing.EndpointMiddleware.<Invoke>g__AwaitRequestTask|6_0(Endpoint endpoint, Task requestTask, ILogger logger)");
        sb.AppendLine();

        sb.AppendLine($"[{Ts(82.10)} WRN] [CircuitBreaker] Policy 'SqlShard02-Circuit' state transitioned:OPEN -> HALF-OPEN. Next health probe scheduled in 15,000ms.");
        sb.AppendLine($"[{Ts(85.00)} INF] [GC-Snapshot] Heap statistics dump:");
        sb.AppendLine("      Gen0 Collections : 248   | Gen0 Size : 14,280,192 bytes");
        sb.AppendLine("      Gen1 Collections : 64    | Gen1 Size :  6,104,832 bytes");
        sb.AppendLine("      Gen2 Collections : 7     | Gen2 Size : 32,891,400 bytes");
        sb.AppendLine("      LOH  Size        : 4,194,304 bytes | Pinned Objects : 12 | Fragmentation : 3.42%");
        sb.AppendLine("      ThreadPool       : WorkerThreads=16/32767, CompletionPortThreads=4/1000, PendingWorkItems=3");
        sb.AppendLine();

        for (int j = 1; j <= 18; j++)
        {
            double sec = 88.0 + j * 1.8;
            sb.AppendLine($"[{Ts(sec)} INF] [AuditWorker-{j % 4 + 1:00}] Reconciling journal segment #{48200 + j} (Rows=250, Checksum=0x{((48200 + j) * 2654435761L & 0xFFFFFFFF):X8}, Status=VERIFIED, Elapsed={18 + (j * 7) % 45}ms)");
        }

        sb.AppendLine();
        sb.AppendLine($"[{Ts(122.40)} INF] [HealthCheck] All background diagnostic probes completed. Waiting for next polling interval (30,000ms)...");
        return sb.ToString().TrimEnd();
    }
}
