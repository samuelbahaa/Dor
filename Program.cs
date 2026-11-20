using System.Text;
using System.Text.Json.Serialization;
using Dor.Api.Data;
using Dor.Api.Hubs;
using Dor.Api.Options;
using Dor.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Settings ----------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection("Queue"));

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

// Fail fast: never run with a missing or weak signing key.
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.StartsWith("SET_ME") || jwt.Key.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is missing or too weak. Set it with: dotnet user-secrets set \"Jwt:Key\" \"<random string, 32+ chars>\"");
}

// ---------- Database ----------
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// ---------- Our services ----------
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<QueueService>();
builder.Services.AddSingleton<QueueNotifier>();      // talks to browsers through SignalR
builder.Services.AddHostedService<QueueMaintenanceService>();   // releases no-shows in the background

// ---------- Web API + real time ----------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddOpenApi();

// ---------- Authentication (JWT) ----------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // A browser cannot add an "Authorization" header to a WebSocket connection,
        // so SignalR sends the token in the URL (?access_token=...). We accept it ONLY for /hubs.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = token;

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// ---------- CORS (only while developing) ----------
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(o => o.AddPolicy("Dev", p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();   // interactive docs at /scalar
    app.UseCors("Dev");
}
else
{
    app.UseHttpsRedirection();
}

app.UseDefaultFiles();   // serves wwwroot/index.html at /
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<QueueHub>("/hubs/queue");

// In development: apply migrations automatically and fill an empty DB with demo data.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.Run();
