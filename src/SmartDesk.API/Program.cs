using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using SmartDesk.API.Hubs;
using SmartDesk.API.Middleware;
using SmartDesk.API.OpenApi;
using SmartDesk.API.RateLimiting;
using SmartDesk.API.Services;
using SmartDesk.Application;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Infrastructure;
using SmartDesk.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Enums go over the wire as names ("Active", "Maintenance"), not as numbers the frontend
// would have to keep in sync by hand. Set on both option types: MVC serializes controller
// responses with the first, but the OpenAPI generator builds its schemas from the second,
// so setting only one makes the documented type (integer) disagree with the real one.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // An unparseable body (e.g. "status": "Broken") still gets a 400 with the field path,
        // but not the serializer's own message, which names internal command types.
        options.AllowInputFormatterExceptionMessages = false;
    });
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecuritySchemeTransformer>();
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
// Registered before the TryAdd below, so this wins and the null notifier stays a fallback.
builder.Services.AddScoped<IReservationNotifier, SignalRReservationNotifier>();
builder.Services.TryAddScoped<IReservationNotifier, NullReservationNotifier>();

// Validated at startup by AddInfrastructure (JwtOptions.ValidateOnStart), so a missing or
// too-short key fails fast with a clear message rather than on the first request.
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key))
        };

        // SignalR's browser client can't set an Authorization header on the websocket
        // handshake, so it sends the token as a query string param instead - read it from
        // there specifically for the hub path.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

// Secure by default: any endpoint without its own [Authorize]/[AllowAnonymous] requires an
// authenticated user, so a new controller can't be left open by forgetting an attribute.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

const string FrontendCorsPolicy = "FrontendCorsPolicy";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddSmartDeskRateLimiting(builder.Configuration);

var app = builder.Build();

// Roles in every environment; demo users and resources only in Development (ADR-0007).
// Off by default: migrations are applied on purpose with `dotnet ef database update`. The
// docker-compose API service turns it on, since its database starts empty.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
}

await app.Services.SeedDatabaseAsync(includeDemoData: app.Environment.IsDevelopment());

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "SmartDesk API v1"));
}

app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

// After CORS so a 429 still carries the CORS headers the browser needs to read it; before
// authentication so rejected requests cost as little as possible.
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ReservationHub>("/hubs/reservations");
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

public partial class Program;
