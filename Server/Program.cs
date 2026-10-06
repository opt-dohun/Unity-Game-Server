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

// CRIMSON_DB_PATH는 SQLite 경로 override, CRIMSON_MYSQL_CONNECTION_STRING은 배포 비밀값이다.
string? overridePath = Environment.GetEnvironmentVariable("CRIMSON_DB_PATH");
if (!string.IsNullOrWhiteSpace(overridePath))
    storeOptions.DatabasePath = overridePath;
string? mysqlConnectionString = Environment.GetEnvironmentVariable("CRIMSON_MYSQL_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("BattleStore")
    ?? storeOptions.MySqlConnectionString;
if (builder.Environment.IsProduction() && !storeOptions.Provider.Equals("mysql", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Production 환경은 BattleStore:Provider=mysql로 설정해야 합니다.");

string databasePath = Path.IsPathRooted(storeOptions.DatabasePath)
    ? storeOptions.DatabasePath
    : Path.Combine(builder.Environment.ContentRootPath, storeOptions.DatabasePath);

var store = new BattleStore(storeOptions.Provider, databasePath, mysqlConnectionString,
    storeOptions.LockMode, storeOptions.JournalMode);
store.Initialize();

var serverMetrics = new ServerMetrics();

builder.Services.AddSingleton(store);
builder.Services.AddSingleton(serverMetrics);
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

app.Logger.LogInformation("Crimson Tide 시작 · 저장소={Provider} · journal={JournalMode} · 락모드={LockMode}",
    store.Provider, store.JournalMode, storeOptions.LockMode);

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
