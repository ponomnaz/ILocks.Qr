using System.Text;
using Application.Security;
using Application.Workflows.Auth;
using Application.Workflows.Qr;
using Application.Workflows.Telegram;
using FluentValidation;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Infrastructure.Workflows;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace Api.Configuration;

internal static class ServiceCollectionExtensions
{
    private const int AccessTokenExpiresInHours = 12;

    public static IServiceCollection AddApiFoundation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            };
        });
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ILocks.Qr API",
                Version = "v1",
                Description = "Backend API for OTP login, QR generation/history, and Telegram delivery."
            });

            var bearerScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "JWT Bearer token. Example: Bearer {token}",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            };

            options.AddSecurityDefinition("Bearer", bearerScheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    bearerScheme,
                    Array.Empty<string>()
                }
            });
        });
        services.AddValidatorsFromAssemblyContaining<Program>();

        return services;
    }

    public static IServiceCollection AddApiInfrastructure(
        this IServiceCollection services,
        ApiRuntimeSettings settings)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(settings.ConnectionString);
        });
        services.AddScoped<IAuthWorkflow, AuthWorkflow>();
        services.AddScoped<IQrWorkflow, QrWorkflow>();
        services.AddScoped<ITelegramWorkflow, TelegramWorkflow>();
        services.AddSingleton<IOtpService, OtpService>();
        services.AddSingleton<IQrCodeService, QrCodeService>();
        services.AddSingleton<ITelegramQrSender, TelegramQrSender>();

        return services;
    }

    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        ApiRuntimeSettings settings,
        SymmetricSecurityKey signingKey)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateIssuer = true,
                    ValidIssuer = settings.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = settings.JwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddSingleton<IJwtTokenService>(_ =>
            new JwtTokenService(
                settings.JwtIssuer,
                settings.JwtAudience,
                signingKey,
                TimeSpan.FromHours(AccessTokenExpiresInHours)));

        services.AddAuthorization();
        return services;
    }

    public static ApiRuntimeSettings GetApiRuntimeSettings(this IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        }

        var jwtIssuer = configuration["Jwt:Issuer"];
        var jwtAudience = configuration["Jwt:Audience"];
        var jwtKey = configuration["Jwt:Key"];
        var telegramBotToken = configuration["Telegram:BotToken"];

        if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience) || string.IsNullOrWhiteSpace(jwtKey))
        {
            throw new InvalidOperationException("Jwt settings are not configured.");
        }

        if (jwtKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key must contain at least 32 characters.");
        }

        if (telegramBotToken is null)
        {
            throw new InvalidOperationException("Telegram:BotToken key is not configured.");
        }

        return new ApiRuntimeSettings(connectionString, jwtIssuer, jwtAudience, jwtKey, telegramBotToken);
    }

    public static SymmetricSecurityKey CreateJwtSigningKey(this ApiRuntimeSettings settings)
    {
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.JwtKey));
    }
}

internal sealed record ApiRuntimeSettings(
    string ConnectionString,
    string JwtIssuer,
    string JwtAudience,
    string JwtKey,
    string TelegramBotToken);
