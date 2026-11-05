using EntraApi.Services;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;
using PricingApi.Data;
using PricingApi.Services;
using PricingApi.Storage;

var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddApplicationInsightsTelemetry();

builder.Services.AddDbContext<PricingDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.")));

// Validate SharePoint settings at startup (fail fast)
var sharePointSection = builder.Configuration.GetRequiredSection("SharePoint");
var sharePointOptions = sharePointSection.Get<SharePointOptions>()
    ?? throw new InvalidOperationException("SharePoint configuration section is missing.");

if (string.IsNullOrWhiteSpace(sharePointOptions.TenantId) ||
    string.IsNullOrWhiteSpace(sharePointOptions.ClientId) ||
    string.IsNullOrWhiteSpace(sharePointOptions.ClientSecret) ||
    string.IsNullOrWhiteSpace(sharePointOptions.DriveId))
{
    throw new InvalidOperationException(
        "SharePoint configuration is invalid. Required: SharePoint:TenantId, ClientId, ClientSecret, DriveId.");
}

builder.Services.Configure<SharePointOptions>(sharePointSection);

builder.Services.AddScoped<ICaseService, CaseService>();
builder.Services.AddScoped<IFileStore, LocalFileStore>();

builder.Services.AddHttpClient<SharePointGraphService>();
builder.Services.AddScoped<ISharePointLibraryService, SharePointLibraryService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:5173"];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddControllers();
// ---- Swagger/OpenAPI -----------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            []
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger")).AllowAnonymous().ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapControllers();

app.Run();
