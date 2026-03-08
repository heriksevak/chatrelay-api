//using ChatRelay.API.Data;
//using Microsoft.EntityFrameworkCore;

//var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddControllers();
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

//builder.Services.AddHostedService<MessageWorker>();
//builder.Services.AddHttpClient<WhatsAppService>();

////var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

////if (!string.IsNullOrEmpty(connectionString))
////{
////    builder.Services.AddDbContext<ApplicationDbContext>(options =>
////        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
////}
////else
////{
////    Console.WriteLine("DefaultConnection not found.");
////}

//var app = builder.Build();

//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}

//app.UseAuthorization();
//app.UseMiddleware<ApiKeyMiddleware>();

//app.MapControllers();

//var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
//app.Run($"http://0.0.0.0:{port}");
//var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddControllers();

//var app = builder.Build();

//app.MapGet("/", () => "ChatRelay API Running");

//var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
//app.Run($"http://0.0.0.0:{port}");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(int.Parse(port));
});

var app = builder.Build();

app.MapGet("/", () => "ChatRelay API Running");

app.Run();