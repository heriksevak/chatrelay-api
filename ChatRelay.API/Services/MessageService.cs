// ============================================================
//  ChatRelay — IMessageService + MessageService
//  Orchestrates: contact upsert → conversation → message → queue
// ============================================================

using ChatRelay.API.Data;
using ChatRelay.API.DTOs;
using ChatRelay.Models;
using ChatRelay.API.Queue;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ChatRelay.API.Services;

public interface IMessageService
{
    Task<ServiceResult<MessageResponse>> SendAsync(
        Guid wabaId, SendMessageRequest request, Guid sentByUserId);

    Task<ServiceResult<BulkSendResponse>> SendBulkAsync(
        Guid wabaId, BulkSendRequest request, Guid sentByUserId);

    Task<ServiceResult<List<MessageResponse>>> GetMessagesAsync(
        Guid wabaId, MessageFilterRequest filter);

    Task<ServiceResult<MessageResponse>> GetByIdAsync(
        Guid messageId, Guid wabaId);

    Task<ServiceResult<bool>> CancelScheduledAsync(
        Guid messageId, Guid wabaId);
}

public class MessageService : IMessageService
{
    private readonly ApplicationDbContext _db;
    private readonly IMessageQueue _queue;
    private readonly ILogger<MessageService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public MessageService(
        ApplicationDbContext db,
        IMessageQueue queue,
        ILogger<MessageService> logger)
    {
        _db     = db;
        _queue  = queue;
        _logger = logger;
    }

    // ── Send single message ───────────────────────────────────

    public async Task<ServiceResult<MessageResponse>> SendAsync(
        Guid wabaId, SendMessageRequest request, Guid sentByUserId)
    {
        // 1. Validate WABA
        var waba = await _db.WabaAccounts
            .FirstOrDefaultAsync(w => w.Id == wabaId && w.IsActive);

        if (waba == null)
            return ServiceResult<MessageResponse>.Fail("WABA not found or inactive");

        if (string.IsNullOrEmpty(waba.AiSensyApiKey))
            return ServiceResult<MessageResponse>.Fail(
                "AiSensy API key not configured for this WABA");

        // 2. Upsert contact
        var contact = await UpsertContactAsync(wabaId, request.PhoneNumber);

        // 3. Upsert conversation
        var conversation = request.ConversationId.HasValue
            ? await _db.Conversations.FindAsync(request.ConversationId.Value)
              ?? await CreateConversationAsync(wabaId, contact.Id)
            : await GetOrCreateConversationAsync(wabaId, contact.Id);

        // 4. Determine initial status
        var isScheduled = request.ScheduledAt.HasValue
                          && request.ScheduledAt > DateTime.UtcNow;

        var initialStatus = isScheduled
            ? MessageStatus.Pending  // worker picks up later
            : MessageStatus.Queued;  // worker picks up immediately

        // 5. Create message record
        var message = new Message
        {
            Id             = Guid.NewGuid(),
            WabaId         = wabaId,
            ConversationId = conversation.Id,
            ContactId      = contact.Id,
            Direction      = MessageDirection.Outbound,
            MessageType    = MapPayloadTypeToMessageType(request.Payload.Type),
            Status         = initialStatus,
            Content        = ExtractTextContent(request.Payload),
            ScheduledAt    = request.ScheduledAt,
            SentByUserId   = sentByUserId,
            IsAutomated    = false,
            QueuedAt       = isScheduled ? null : DateTime.UtcNow,
            RawPayload     = JsonSerializer.Serialize(request.Payload, JsonOpts),
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow,
        };

        _db.Messages.Add(message);

        // 6. Update conversation stats
        conversation.MessageCount++;
        conversation.LastMessageAt = DateTime.UtcNow;
        conversation.UpdatedAt     = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // 7. Enqueue immediately if not scheduled
        if (!isScheduled)
        {
            _queue.Enqueue(new QueuedMessage
            {
                MessageId = message.Id,
                WabaId    = wabaId
            });

            _logger.LogInformation(
                "Message {Id} enqueued for immediate send to {Phone}",
                message.Id, request.PhoneNumber);
        }
        else
        {
            _logger.LogInformation(
                "Message {Id} scheduled for {ScheduledAt}",
                message.Id, request.ScheduledAt);
        }

        return ServiceResult<MessageResponse>.Ok(ToResponse(message, contact.PhoneNumber));
    }

    // ── Send bulk messages ────────────────────────────────────

    public async Task<ServiceResult<BulkSendResponse>> SendBulkAsync(
        Guid wabaId, BulkSendRequest request, Guid sentByUserId)
    {
        if (request.PhoneNumbers.Count > 1000)
            return ServiceResult<BulkSendResponse>.Fail(
                "Maximum 1000 recipients per bulk request");

        if (request.PhoneNumbers.Count == 0)
            return ServiceResult<BulkSendResponse>.Fail("No recipients provided");

        var response = new BulkSendResponse
        {
            Total = request.PhoneNumbers.Count
        };

        // Deduplicate
        var uniqueNumbers = request.PhoneNumbers
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();

        foreach (var phone in uniqueNumbers)
        {
            var singleRequest = new SendMessageRequest
            {
                PhoneNumber    = phone,
                Payload        = request.Payload,
                ScheduledAt    = request.ScheduledAt
            };

            var result = await SendAsync(wabaId, singleRequest, sentByUserId);

            if (result.Success)
            {
                response.Queued++;
                response.Messages.Add(result.Data!);
            }
            else
            {
                response.Failed++;
                _logger.LogWarning(
                    "Bulk send failed for {Phone}: {Error}", phone, result.Error);
            }
        }

        return ServiceResult<BulkSendResponse>.Ok(response);
    }

    // ── Get messages ──────────────────────────────────────────

    public async Task<ServiceResult<List<MessageResponse>>> GetMessagesAsync(
        Guid wabaId, MessageFilterRequest filter)
    {
        var query = _db.Messages
            .Include(m => m.Contact)
            .Where(m => m.WabaId == wabaId)
            .AsQueryable();

        if (filter.ConversationId.HasValue)
            query = query.Where(m => m.ConversationId == filter.ConversationId.Value);

        if (filter.ContactId.HasValue)
            query = query.Where(m => m.ContactId == filter.ContactId.Value);

        if (filter.Status.HasValue)
            query = query.Where(m => m.Status == filter.Status.Value);

        if (filter.Direction.HasValue)
            query = query.Where(m => m.Direction == filter.Direction.Value);

        if (filter.From.HasValue)
            query = query.Where(m => m.CreatedAt >= filter.From.Value);

        if (filter.To.HasValue)
            query = query.Where(m => m.CreatedAt <= filter.To.Value);

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        var result = messages
            .Select(m => ToResponse(m, m.Contact?.PhoneNumber ?? string.Empty))
            .ToList();

        return ServiceResult<List<MessageResponse>>.Ok(result);
    }

    // ── Get single message ────────────────────────────────────

    public async Task<ServiceResult<MessageResponse>> GetByIdAsync(
        Guid messageId, Guid wabaId)
    {
        var message = await _db.Messages
            .Include(m => m.Contact)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.WabaId == wabaId);

        if (message == null)
            return ServiceResult<MessageResponse>.Fail("Message not found");

        return ServiceResult<MessageResponse>.Ok(
            ToResponse(message, message.Contact?.PhoneNumber ?? string.Empty));
    }

    // ── Cancel scheduled message ──────────────────────────────

    public async Task<ServiceResult<bool>> CancelScheduledAsync(
        Guid messageId, Guid wabaId)
    {
        var message = await _db.Messages
            .FirstOrDefaultAsync(m =>
                m.Id == messageId &&
                m.WabaId == wabaId &&
                m.Status == MessageStatus.Pending);

        if (message == null)
            return ServiceResult<bool>.Fail(
                "Scheduled message not found or already processed");

        message.Status      = MessageStatus.Cancelled;
        message.CancelledAt = DateTime.UtcNow;
        message.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult<bool>.Ok(true);
    }

    // ── Private helpers ───────────────────────────────────────

    private async Task<Contact> UpsertContactAsync(Guid wabaId, string phoneNumber)
    {
        var normalized = phoneNumber.Trim();

        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c =>
                c.WabaId == wabaId && c.PhoneNumber == normalized);

        if (contact != null) return contact;

        // Auto-create contact
        contact = new Contact
        {
            Id          = Guid.NewGuid(),
            WabaId      = wabaId,
            PhoneNumber = normalized,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        _db.Contacts.Add(contact);
        return contact;
    }

    private async Task<Conversation> GetOrCreateConversationAsync(
        Guid wabaId, Guid contactId)
    {
        // Find open conversation for this contact
        var existing = await _db.Conversations
            .FirstOrDefaultAsync(c =>
                c.WabaId    == wabaId &&
                c.ContactId == contactId &&
                c.Status    == ConversationStatus.Open);

        if (existing != null) return existing;

        return await CreateConversationAsync(wabaId, contactId);
    }

    private async Task<Conversation> CreateConversationAsync(
        Guid wabaId, Guid contactId)
    {
        var conversation = new Conversation
        {
            Id            = Guid.NewGuid(),
            WabaId        = wabaId,
            ContactId     = contactId,
            Status        = ConversationStatus.Open,
            Channel       = "whatsapp",
            LastMessageAt = DateTime.UtcNow,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        };

        _db.Conversations.Add(conversation);
        return conversation;
    }

    private static MessageType MapPayloadTypeToMessageType(MessagePayloadType type) =>
        type switch
        {
            MessagePayloadType.Text        => MessageType.Text,
            MessagePayloadType.Image       => MessageType.Image,
            MessagePayloadType.Video       => MessageType.Video,
            MessagePayloadType.Audio       => MessageType.Audio,
            MessagePayloadType.Document    => MessageType.Document,
            MessagePayloadType.Sticker     => MessageType.Sticker,
            MessagePayloadType.Location    => MessageType.Location,
            MessagePayloadType.Contact     => MessageType.Contact,
            MessagePayloadType.Template    => MessageType.Template,
            MessagePayloadType.Interactive => MessageType.Text,
            _ => MessageType.Text
        };

    // Extract preview text for display
    private static string? ExtractTextContent(MessagePayload payload) =>
        payload.Type switch
        {
            MessagePayloadType.Text        => payload.Text?.Body,
            MessagePayloadType.Template    => $"[Template] {payload.Template?.Name}",
            MessagePayloadType.Image       => $"[Image] {payload.Media?.Caption}",
            MessagePayloadType.Video       => $"[Video] {payload.Media?.Caption}",
            MessagePayloadType.Audio       => "[Audio]",
            MessagePayloadType.Document    => $"[Document] {payload.Media?.FileName}",
            MessagePayloadType.Location    => "[Location]",
            MessagePayloadType.Contact     => "[Contact]",
            MessagePayloadType.Interactive => $"[Interactive] {payload.Interactive?.Body}",
            _ => null
        };

    private static MessageResponse ToResponse(Message m, string phone) => new()
    {
        Id                = m.Id,
        WabaId            = m.WabaId,
        ConversationId    = m.ConversationId,
        ContactId         = m.ContactId,
        PhoneNumber       = phone,
        Direction         = m.Direction.ToString(),
        MessageType       = m.MessageType.ToString(),
        Status            = m.Status.ToString(),
        Content           = m.Content,
        ProviderMessageId = m.ProviderMessageId,
        FailureReason     = m.FailureReason,
        ScheduledAt       = m.ScheduledAt,
        SentAt            = m.SentAt,
        DeliveredAt       = m.DeliveredAt,
        ReadAt            = m.ReadAt,
        CreatedAt         = m.CreatedAt,
    };
}

// ── Filter ────────────────────────────────────────────────────

public class MessageFilterRequest
{
    public Guid? ConversationId { get; set; }
    public Guid? ContactId { get; set; }
    public MessageStatus? Status { get; set; }
    public MessageDirection? Direction { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
