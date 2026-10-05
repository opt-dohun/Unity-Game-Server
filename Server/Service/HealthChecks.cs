using System.Globalization;
using CrimsonTide.Infra;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrimsonTide.Server.Service;

/// <summary>SQLite 저장소에 실제로 접속해 읽기가 되는지 확인하는 준비 상태(readiness) 검사.</summary>
public sealed class BattleStoreHealthCheck(BattleStore store) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = store.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            long probe = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            return Task.FromResult(probe == 1
                ? HealthCheckResult.Healthy("SQLite에 연결할 수 있습니다.")
                : HealthCheckResult.Unhealthy($"SQLite가 예상 밖의 값을 반환했습니다: {probe}"));
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("SQLite에 연결할 수 없습니다.", exception));
        }
    }
}

/// <summary>헬스 검사 결과를 JSON으로 돌려주는 응답 기록기.</summary>
public static class HealthCheckJsonWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 3)
            })
        };
        return context.Response.WriteAsJsonAsync(payload);
    }
}
