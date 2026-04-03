using DomoLibri.Application.Interfaces;
using DomoLibri.Application.Services;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Domain.Settings;
using DomoLibri.Infrastructure.Services;
using DomoLibri.Infrastructure.Data;
using DomoLibri.Api;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// JWT Secret validation
// Precedence: env var (Jwt__Secret) > User Secrets (dev) > appsettings.json
// In production the secret MUST be injected via environment variable.
// Example: export Jwt__Secret="your-strong-secret-min-32-chars"
// ---------------------------------------------------------------------------
var jwtSecret = builder.Configuration["Jwt:Secret"];
var jwtSecretValid = !string.IsNullOrWhiteSpace(jwtSecret) && jwtSecret.Length >= 32;

if (!jwtSecretValid)
{
    const string msg = "Jwt:Secret não está configurado ou tem menos de 32 caracteres. " +
                       "Em produção, defina a variável de ambiente Jwt__Secret. " +
                       "Em desenvolvimento, use: dotnet user-secrets set \"Jwt:Secret\" \"<segredo>\"";

    if (builder.Environment.IsProduction())
        throw new InvalidOperationException(msg);

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Error.WriteLine($"[AVISO DE SEGURANÇA] {msg}");
    Console.ResetColor();
}
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// CORS Origins validation
// Configure: AllowedOrigins=https://app.domolibri.com.br,https://www.domolibri.com.br
// In production this MUST be set via environment variable (AllowedOrigins=...).
// ---------------------------------------------------------------------------
var allowedOriginsRaw = builder.Configuration["AllowedOrigins"];
var allowedOrigins = allowedOriginsRaw?
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (allowedOrigins is null or { Length: 0 })
{
    const string msg = "AllowedOrigins não está configurado. " +
                       "Em produção, defina a variável de ambiente AllowedOrigins com as origens permitidas separadas por vírgula. " +
                       "Em desenvolvimento, adicione AllowedOrigins ao appsettings.Development.json ou User Secrets.";

    if (builder.Environment.IsProduction())
        throw new InvalidOperationException(msg);

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Error.WriteLine($"[AVISO DE SEGURANÇA] {msg}");
    Console.ResetColor();

    allowedOrigins = ["http://localhost:4200"];
}
// ---------------------------------------------------------------------------

// 1. Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // Required for HttpOnly Cookies
    });
});

// 2. Configure Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth-limit", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 5; // Allow 5 attempts per minute per IP
        opt.QueueLimit = 0;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails(options =>
{
    // RFC 9457 §3: applies to all ProblemDetails produced by ASP.NET Core infrastructure
    // (e.g. 401, 403, 404 from middleware). Controller-produced Problem() calls are
    // handled individually below.
    options.CustomizeProblemDetails = ctx =>
    {
        var status = ctx.ProblemDetails.Status ?? StatusCodes.Status500InternalServerError;
        ctx.ProblemDetails.Type     = ProblemDetailsHelper.GetTypeUri(status);
        ctx.ProblemDetails.Title    ??= ProblemDetailsHelper.GetTitle(status);
        ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    };
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problemDetails = new ValidationProblemDetails(context.ModelState)
        {
            Type     = ProblemDetailsHelper.GetTypeUri(400),
            Title    = ProblemDetailsHelper.GetTitle(400),
            Status   = StatusCodes.Status400BadRequest,
            Detail   = "Um ou mais campos estão inválidos.",
            Instance = context.HttpContext.Request.Path
        };
        problemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problemDetails)
        {
            ContentTypes = { "application/problem+json" }
        };
    };
});

// Register DbContext
builder.Services.AddDbContext<DomoLibriDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Register TenantProvider (Implementation to be added)
builder.Services.AddScoped<ITenantProvider, HttpTenantProvider>();

// Register application services
builder.Services.AddScoped<IStorageService, BlobStorageService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Smtp"));
// SmtpEmailService is registered as itself so Hangfire can resolve it as a job type.
builder.Services.AddScoped<SmtpEmailService>();
// IEmailService resolves to BackgroundEmailService, which enqueues jobs instead of sending inline.
builder.Services.AddScoped<IEmailService, BackgroundEmailService>();

// Configure Hangfire with PostgreSQL storage (reuses the application database).
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c =>
        c.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"))));
builder.Services.AddHangfireServer();

// Configure JWT authentication
var jwtSection = builder.Configuration.GetSection("Jwt");
var secret = jwtSection["Secret"] ?? string.Empty;
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
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
        };

        // Custom logic to read token from Cookie if Authorization header is missing
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("access_token", out var token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });

var app = builder.Build();

// 3. Apply CORS Policy
app.UseCors("DefaultPolicy");

// 4. Add Security Headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'; script-src 'self'; object-src 'none';");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseExceptionHandler(exApp =>
{
    exApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Type     = ProblemDetailsHelper.GetTypeUri(500),
            Title    = ProblemDetailsHelper.GetTitle(500),
            Status   = StatusCodes.Status500InternalServerError,
            Detail   = "Ocorreu um erro inesperado. Tente novamente mais tarde.",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        await context.Response.WriteAsJsonAsync(problem);
    });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    // Hangfire Dashboard exposed only in development (no auth required locally).
    app.UseHangfireDashboard("/hangfire");
}
else
{
    app.UseHsts(); // 4. Secure HSTS for Production
}

app.UseHttpsRedirection();

// 5. Apply Rate Limiting
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Apply migrations automatically ONLY in development
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<DomoLibriDbContext>();
            if (context.Database.GetPendingMigrations().Any())
            {
                context.Database.Migrate();
            }
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Ocorreu um erro ao aplicar as migrações no banco de dados.");
        }
    }
}

app.Run();

// Implementation of ITenantProvider
public class HttpTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? GetTenantId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return null;

        // 1. Always prioritize the tenant_id claim from the JWT (most secure)
        var tenantIdClaim = context.User?.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return tenantId;
        }

        // 2. Fallback to X-Tenant-Id header ONLY for unauthenticated requests
        // (e.g., public pages of a specific tenant or onboarding steps before login).
        // If the user IS authenticated but the claim is missing, we do NOT trust the header.
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            return null;
        }

        var tenantHeader = context.Request.Headers["X-Tenant-Id"].ToString();
        if (!string.IsNullOrEmpty(tenantHeader) && Guid.TryParse(tenantHeader, out var headerTenantId))
        {
            return headerTenantId;
        }

        return null;
    }
}
