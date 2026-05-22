using System.Text;
using FitQuest.Api.Data;
using FitQuest.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);
ConfigureRenderPort(builder);

// Service registration
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    });

var databaseProvider = Environment.GetEnvironmentVariable("DATABASE_PROVIDER")
    ?? builder.Configuration["Database:Provider"]
    ?? "sqlite";
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=fitness.db";

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (string.Equals(databaseProvider, "postgres", StringComparison.OrdinalIgnoreCase)
        || string.Equals(databaseProvider, "postgresql", StringComparison.OrdinalIgnoreCase))
    {
        ValidatePostgresConnectionString(connectionString);
        options.UseNpgsql(ToNpgsqlConnectionString(connectionString), npgsqlOptions =>
        {
            npgsqlOptions.CommandTimeout(120);
            npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
        });
        return;
    }

    options.UseSqlite(connectionString);
});
builder.Services.AddScoped<JwtTokenService>();

var jwtSecret = JwtTokenService.GetJwtSecret(builder.Configuration);
var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtKey,
            ValidateIssuer = true,
            ValidIssuer = JwtTokenService.GetJwtIssuer(builder.Configuration),
            ValidateAudience = true,
            ValidAudience = JwtTokenService.GetJwtAudience(builder.Configuration),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

// HttpClient for the configured AI provider. DeepSeek currently uses an OpenAI-compatible API.
builder.Services.AddHttpClient<IAiService, DeepSeekService>(client =>
{
    var baseUrl = builder.Configuration["Ai:BaseUrl"]
        ?? Environment.GetEnvironmentVariable("AI_BASE_URL")
        ?? "https://api.deepseek.com";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(120);
});

// CORS for the Vite development server.
var frontendOrigin = builder.Configuration["Cors:FrontendOrigin"] ?? "http://localhost:5173";
frontendOrigin = Environment.GetEnvironmentVariable("FRONTEND_ORIGIN") ?? frontendOrigin;
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(frontendOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// HTTP pipeline
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsNpgsql())
    {
        EnsurePostgresSchema(db);
    }
    else
    {
        db.Database.EnsureCreated();
        EnsureProfileCompatibilityColumns(db);
    }
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Health checks
app.MapGet("/health", (IConfiguration cfg) => Results.Ok(new
{
    status = "ok",
    provider = cfg["Ai:Provider"] ?? Environment.GetEnvironmentVariable("AI_PROVIDER") ?? "DeepSeek",
    model = cfg["Ai:Model"] ?? Environment.GetEnvironmentVariable("AI_MODEL"),
    ai_configured = HasAiApiKey(cfg)
}));
app.MapGet("/api/health", (IConfiguration cfg) => Results.Ok(new
{
    status = "ok",
    provider = cfg["Ai:Provider"] ?? Environment.GetEnvironmentVariable("AI_PROVIDER") ?? "DeepSeek",
    model = cfg["Ai:Model"] ?? Environment.GetEnvironmentVariable("AI_MODEL"),
    ai_configured = HasAiApiKey(cfg)
}));

app.Run();

static void LoadDotEnv()
{
    var envPath = FindDotEnv();
    if (!File.Exists(envPath)) return;

    foreach (var rawLine in File.ReadAllLines(envPath))
    {
        var line = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0) continue;

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(key)) continue;
        if (Environment.GetEnvironmentVariable(key) is null)
            Environment.SetEnvironmentVariable(key, value);
    }
}

static void ConfigureRenderPort(WebApplicationBuilder builder)
{
    var port = Environment.GetEnvironmentVariable("PORT");
    if (string.IsNullOrWhiteSpace(port)) return;

    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

static string FindDotEnv()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, ".env");
        if (File.Exists(candidate)) return candidate;
        directory = directory.Parent;
    }

    return Path.Combine(Directory.GetCurrentDirectory(), ".env");
}

static bool HasAiApiKey(IConfiguration cfg)
{
    var values = new[]
    {
        cfg["Ai:ApiKey"],
        Environment.GetEnvironmentVariable("AI_API_KEY"),
    };

    return values.Any(value => !string.IsNullOrWhiteSpace(value));
}

static string ToNpgsqlConnectionString(string connectionString)
{
    if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        && !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        return connectionString;

    var uri = new Uri(connectionString);
    var userInfo = uri.UserInfo.Split(':', 2);
    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = uri.AbsolutePath.TrimStart('/'),
        Username = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? ""),
        Password = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? ""),
        SslMode = SslMode.Require,
        Pooling = true,
    };

    return builder.ConnectionString;
}

static void ValidatePostgresConnectionString(string connectionString)
{
    var isPostgresUri = connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);
    var isKeyValueString = connectionString.StartsWith("Host=", StringComparison.OrdinalIgnoreCase)
        || connectionString.StartsWith("Server=", StringComparison.OrdinalIgnoreCase);

    if (isPostgresUri || isKeyValueString) return;

    throw new InvalidOperationException(
        "DATABASE_PROVIDER is postgres, but DATABASE_URL is not a valid PostgreSQL connection string. " +
        "Use the full Supabase connection string, for example postgresql://user:password@host:5432/postgres.");
}

static void EnsurePostgresSchema(AppDbContext db)
{
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS users (
            id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            name character varying(120) NOT NULL,
            email character varying(255) NOT NULL,
            password_hash character varying(255) NOT NULL,
            created_at timestamp with time zone NOT NULL,
            updated_at timestamp with time zone NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ix_users_email ON users (email);

        CREATE TABLE IF NOT EXISTS user_profiles (
            id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            user_id integer NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            experience_level character varying(40) NOT NULL,
            goal character varying(40) NOT NULL,
            gender character varying(30) NOT NULL DEFAULT 'not_specified',
            height_cm double precision NULL,
            weight_kg double precision NULL,
            language character varying(20) NOT NULL DEFAULT 'system',
            created_at timestamp with time zone NOT NULL,
            updated_at timestamp with time zone NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ix_user_profiles_user_id ON user_profiles (user_id);

        CREATE TABLE IF NOT EXISTS training_sessions (
            id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            user_id integer NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            session_date date NOT NULL,
            muscle_group character varying(40) NOT NULL,
            day_type character varying(120) NOT NULL,
            duration_minutes integer NOT NULL,
            ai_note text NULL,
            created_at timestamp with time zone NOT NULL,
            updated_at timestamp with time zone NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_training_sessions_user_id_session_date
            ON training_sessions (user_id, session_date);

        CREATE TABLE IF NOT EXISTS session_exercises (
            id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            session_id integer NOT NULL REFERENCES training_sessions(id) ON DELETE CASCADE,
            exercise_name character varying(160) NOT NULL,
            category character varying(40) NOT NULL,
            sets integer NOT NULL,
            reps integer NOT NULL,
            weight double precision NULL,
            unit character varying(10) NULL,
            rationale text NULL,
            sort_order integer NOT NULL,
            created_at timestamp with time zone NOT NULL,
            updated_at timestamp with time zone NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_session_exercises_session_id_sort_order
            ON session_exercises (session_id, sort_order);

        CREATE TABLE IF NOT EXISTS ai_plan_requests (
            id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            user_id integer NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            provider character varying(80) NOT NULL,
            model character varying(120) NOT NULL,
            request_type character varying(40) NOT NULL,
            prompt_snapshot text NOT NULL,
            response_snapshot text NULL,
            status character varying(40) NOT NULL,
            error_message text NULL,
            created_at timestamp with time zone NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_ai_plan_requests_user_id_created_at
            ON ai_plan_requests (user_id, created_at);
        """);
}

static void EnsureProfileCompatibilityColumns(AppDbContext db)
{
    if (!db.Database.IsSqlite()) return;

    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;
    if (shouldClose) connection.Open();

    try
    {
        EnsureUserProfileColumn(connection, "language", "TEXT NOT NULL DEFAULT 'system'");
        EnsureUserProfileColumn(connection, "gender", "TEXT NOT NULL DEFAULT 'not_specified'");
    }
    finally
    {
        if (shouldClose) connection.Close();
    }
}

static void EnsureUserProfileColumn(System.Data.Common.DbConnection connection, string columnName, string columnDefinition)
{
    using var check = connection.CreateCommand();
    check.CommandText = "PRAGMA table_info(user_profiles);";
    using var reader = check.ExecuteReader();

    while (reader.Read())
    {
        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            return;
    }

    using var alter = connection.CreateCommand();
    alter.CommandText = $"ALTER TABLE user_profiles ADD COLUMN {columnName} {columnDefinition};";
    alter.ExecuteNonQuery();
}
