using CrimsonTide.Infra;
using CrimsonTide.Server.Application;
using CrimsonTide.Server.Service;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:8001");

var dbPath = Environment.GetEnvironmentVariable("CRIMSON_DB_PATH")
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "crimson-tide.db");
var store = new BattleStore(dbPath);
store.Initialize();

builder.Services.AddSingleton(store);
builder.Services.AddSingleton<BattleApplicationService>();

builder.Services.AddCors(options => options.AddPolicy("local-preview", policy => policy
    .WithOrigins("http://localhost:8000", "http://127.0.0.1:8000")
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors("local-preview");

app.MapBattleEndpoints();

app.Run();
