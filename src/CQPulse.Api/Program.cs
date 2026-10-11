using CQPulse.Infrastructure;
using CQPulse.Application.Simulation;
using CQPulse.Api.Hubs;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddCQPulseInfrastructure(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddScoped<IRunProgressPublisher, SignalRRunProgressPublisher>();
builder.Services.AddCors(options => options.AddPolicy("client", policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:4200"])
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors("client");
app.UseAuthorization();
app.MapControllers();
app.MapHub<RunsHub>("/hubs/runs");
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapOpenApi();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await CQPulse.Infrastructure.Persistence.DevelopmentDataSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<CQPulse.Infrastructure.Persistence.CQPulseDbContext>(),
        CancellationToken.None);
}

app.Run();

public partial class Program;
