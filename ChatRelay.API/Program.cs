using ChatRelay.API.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// HTTP client for WhatsApp
builder.Services.AddHttpClient<WhatsAppService>();

// MySQL connection
//var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var connectionString =
    Environment.GetEnvironmentVariable("MYSQL_CONNECTION") ??
    builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseMySql(connectionString, ServerVersion.Parse("8.0.36-mysql")));

// Background worker
builder.Services.AddHostedService<MessageWorker>();

var app = builder.Build();

// Swagger only in development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Middleware
app.UseAuthorization();
app.UseMiddleware<ApiKeyMiddleware>();

// Routes
app.MapControllers();

// Health check
app.MapGet("/", () => "ChatRelay API Running");

if (app.Environment.IsDevelopment())
{
    app.Run();
}
else
{
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    app.Run($"http://0.0.0.0:{port}");
}