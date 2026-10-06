using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using CrimsonTide.Infra;
using CrimsonTide.Server.Application;
using CrimsonTide.Server.Schema;

namespace CrimsonTide.Server.Service;

public static class BattleEndpoints
{
    public static void MapBattleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/battles", async (HttpRequest request, BattleApplicationService service) =>
        {
            string key = request.Headers["Idempotency-Key"].ToString();
            return await service.CreateBattleAsync(key);
        });

        app.MapGet("/api/battles/{id}", (string id, BattleApplicationService service) =>
        {
            return service.GetBattle(id);
        });

        app.MapPost("/api/battles/{id}/turns", async (string id, TurnRequest input, BattleApplicationService service) =>
        {
            return await service.PlayTurnAsync(id, input);
        });

        app.MapPost("/api/battles/{id}/score", async (string id, RegisterRequest input, BattleApplicationService service) =>
        {
            return await service.RegisterScoreAsync(id, input);
        });

        app.MapGet("/api/scores", (BattleApplicationService service) =>
        {
            return service.GetScores();
        });

        // 부하 테스트 계측 스냅샷. 모든 값은 ms이며, 수집 시작(프로세스 기동)부터
        // 누적된 통계다. k6 스크립트가 테스트 시작/끝 시점에 호출해 구간별 차를 계산한다.
        app.MapGet("/api/server/metrics", (ServerMetrics metrics) =>
        {
            return Results.Ok(new
            {
                metrics = metrics.Snapshot().ToDictionary(
                    kv => kv.Key,
                    kv => new
                    {
                        count = kv.Value.Count,
                        meanMs = Math.Round(kv.Value.MeanMs, 3),
                        p50Ms = Math.Round(kv.Value.P50Ms, 3),
                        p95Ms = Math.Round(kv.Value.P95Ms, 3),
                        p99Ms = Math.Round(kv.Value.P99Ms, 3),
                        maxMs = Math.Round(kv.Value.MaxMs, 3)
                    })
            });
        });

        // 부하 테스트 구간을 깔끔하게 분리하기 위해 표본을 초기화한다.
        app.MapPost("/api/server/metrics/reset", (ServerMetrics metrics) =>
        {
            metrics.Clear();
            return Results.Ok(new { ok = true });
        });
    }
}
