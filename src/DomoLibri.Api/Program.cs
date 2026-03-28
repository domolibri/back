using DomoLibri.Application.Services;
using DomoLibri.Infrastructure.Services;
using DomoLibri.Infrastructure.Data;
using DomoLibri.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
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
builder.Services.AddScoped<IAuthService, AuthService>();

// Configure JWT authentication
var jwtSection = builder.Configuration.GetSection("Jwt");
var secret = jwtSection["Secret"]!;
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
    });

var app = builder.Build();

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
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Apply migrations automatically on startup
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

app.Run();

// Mock implementation of ITenantProvider
public class HttpTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? GetTenantId()
    {
        // Try to get TenantId from a custom header or JWT claim
        var tenantIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return tenantId;
        }

        // Fallback to a header for testing if no JWT is present
        var tenantHeader = _httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].ToString();
        if (Guid.TryParse(tenantHeader, out var headerTenantId))
        {
            return headerTenantId;
        }

        return null;
    }
}
