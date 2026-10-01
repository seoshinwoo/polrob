using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace polrob.Server.Operations;

public static class OperationsEndpoints
{
    public static void AddRequestLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    context.Request.Path.StartsWithSegments("/hubs")
                    ? RateLimitPartition.GetNoLimiter("hub-connections-have-their-own-cap")
                    : RateLimitPartition.GetConcurrencyLimiter("http", _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = Math.Max(1, configuration.GetValue("Limits:HttpConcurrentRequests", 128)),
                        QueueLimit = 0
                    })),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    var auth = context.Request.Path.StartsWithSegments("/auth");
                    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter($"{auth}:{ip}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, configuration.GetValue(auth
                            ? "Limits:AuthRequestsPerMinute" : "Limits:HttpRequestsPerMinute", auth ? 30 : 600)),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
                }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.RequestServices.GetRequiredService<OperationalMetrics>().Add("http_rate_limited_total");
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsJsonAsync(new { code = "rate_limited", message = "요청이 너무 많습니다." }, cancellationToken);
            };
        });
    }

    public static void MapOperations(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).DisableRateLimiting();
        app.MapGet("/health/ready", (ServerAdmission admission) => admission.IsReady
            ? Results.Ok(new { status = "ready" })
            : Results.Json(new { status = "not_ready" }, statusCode: 503)).DisableRateLimiting();

        var ops = app.MapGroup("/ops").DisableRateLimiting();
        ops.AddEndpointFilter(async (context, next) =>
        {
            var expected = app.Configuration["Operations:ApiKey"];
            var provided = context.HttpContext.Request.Headers["X-Operations-Key"].ToString();
            if (!IsAuthorized(expected, provided)) return Results.StatusCode(403);
            return await next(context);
        });
        ops.MapGet("/metrics", (OperationalMetrics metrics, GameRecordWriter writer,
            ServerAdmission admission, ServerOperations operations) =>
        {
            writer.PublishMetrics();
            metrics.Set("ready", admission.IsReady ? 1 : 0);
            metrics.Set("draining", operations.IsDraining ? 1 : 0);
            metrics.Set("uptime_seconds", (DateTime.UtcNow - operations.StartedAtUtc).TotalSeconds);
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            metrics.Set("process_working_set_bytes", process.WorkingSet64);
            metrics.Set("process_cpu_seconds_total", process.TotalProcessorTime.TotalSeconds);
            metrics.Set("managed_heap_bytes", GC.GetTotalMemory(false));
            metrics.Set("threadpool_pending", ThreadPool.PendingWorkItemCount);
            return Results.Text(metrics.Export(), "text/plain; version=0.0.4; charset=utf-8");
        });
        ops.MapGet("/status", (ServerOperations operations, ServerAdmission admission, GameRecordOutbox outbox) => Results.Ok(new
        {
            operations.BootId, operations.StartedAtUtc, operations.PreviousShutdownClean,
            operations.IsDraining, admission.IsReady, outbox = outbox.Snapshot(),
            restartPolicy = "abandon-unfinished-relogin-rematch; replay-durable-completed-records"
        }));
        ops.MapPost("/drain", (ServerOperations operations) =>
        {
            operations.BeginDrain();
            return Results.Ok(new { status = "draining", message = "New games are blocked; existing games continue until process shutdown." });
        });
    }

    public static bool IsAuthorized(string? expected, string? provided)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(provided)) return false;
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(provided);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
