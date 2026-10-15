using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartDesk.API.Authentication;
using SmartDesk.API.Hubs;
using SmartDesk.API.Middleware;
using SmartDesk.API.OpenApi;
using SmartDesk.API.RateLimiting;
using SmartDesk.API.Services;
using SmartDesk.Application;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Localization;
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
builder.Services.AddScoped<IInboxNotifier, SignalRInboxNotifier>();
builder.Services.TryAddScoped<IInboxNotifier, NullInboxNotifier>();
builder.Services.AddScoped<IIssueNotifier, SignalRIssueNotifier>();
builder.Services.TryAddScoped<IIssueNotifier, NullIssueNotifier>();
builder.Services.AddHostedService<ReservationReminderWorker>();
builder.Services.AddHostedService<NoShowReleaseWorker>();
builder.Services.AddHostedService<ReleaseWarningWorker>();
builder.Services.AddHostedService<EndingSoonNoticeWorker>();
builder.Services.AddHostedService<ApprovalLapseWorker>();
// Replaces Infrastructure's no-op fallback: a change to an account cuts its hub connections.
builder.Services.AddSingleton<HubConnectionRegistry>();
builder.Services.AddSingleton<IRealtimeSessions>(sp => sp.GetRequiredService<HubConnectionRegistry>());

// Validated at startup by AddInfrastructure (JwtOptions.ValidateOnStart), so a missing or
// too-short key fails fast with a clear message rather than on the first request.
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = JwtBearerSetup.ValidationParameters(jwtOptions);

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
            },
            OnTokenValidated = JwtBearerSetup.RequireCurrentAccessAsync,
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

// Behind a reverse proxy, the client's address and the scheme it used arrive in X-Forwarded-For
// and X-Forwarded-Proto (ADR-0028). Anyone can send those headers, so they're believed only
// from the proxies listed in ForwardedHeaders:KnownProxies (addresses) or KnownNetworks (CIDR
// ranges, e.g. a Docker network). With neither set, only a proxy on this machine is trusted.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    }

    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

var app = builder.Build();

// Roles in every environment; demo users and resources only in Development (ADR-0007).
// Off by default: migrations are applied on purpose with `dotnet ef database update`. The
// docker-compose API service turns it on, since its database starts empty.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
}

await app.Services.SeedDatabaseAsync(includeDemoData: app.Environment.IsDevelopment());

// `--demo-data`: a lived-in office for a demo (ADR-0047) - people, floor plans, six weeks of
// bookings - written once into a database with no bookings yet, then the API stops; exit code 1
// when it wrote nothing, and the log says why. It writes past the API's checks, so never
// anywhere but Development.
if (args.Contains("--demo-data"))
{
    if (!app.Environment.IsDevelopment())
    {
        Console.Error.WriteLine("--demo-data is for Development only.");
        Environment.ExitCode = 1;
        return;
    }

    Environment.ExitCode = await app.Services.SeedDemoDataAsync() ? 0 : 1;
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "SmartDesk API v1"));
}

// First, so everything after it - the sign-in rate limit, HSTS, the HTTPS redirect - sees the
// client's address and scheme rather than a reverse proxy's (ADR-0028).
app.UseForwardedHeaders();

// Then the language, so everything after it - errors included - answers in the request's language
// (ADR-0023). Accept-Language picks Turkish or English; no header, or any other language, is
// English. Only the UI culture follows it, which is what messages are looked up in. Formatting
// stays English, so a Turkish request can't change how the code parses, formats or compares
// text (the dotted and dotless "i").
app.UseRequestLocalization(options =>
{
    options.DefaultRequestCulture = new RequestCulture(Languages.English);
    options.SupportedCultures = [CultureInfo.GetCultureInfo(Languages.English)];
    options.SupportedUICultures = [.. Languages.Supported.Select(CultureInfo.GetCultureInfo)];
    options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
});

app.UseExceptionHandler();

// Tells browsers to reach this host over HTTPS only from then on. Sent on HTTPS responses
// outside Development; the middleware leaves localhost out in any case.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

// After CORS so a 429 still carries the CORS headers the browser needs to read it, and after
// authentication so the assistant's limit counts per user (ADR-0029). Sign-in requests carry
// no token, so authenticating them first costs nothing.
app.UseRateLimiter();

app.UsePasswordChangeGate();

app.MapControllers();
app.MapHub<ReservationHub>("/hubs/reservations");
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

public partial class Program;
