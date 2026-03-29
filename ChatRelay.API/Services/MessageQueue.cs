// ============================================================
//  ChatRelay — InMemoryMessageQueue + MessageWorker
// ============================================================

using ChatRelay.API.Data;
using ChatRelay.Models;
using ChatRelay.API.Services;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace ChatRelay.API.Queue;

// ── Queue item ────────────────────────────────────────────────

public class QueuedMessage
{
    public Guid MessageId { get; set; }
    public Guid WabaId { get; set; }
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
}

// ── Queue interface ───────────────────────────────────────────

public interface IMessageQueue
{
    void Enqueue(QueuedMessage message);
    bool TryDequeue(out QueuedMessage? message);
    int Count { get; }
}

// ── In-memory implementation (swap for Redis/RabbitMQ later) ──

public class InMemoryMessageQueue : IMessageQueue
{
    private readonly ConcurrentQueue<QueuedMessage> _queue = new();

    public void Enqueue(QueuedMessage message) => _queue.Enqueue(message);

    public bool TryDequeue(out QueuedMessage? message) =>
        _queue.TryDequeue(out message);

    public int Count => _queue.Count;
}

// ── MessageWorker — background service ───────────────────────

public class MessageWorker : BackgroundService
{
    private readonly IMessageQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageWorker> _logger;

    // How often worker polls the queue (ms)
    private const int PollIntervalMs = 500;

    // How often worker checks for scheduled messages (seconds)
    private const int ScheduledCheckIntervalSeconds = 30;

    private DateTime _lastScheduledCheck = DateTime.MinValue;

    public MessageWorker(
        IMessageQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<MessageWorker> logger)
    {
        _queue        = queue;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MessageWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Process queued messages
                while (_queue.TryDequeue(out var queued))
                {
                    if (queued != null)
                        await ProcessMessageAsync(queued.MessageId, queued.WabaId);
                }

                // Check for scheduled messages every 30 seconds
                if ((DateTime.UtcNow - _lastScheduledCheck).TotalSeconds
                    >= ScheduledCheckIntervalSeconds)
                {
                    await EnqueueScheduledMessagesAsync();
                    _lastScheduledCheck = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MessageWorker unhandled error");
            }

            await Task.Delay(PollIntervalMs, stoppingToken);
        }

        _logger.LogInformation("MessageWorker stopped");
    }

    // ── Process a single message ──────────────────────────────

    private async Task ProcessMessageAsync(Guid messageId, Guid wabaId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var provider    = scope.ServiceProvider.GetRequiredService<IMessageProvider>();
        var encryption  = scope.ServiceProvider.GetRequiredService<IEncryptionService>();

        // Load message
        var message = await db.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message == null)
        {
            _logger.LogWarning("MessageWorker: message {Id} not found", messageId);
            return;
        }

        // Skip if already processed (prevent double-processing)
        if (message.Status != MessageStatus.Queued)
        {
            _logger.LogDebug("MessageWorker: skipping message {Id} status={Status}",
                messageId, message.Status);
            return;
        }

        // Load WABA credentials
        var waba = await db.WabaAccounts
            .FirstOrDefaultAsync(w => w.Id == wabaId);

        if (waba == null || !waba.IsActive)
        {
            await MarkFailedAsync(db, message,
                "WABA not found or inactive", "WABA_INACTIVE");
            return;
        }

        if (string.IsNullOrEmpty(waba.AiSensyApiKey))
        {
            await MarkFailedAsync(db, message,
                "AiSensy API key not configured", "NO_API_KEY");
            return;
        }

        // Decrypt credentials
        string decryptedKey;
        try
        {
            decryptedKey = encryption.Decrypt(waba.AiSensyApiKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt credentials for WABA {WabaId}", wabaId);
            await MarkFailedAsync(db, message,
                "Credential decryption failed", "DECRYPT_ERROR");
            return;
        }

        // Load contact for phone number
        var contact = await db.Contacts
            .FirstOrDefaultAsync(c => c.Id == message.ContactId);

        if (contact == null)
        {
            await MarkFailedAsync(db, message,
                "Contact not found", "CONTACT_NOT_FOUND");
            return;
        }

        // Deserialize payload
        ChatRelay.API.DTOs.MessagePayload? payload;
        try
        {
            payload = System.Text.Json.JsonSerializer
                .Deserialize<ChatRelay.API.DTOs.MessagePayload>(
                    message.RawPayload ?? "{}");
        }
        catch
        {
            await MarkFailedAsync(db, message,
                "Invalid message payload", "INVALID_PAYLOAD");
            return;
        }

        if (payload == null)
        {
            await MarkFailedAsync(db, message,
                "Null message payload", "NULL_PAYLOAD");
            return;
        }

        // Send via AiSensy
        _logger.LogInformation(
            "Sending message {Id} to {Phone} via AiSensy", messageId, contact.PhoneNumber);

        var result = await provider.SendAsync(new ProviderSendRequest
        {
            PhoneNumber      = contact.PhoneNumber,
            AiSensyApiKey    = decryptedKey,
            Payload          = payload,
            WabaDisplayName  = waba.DisplayName
        });

        // Update message status
        if (result.Success)
        {
            message.Status            = MessageStatus.Sent;
            message.ProviderMessageId = result.ProviderMessageId;
            message.Provider          = "aisensy";
            message.SentAt            = DateTime.UtcNow;

            _logger.LogInformation(
                "Message {Id} sent successfully. ProviderMsgId={ProviderId}",
                messageId, result.ProviderMessageId);
        }
        else
        {
            message.Status        = MessageStatus.Failed;
            message.FailureReason = result.Error;
            message.FailureCode   = result.ErrorCode;
            message.FailedAt      = DateTime.UtcNow;

            _logger.LogWarning(
                "Message {Id} failed: {Error}", messageId, result.Error);
        }

        message.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    // ── Pick up scheduled messages due for sending ────────────

    private async Task EnqueueScheduledMessagesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var due = await db.Messages
            .Where(m =>
                m.Status == MessageStatus.Pending &&
                m.ScheduledAt.HasValue &&
                m.ScheduledAt <= DateTime.UtcNow)
            .Select(m => new { m.Id, m.WabaId })
            .Take(100) // process in batches
            .ToListAsync();

        if (!due.Any()) return;

        _logger.LogInformation(
            "Enqueuing {Count} scheduled messages", due.Count);

        foreach (var m in due)
        {
            // Mark as Queued so worker picks it up
            var message = await db.Messages.FindAsync(m.Id);
            if (message == null) continue;

            message.Status   = MessageStatus.Queued;
            message.QueuedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            _queue.Enqueue(new QueuedMessage
            {
                MessageId = m.Id,
                WabaId    = m.WabaId
            });
        }
    }

    // ── Mark failed ───────────────────────────────────────────

    private static async Task MarkFailedAsync(
        ApplicationDbContext db,
        Message message,
        string reason,
        string code)
    {
        message.Status        = MessageStatus.Failed;
        message.FailureReason = reason;
        message.FailureCode   = code;
        message.FailedAt      = DateTime.UtcNow;
        message.UpdatedAt     = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
