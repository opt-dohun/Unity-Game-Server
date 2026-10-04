using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
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
    }
}
