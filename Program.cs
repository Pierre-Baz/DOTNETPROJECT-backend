using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NetManage.Api.Configuration;
using NetManage.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInMemoryCollection(LoadLocalEnvironmentFiles(builder.Environment.ContentRootPath));

const string FrontendCorsPolicy = "FrontendCorsPolicy";

var mongoDbSettings = LoadMongoDbSettings(builder.Configuration);
var jwtSettings = LoadJwtSettings(builder.Configuration);
var frontendUrl = builder.Configuration["FRONTEND_URL"]
    ?? builder.Configuration["FrontendUrl"]
    ?? "http://localhost:3000";

builder.Services.AddSingleton(mongoDbSettings);
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<MongoDbContext>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy
            .WithOrigins(frontendUrl)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter a JWT bearer token.",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = JwtBearerDefaults.AuthenticationScheme
        }
    };

    options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, securityScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            securityScheme,
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

await app.Services.GetRequiredService<MongoDbContext>().EnsureIndexesAsync();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new
    {
        status = "ok",
        app = "NetManage.Api"
    }))
    .WithName("GetHealth")
    .WithOpenApi();

app.Run();

static Dictionary<string, string?> LoadLocalEnvironmentFiles(string contentRootPath)
{
    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    var envPath = Path.Combine(contentRootPath, ".env");

    if (!File.Exists(envPath))
    {
        return values;
    }

    foreach (var rawLine in File.ReadAllLines(envPath))
    {
        var line = rawLine.Trim();

        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
        {
            continue;
        }

        if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
        {
            line = line["export ".Length..].TrimStart();
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim().Trim('"', '\'');

        if (Environment.GetEnvironmentVariable(key) is null)
        {
            values[key] = value;
        }
    }

    return values;
}

static MongoDbSettings LoadMongoDbSettings(IConfiguration configuration)
{
    var settings = configuration.GetSection(MongoDbSettings.SectionName).Get<MongoDbSettings>() ?? new MongoDbSettings();
    settings.ConnectionString = configuration["MONGODB_CONNECTION_STRING"] ?? settings.ConnectionString;
    settings.DatabaseName = configuration["MONGODB_DATABASE_NAME"] ?? settings.DatabaseName;

    if (string.IsNullOrWhiteSpace(settings.ConnectionString))
    {
        throw new InvalidOperationException("MongoDB connection string is not configured.");
    }

    if (string.IsNullOrWhiteSpace(settings.DatabaseName))
    {
        throw new InvalidOperationException("MongoDB database name is not configured.");
    }

    return settings;
}

static JwtSettings LoadJwtSettings(IConfiguration configuration)
{
    var settings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
    settings.Secret = configuration["JWT_SECRET"] ?? settings.Secret;
    settings.Issuer = configuration["JWT_ISSUER"] ?? settings.Issuer;
    settings.Audience = configuration["JWT_AUDIENCE"] ?? settings.Audience;

    if (string.IsNullOrWhiteSpace(settings.Secret) || settings.Secret.Length < 32)
    {
        throw new InvalidOperationException("JWT secret must be configured and be at least 32 characters long.");
    }

    if (string.IsNullOrWhiteSpace(settings.Issuer))
    {
        throw new InvalidOperationException("JWT issuer is not configured.");
    }

    if (string.IsNullOrWhiteSpace(settings.Audience))
    {
        throw new InvalidOperationException("JWT audience is not configured.");
    }

    return settings;
}
