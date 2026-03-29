using ChatRelay.API.Context;
using ChatRelay.API.Data;
using ChatRelay.API.Middleware;
using ChatRelay.API.Queue;
using ChatRelay.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

//  JWT key — fail fast if missing 
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key is missing from configuration. Add it to appsettings.json.");

//  Authentication 
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero  // token expires exactly on time
    };
});
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();
builder.Services.AddHttpClient<IMetaTemplateService, MetaTemplateService>();
// Services
builder.Services.AddScoped<IWebhookProcessor, WebhookProcessor>();
builder.Services.AddScoped<IAutoReplyEngine, AutoReplyEngine>();
builder.Services.AddScoped<IAutoReplyService, AutoReplyService>();
// Services
builder.Services.AddSingleton<IMessageQueue, InMemoryMessageQueue>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IMessageProvider, AiSensyProvider>();
builder.Services.AddHttpClient<IMessageProvider, AiSensyProvider>();

// Background worker — uncomment this now
//builder.Services.AddHostedService<MessageWorker>();

builder.Services.AddScoped<IWabaService, WabaService>();
builder.Services.AddScoped<IEncryptionService, AesEncryptionService>();
builder.Services.AddScoped<IMetaGraphService, MetaGraphService>();
builder.Services.AddHttpClient<IAiSensyValidationService, AiSensyValidationService>();
builder.Services.AddHttpClient<IMetaGraphService, MetaGraphService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddAuthorization();

//  Database 
// Your existing connection string logic — preserved exactly
var connectionString =
    Environment.GetEnvironmentVariable("MYSQL_CONNECTION") ??
    builder.Configuration.GetConnectionString("DefaultConnection");

Console.WriteLine("CONNECTION STRING: " + connectionString);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    //options.UseMySql(connectionString, ServerVersion.Parse("8.0.36-mysql")));
    options.UseMySql(connectionString, ServerVersion.Parse("8.0.36-mysql"),
    mysqlOptions =>
    {
        mysqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null
        );
    }));

//  HttpContextAccessor (required for ITenantContext) 
builder.Services.AddHttpContextAccessor();

//  Tenant context (scoped — fresh per request) 
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("ChatRelayPolicy", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",       // tenant app dev
                "http://localhost:3001",       // admin app dev
                "https://app.chatrelay.in",   // tenant app prod
                "https://chatrelay.in",   // website app prod
                "https://admin.chatrelay.in" // website app prod
                // custom tenant domains handled dynamically:
                // add your tenants' domains here or use AllowAnyOrigin for now
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

//  Your existing services 
builder.Services.AddHttpClient<WhatsAppService>();
builder.Services.AddScoped<IAuthService, AuthService>();
// builder.Services.AddHostedService<MessageWorker>(); // uncomment when ready

//  Controllers + Swagger 
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Adds JWT Bearer input box in Swagger UI
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste your JWT token here"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// 
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.UseRouting();

app.UseCors("ChatRelayPolicy");
//  Middleware pipeline — ORDER IS CRITICAL 
app.UseAuthentication();    // 1. Parse JWT  populate User.Claims
app.UseAuthorization();     // 2. Enforce [Authorize] attributes
app.UseMiddleware<ApiKeyMiddlewareOld>();  // 3. Your existing API key check
app.UseTenantMiddleware();  // 4. Validate tenant is active

//  Routes 
app.MapControllers();
app.MapGet("/", () => "ChatRelay API Running");  // your health check

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    try
    {
        db.Database.Migrate();
        Console.WriteLine("Database migrated successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine(" Migration failed: " + ex.Message);
    }
}
//  Port binding  your existing logic preserved 
if (app.Environment.IsDevelopment())
{
    app.Run();
}
else
{
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    app.Run($"http://0.0.0.0:{port}");
}

//using ChatRelay.API.Data;
//using Microsoft.AspNetCore.Authentication.JwtBearer;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.IdentityModel.Tokens;
//using System.Text;

//var builder = WebApplication.CreateBuilder(args);


//var jwtKey = builder.Configuration["Jwt:Key"];

//builder.Services.AddAuthentication(options =>
//{
//    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
//    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
//})
//.AddJwtBearer(options =>
//{
//    options.TokenValidationParameters = new TokenValidationParameters
//    {
//        ValidateIssuer = true,
//        ValidateAudience = true,
//        ValidateLifetime = true,
//        ValidateIssuerSigningKey = true,

//        ValidIssuer = builder.Configuration["Jwt:Issuer"],
//        ValidAudience = builder.Configuration["Jwt:Audience"],
//        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
//    };
//});

//builder.Services.AddAuthorization();
//// Controllers
//builder.Services.AddControllers();
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

//// HTTP client for WhatsApp
//builder.Services.AddHttpClient<WhatsAppService>();

//// MySQL connection
////var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
//var connectionString =
//    Environment.GetEnvironmentVariable("MYSQL_CONNECTION") ??
//    builder.Configuration.GetConnectionString("DefaultConnection");

//builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseMySql(connectionString, ServerVersion.Parse("8.0.36-mysql")));

//// Background worker
////builder.Services.AddHostedService<MessageWorker>();

//var app = builder.Build();

//// Swagger only in development
//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}

//// Middleware
//app.UseAuthorization();
//app.UseMiddleware<ApiKeyMiddleware>();
//app.UseAuthentication();
//app.UseAuthorization();
//// Routes
//app.MapControllers();

//// Health check
//app.MapGet("/", () => "ChatRelay API Running");

//if (app.Environment.IsDevelopment())
//{
//    app.Run();
//}
//else
//{
//    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
//    app.Run($"http://0.0.0.0:{port}");
//}