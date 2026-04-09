using DomoLibri.Application.Interfaces;
using DomoLibri.Application.Services;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Domain.Settings;
using DomoLibri.Infrastructure.Services;
using DomoLibri.Infrastructure.Data;
using DomoLibri.Api;
using DomoLibri.Api.Authorization;using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// JWT Secret — must come from an environment variable in production.
//
// Production : set  Jwt__Secret=<secret>  (double-underscore = section separator)
//              The env var is read directly so the secret can never accidentally
//              be satisfied by a value committed to appsettings.json.
// Development: falls back to IConfiguration (user-secrets → appsettings.Development.json)
//              dotnet user-secrets set "Jwt:Secret" "<segredo>"
// ---------------------------------------------------------------------------
const int MinJwtSecretLength = 32;

var jwtSecret = builder.Environment.IsProduction()
    ? Environment.GetEnvironmentVariable("Jwt__Secret")   // explicit — bypasses appsettings
    : builder.Configuration["Jwt:Secret"];                // user-secrets / appsettings.Dev

if (string.IsNullOrWhiteSpace(jwtSecret))
{
    var missingMsg =
        "O segredo JWT (Jwt__Secret) não está configurado. " +
        "Em produção, defina a variável de ambiente Jwt__Secret " +
        $"com no mínimo {MinJwtSecretLength} caracteres. " +
        "Em desenvolvimento, use: dotnet user-secrets set \"Jwt:Secret\" \"<segredo>\"";

    if (builder.Environment.IsProduction())
        throw new InvalidOperationException(missingMsg);

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Error.WriteLine($"[AVISO DE SEGURANÇA] {missingMsg}");
    Console.ResetColor();

    jwtSecret = string.Empty;
}
else if (jwtSecret.Length < MinJwtSecretLength)
{
    var shortMsg =
        $"O segredo JWT tem apenas {jwtSecret.Length} caracteres; " +
        $"o mínimo exigido é {MinJwtSecretLength}. " +
        "Em produção, injete um segredo forte via variável de ambiente Jwt__Secret.";

    if (builder.Environment.IsProduction())
        throw new InvalidOperationException(shortMsg);

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Error.WriteLine($"[AVISO DE SEGURANÇA] {shortMsg}");
    Console.ResetColor();
}
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// CORS Origins — mandatory in every environment; no silent fallback.
//
// Production : AllowedOrigins=https://app.domolibri.com.br,https://www.domolibri.com.br
// Development: add to appsettings.Development.json or User Secrets:
//              "AllowedOrigins": "http://localhost:4200"
//
// A missing or empty value is always a misconfiguration — the app refuses to start
// so that an unsafe wildcard or empty policy can never slip through unnoticed.
// ---------------------------------------------------------------------------
var allowedOriginsRaw = builder.Configuration["AllowedOrigins"];
var allowedOrigins = allowedOriginsRaw?
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (allowedOrigins is null or { Length: 0 })
    throw new InvalidOperationException(
        "AllowedOrigins não está configurado. " +
        "Defina a variável de ambiente AllowedOrigins com as origens permitidas separadas por vírgula. " +
        "Em desenvolvimento, adicione ao appsettings.Development.json: " +
        "\"AllowedOrigins\": \"http://localhost:4200\"");
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
builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddDbContext<DomoLibriDbContext>((provider, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.AddInterceptors(provider.GetRequiredService<AuditInterceptor>());
});

// Register TenantProvider (Implementation to be added)
builder.Services.AddScoped<ITenantProvider, HttpTenantProvider>();
builder.Services.AddScoped<IUserContextProvider, HttpUserContextProvider>();

// Register application services
builder.Services.Configure<AwsSettings>(builder.Configuration.GetSection("AwsSettings"));
builder.Services.AddScoped<IStorageService, S3StorageService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IInvitationService, InvitationService>();
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Smtp"));
// SmtpEmailService is registered as itself so Hangfire can resolve it as a job type.
builder.Services.AddScoped<SmtpEmailService>();
// IEmailService resolves to BackgroundEmailService, which enqueues jobs instead of sending inline.
builder.Services.AddScoped<IEmailService, BackgroundEmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IEditoraRepository, DomoLibri.Infrastructure.Data.Repositories.EditoraRepository>();
builder.Services.AddScoped<IUsuarioRepository, DomoLibri.Infrastructure.Data.Repositories.UsuarioRepository>();
builder.Services.AddScoped<ITenantSetupService, TenantSetupService>();
builder.Services.AddScoped<IEditoraQueryService, EditoraQueryService>();

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
// jwtSecret was validated (and sourced from the env var in production) above.
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
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

// Permission-based authorization:
// PermissionPolicyProvider auto-generates a policy for any [Authorize(Policy = "code")]
// attribute, treating the policy name as a permission code checked against the JWT's
// "permission" claims. PermissionAuthorizationHandler performs the actual evaluation.
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

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

// Implementation of IUserContextProvider
public class HttpUserContextProvider : IUserContextProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpUserContextProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? GetUserId()
    {
        // The JWT uses JwtRegisteredClaimNames.Sub ("sub") for the user ID.
        // JwtBearerHandler maps "sub" -> ClaimTypes.NameIdentifier via InboundClaimTypeMap.
        var claim = _httpContextAccessor.HttpContext?.User?
            .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;

        return Guid.TryParse(claim, out var id) ? id : null;
    }

    public string? GetIp()
        => _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();

    public string? GetUserAgent()
        => _httpContextAccessor.HttpContext?.Request?.Headers["User-Agent"].ToString();
}
