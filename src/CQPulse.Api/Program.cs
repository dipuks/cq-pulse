using CQPulse.Infrastructure;
using CQPulse.Application.Simulation;
using CQPulse.Api.Hubs;
using CQPulse.Api.Auth;
using CQPulse.Api.Errors;
using CQPulse.Api.Observability;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Json.Serialization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<CQPulse.Application.Common.ICorrelationContext, HttpCorrelationContext>();
builder.Services.AddCQPulseInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CQPulse.Infrastructure.Persistence.CQPulseDbContext>("database", tags: ["ready"]);
// SignalR has its own JSON settings; match the REST API so statuses arrive as names, not numbers.
builder.Services.AddSignalR().AddJsonProtocol(options =>
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddScoped<IRunProgressPublisher, SignalRRunProgressPublisher>();
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || jwtOptions.SigningKey.Length < 32)
    throw new InvalidOperationException("Configure a 32-character-or-longer Jwt:SigningKey through user secrets.");
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<TokenService>();
var loginRateLimit = builder.Configuration.GetSection(LoginRateLimit.SectionName).Get<LoginRateLimit>() ?? new LoginRateLimit();
builder.Services.AddRateLimiter(options => LoginRateLimit.Configure(options, loginRateLimit));
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true, ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrWhiteSpace(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs/runs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddCors(options => options.AddPolicy("client", policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:4200"])
        .AllowAnyHeader()
        .AllowAnyMethod()
        // The SignalR browser client sends credentials on negotiate by default; origins are explicit, so this is allowed.
        .AllowCredentials()));

var app = builder.Build();

// Operator command (see docs/operations.md): fill stored failure signatures, then exit without serving requests.
if (args.Contains(CQPulse.Infrastructure.Persistence.SignatureBackfillCommand.Name, StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await CQPulse.Infrastructure.Persistence.SignatureBackfillCommand.RunAsync(app.Services, Console.Out);
    return;
}

app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors("client");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapHub<RunsHub>("/hubs/runs");
// Liveness: the process is up. Readiness: dependencies (the database) are reachable.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await using var scope = app.Services.CreateAsyncScope();
    await CQPulse.Infrastructure.Persistence.DevelopmentDataSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<CQPulse.Infrastructure.Persistence.CQPulseDbContext>(),
        CancellationToken.None);
    await DevelopmentIdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration, app.Logger);
}

app.Run();

public partial class Program;
