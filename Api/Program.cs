using Api.Configuration;
using Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var runtimeSettings = builder.Configuration.GetApiRuntimeSettings();
var signingKey = runtimeSettings.CreateJwtSigningKey();

builder.Services
    .AddApiFoundation()
    .AddApiInfrastructure(runtimeSettings)
    .AddApiAuthentication(runtimeSettings, signingKey);

var app = builder.Build();

app.UseApiPipeline();

app.MapAuthEndpoints();
app.MapTelegramEndpoints();
app.MapQrEndpoints();
app.MapHealthEndpoints();

app.Run();
