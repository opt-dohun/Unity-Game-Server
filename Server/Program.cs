using CrimsonTide.Infra;
using CrimsonTide.Server.Application;
using CrimsonTide.Server.Configuration;
using CrimsonTide.Server.Service;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// 바인딩 URL은 코드가 아니라 설정에서 온다. 환경 변수(ASPNETCORE_URLS)가
// 설정(Server:Urls)보다 우선하므로 컨테이너는 자기 포트로 바인딩할 수 있다.
var serverOptions = builder.Configuration
    .GetSection(ServerOptions.SectionName)
    .Get<ServerOptions>() ?? new ServerOptions();

string urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is { Length: > 0 } environmentUrls
    ? environmentUrls
    : serverOptions.Urls;

builder.WebHost.UseUrls(urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

var storeOptions = builder.Configuration
    .GetSection(BattleStoreOptions.SectionName)
    .Get<BattleStoreOptions>() ?? new BattleStoreOptions();

// CRIMSON_DB_PATH는 문서화된 검사 절차를 위해 계속 지원하며 설정보다 우선한다.
string? overridePath = Environment.GetEnvironmentVariable("CRIMSON_DB_PATH");
if (!string.IsNullOrWhiteSpace(overridePath))
    storeOptions.DatabasePath = overridePath;

string databasePath = Path.IsPathRooted(storeOptions.DatabasePath)
    ? storeOptions.DatabasePath
    : Path.Combine(builder.Environment.ContentRootPath, storeOptions.DatabasePath);

var store = new BattleStore(databasePath);
store.Initialize();

builder.Services.AddSingleton(store);
builder.Services.AddSingleton<BattleApplicationService>();
builder.Services.AddSingleton<BattleStoreHealthCheck>();

string[] corsOrigins = builder.Configuration
    .GetSection(CorsOptions.SectionName)
    .Get<CorsOptions>()?.AllowedOrigins ?? [];

builder.Services.AddCors(options => options.AddPolicy("local-preview", policy =>
{
    if (corsOrigins.Length > 0)
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddHealthChecks()
    .AddCheck<BattleStoreHealthCheck>("battle_store", tags: ["ready"]);

var app = builder.Build();

app.Logger.LogInformation("Crimson Tide 시작 · 데이터베이스={DatabasePath}", databasePath);

app.UseCors("local-preview");
app.MapBattleEndpoints();

// 프로세스 생존 여부(/health)와 실제 트래픽 수용 준비 여부(/ready)를 분리한다.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthCheckJsonWriter.WriteAsync
});
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthCheckJsonWriter.WriteAsync
});

app.Run();
