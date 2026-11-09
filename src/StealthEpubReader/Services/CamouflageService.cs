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
    /// 產生逼真的 C# / SQL Exception StackTrace 假報錯畫面（老闆鍵 Panic 模式專用）
    /// </summary>
    public static string GenerateFakeExceptionScreen()
    {
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        return $$"""
            [{{now}} ERR] [ConnectionPool-12] Unhandled exception in background worker pipeline:
            Microsoft.Data.SqlClient.SqlException (0x80131904): Execution Timeout Expired. The timeout period elapsed prior to completion of the operation or the server is not responding.
             ---> System.ComponentModel.Win32Exception (258): The wait operation timed out.
               at Microsoft.Data.SqlClient.SqlConnection.OnError(SqlException exception, Boolean breakConnection, Action`1 wrapCloseInAction)
               at Microsoft.Data.SqlClient.TdsParser.ThrowExceptionAndWarning(TdsParserStateObject stateObj, Boolean callerHasConnectionLock, Boolean asyncClose)
               at Microsoft.Data.SqlClient.TdsParserStateObject.ReadSniError(TdsParserStateObject stateObj, UInt32 error)
               at Microsoft.Data.SqlClient.SqlCommand.InternalExecuteNonQuery(TaskCompletionSource`1 completion, Boolean sendToPipe, Int32 timeout)
               at Internal.Infrastructure.Persistence.SqlRepository`1.ExecuteBatchWithRetryAsync(IReadOnlyList`1 entities, CancellationToken ct) in D:\src\Core.Infrastructure\Persistence\SqlRepository.cs:line 184
               at Internal.Services.OrderProcessing.BatchSyncWorker.ProcessPendingQueueAsync(CancellationToken stoppingToken) in D:\src\Core.Services\Workers\BatchSyncWorker.cs:line 92
            ClientConnectionId: 4a9e18f0-7c2b-4d3e-9a11-08d9f4c21e03
            Error Number: -2, State: 0, Class: 11

            [{{now}} WRN] [RetryPolicy] CircuitBreaker state transitioned to HALF-OPEN. Next health probe scheduled in 15,000ms.
            [{{now}} INF] [GC-Monitor] Gen0=142, Gen1=28, Gen2=4 | Allocated=48.2 MB | ThreadPool Active=8, Pending=0
            """;
    }
}
