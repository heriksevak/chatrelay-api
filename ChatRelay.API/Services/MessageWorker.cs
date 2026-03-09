using Microsoft.EntityFrameworkCore;
using ChatRelay.API.Data;

public class MessageWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MessageWorker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var pendingMessages = await db.Messages
                .Where(m => m.Status == "Pending")
                .OrderBy(m => m.Id)
                .Take(10)
                .ToListAsync();


            foreach (var msg in pendingMessages)
            {
                msg.Status = "Sent";
                msg.SentAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();

            await Task.Delay(5000);
        }
    }
}