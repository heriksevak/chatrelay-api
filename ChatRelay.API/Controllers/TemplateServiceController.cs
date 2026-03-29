// ============================================================
//  ChatRelay — ITemplateService + TemplateService
// ============================================================

using ChatRelay.API.Data;
using ChatRelay.API.DTOs;
using ChatRelay.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChatRelay.API.Services;

public interface ITemplateService
{
    Task<ServiceResult<TemplateResponse>> CreateLocalAsync(
        Guid wabaId, CreateTemplateRequest request);

    Task<ServiceResult<TemplateResponse>> SubmitToMetaAsync(
        Guid wabaId, Guid templateId);

    Task<ServiceResult<TemplateResponse>> CreateAndSubmitAsync(
        Guid wabaId, CreateTemplateRequest request);

    Task<ServiceResult<List<TemplateResponse>>> GetTemplatesAsync(
        Guid wabaId, TemplateFilterRequest filter);

    Task<ServiceResult<TemplateResponse>> GetByIdAsync(
        Guid templateId, Guid wabaId);

    Task<ServiceResult<TemplateSyncResponse>> SyncFromMetaAsync(Guid wabaId);

    Task<ServiceResult<bool>> DeleteAsync(Guid templateId, Guid wabaId);

    Task<ServiceResult<TemplatePreviewResponse>> PreviewAsync(
        CreateTemplateRequest request);
}

public class TemplateService : ITemplateService
{
    private readonly ApplicationDbContext _db;
    private readonly IMetaTemplateService _metaTemplate;
    private readonly IEncryptionService _encryption;
    private readonly ILogger<TemplateService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization
            .JsonIgnoreCondition.WhenWritingNull
    };

    public TemplateService(
        ApplicationDbContext db,
        IMetaTemplateService metaTemplate,
        IEncryptionService encryption,
        ILogger<TemplateService> logger)
    {
        _db          = db;
        _metaTemplate = metaTemplate;
        _encryption  = encryption;
        _logger      = logger;
    }

    // ── Create locally (draft) ────────────────────────────────

    public async Task<ServiceResult<TemplateResponse>> CreateLocalAsync(
        Guid wabaId, CreateTemplateRequest request)
    {
        var validation = ValidateRequest(request);
        if (validation != null) return ServiceResult<TemplateResponse>.Fail(validation);

        // Check duplicate name for this WABA + language
        var exists = await _db.Templates.AnyAsync(t =>
            t.WabaId == wabaId &&
            t.Name   == request.Name.ToLower().Replace(" ", "_") &&
            t.Language == request.Language);

        if (exists)
            return ServiceResult<TemplateResponse>.Fail(
                "A template with this name and language already exists for this WABA");

        var template = BuildTemplateEntity(wabaId, request);
        _db.Templates.Add(template);
        await _db.SaveChangesAsync();

        return ServiceResult<TemplateResponse>.Ok(ToResponse(template));
    }

    // ── Submit existing draft to Meta ─────────────────────────

    public async Task<ServiceResult<TemplateResponse>> SubmitToMetaAsync(
        Guid wabaId, Guid templateId)
    {
        var template = await _db.Templates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.WabaId == wabaId);

        if (template == null)
            return ServiceResult<TemplateResponse>.Fail("Template not found");

        if (template.Status == TemplateStatus.Approved)
            return ServiceResult<TemplateResponse>.Fail(
                "Template is already approved");

        // Load WABA credentials
        var waba = await _db.WabaAccounts.FindAsync(wabaId);
        if (waba == null || string.IsNullOrEmpty(waba.MetaAccessToken))
            return ServiceResult<TemplateResponse>.Fail(
                "WABA Meta credentials not configured");

        var accessToken = _encryption.Decrypt(waba.MetaAccessToken);
        var metaWabaId  = waba.WabaId ?? string.Empty;

        // Rebuild request from stored template
        var request = RebuildRequest(template);

        // Submit to Meta
        var result = await _metaTemplate.SubmitTemplateAsync(
            metaWabaId, accessToken, request);

        if (!result.Success)
            return ServiceResult<TemplateResponse>.Fail(
                $"Meta submission failed: {result.Error}");

        // Update template record
        template.MetaTemplateId = result.MetaTemplateId;
        template.Status         = TemplateStatus.Pending;
        template.SubmittedAt    = DateTime.UtcNow;
        template.UpdatedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Template '{Name}' submitted to Meta. MetaId={MetaId}",
            template.Name, result.MetaTemplateId);

        return ServiceResult<TemplateResponse>.Ok(ToResponse(template));
    }

    // ── Create and submit in one step ─────────────────────────

    public async Task<ServiceResult<TemplateResponse>> CreateAndSubmitAsync(
        Guid wabaId, CreateTemplateRequest request)
    {
        // Create locally first
        var createResult = await CreateLocalAsync(wabaId, request);
        if (!createResult.Success) return createResult;

        // Then submit to Meta
        return await SubmitToMetaAsync(wabaId, createResult.Data!.Id);
    }

    // ── Get templates with filters ────────────────────────────

    public async Task<ServiceResult<List<TemplateResponse>>> GetTemplatesAsync(
        Guid wabaId, TemplateFilterRequest filter)
    {
        var query = _db.Templates
            .Where(t => t.WabaId == wabaId)
            .AsQueryable();

        if (!string.IsNullOrEmpty(filter.Status) &&
            Enum.TryParse<TemplateStatus>(filter.Status, true, out var status))
            query = query.Where(t => t.Status == status);

        if (!string.IsNullOrEmpty(filter.Category) &&
            Enum.TryParse<TemplateCategory>(filter.Category, true, out var category))
            query = query.Where(t => t.Category == category);

        if (!string.IsNullOrEmpty(filter.Language))
            query = query.Where(t => t.Language == filter.Language);

        if (!string.IsNullOrEmpty(filter.Search))
            query = query.Where(t => t.Name.Contains(filter.Search));

        var templates = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return ServiceResult<List<TemplateResponse>>.Ok(
            templates.Select(ToResponse).ToList());
    }

    // ── Get single template ───────────────────────────────────

    public async Task<ServiceResult<TemplateResponse>> GetByIdAsync(
        Guid templateId, Guid wabaId)
    {
        var template = await _db.Templates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.WabaId == wabaId);

        if (template == null)
            return ServiceResult<TemplateResponse>.Fail("Template not found");

        return ServiceResult<TemplateResponse>.Ok(ToResponse(template));
    }

    // ── Sync templates from Meta ──────────────────────────────

    public async Task<ServiceResult<TemplateSyncResponse>> SyncFromMetaAsync(Guid wabaId)
    {
        var waba = await _db.WabaAccounts.FindAsync(wabaId);
        if (waba == null || string.IsNullOrEmpty(waba.MetaAccessToken))
            return ServiceResult<TemplateSyncResponse>.Fail(
                "WABA Meta credentials not configured");

        var accessToken = _encryption.Decrypt(waba.MetaAccessToken);
        var metaWabaId  = waba.WabaId ?? string.Empty;

        var syncResult = new TemplateSyncResponse();

        // Fetch all templates from Meta
        List<MetaTemplateRecord> metaTemplates;
        try
        {
            metaTemplates = await _metaTemplate
                .FetchAllTemplatesAsync(metaWabaId, accessToken);
        }
        catch (Exception ex)
        {
            return ServiceResult<TemplateSyncResponse>.Fail(
                $"Failed to fetch templates from Meta: {ex.Message}");
        }

        syncResult.TotalFromMeta = metaTemplates.Count;

        foreach (var metaTemplate in metaTemplates)
        {
            try
            {
                // Find existing by Meta ID or name+language
                var existing = await _db.Templates.FirstOrDefaultAsync(t =>
                    t.WabaId == wabaId &&
                    (t.MetaTemplateId == metaTemplate.Id ||
                     (t.Name == metaTemplate.Name &&
                      t.Language == metaTemplate.Language)));

                var newStatus = MapMetaStatus(metaTemplate.Status);

                if (existing == null)
                {
                    // New template found on Meta — save it locally
                    var newTemplate = new Template
                    {
                        Id             = Guid.NewGuid(),
                        WabaId         = wabaId,
                        Name           = metaTemplate.Name,
                        Language       = metaTemplate.Language,
                        Category       = MapMetaCategory(metaTemplate.Category),
                        Status         = newStatus,
                        MetaTemplateId = metaTemplate.Id,
                        BodyText       = ExtractBodyFromComponents(metaTemplate),
                        RejectionReason = metaTemplate.RejectedReason,
                        CreatedAt      = DateTime.UtcNow,
                        UpdatedAt      = DateTime.UtcNow,
                    };

                    _db.Templates.Add(newTemplate);
                    syncResult.NewTemplates++;
                }
                else if (existing.Status != newStatus ||
                         existing.MetaTemplateId != metaTemplate.Id)
                {
                    // Update status
                    existing.Status         = newStatus;
                    existing.MetaTemplateId = metaTemplate.Id;
                    existing.RejectionReason = metaTemplate.RejectedReason;
                    existing.UpdatedAt      = DateTime.UtcNow;

                    if (newStatus == TemplateStatus.Approved && !existing.ApprovedAt.HasValue)
                        existing.ApprovedAt = DateTime.UtcNow;

                    if (newStatus == TemplateStatus.Rejected && !existing.RejectedAt.HasValue)
                        existing.RejectedAt = DateTime.UtcNow;

                    syncResult.UpdatedTemplates++;
                }
                else
                {
                    syncResult.NoChange++;
                }
            }
            catch (Exception ex)
            {
                syncResult.Errors.Add(
                    $"Error syncing template '{metaTemplate.Name}': {ex.Message}");
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Template sync complete for WABA {WabaId}: " +
            "total={Total} new={New} updated={Updated} nochange={NoChange}",
            wabaId, syncResult.TotalFromMeta,
            syncResult.NewTemplates, syncResult.UpdatedTemplates, syncResult.NoChange);

        return ServiceResult<TemplateSyncResponse>.Ok(syncResult);
    }

    // ── Delete template ───────────────────────────────────────

    public async Task<ServiceResult<bool>> DeleteAsync(Guid templateId, Guid wabaId)
    {
        var template = await _db.Templates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.WabaId == wabaId);

        if (template == null)
            return ServiceResult<bool>.Fail("Template not found");

        // Delete from Meta if submitted
        if (!string.IsNullOrEmpty(template.MetaTemplateId))
        {
            var waba = await _db.WabaAccounts.FindAsync(wabaId);
            if (waba != null && !string.IsNullOrEmpty(waba.MetaAccessToken))
            {
                var accessToken = _encryption.Decrypt(waba.MetaAccessToken);
                var deleted = await _metaTemplate.DeleteTemplateAsync(
                    waba.WabaId ?? string.Empty,
                    accessToken,
                    template.Name,
                    template.MetaTemplateId);

                if (!deleted)
                    _logger.LogWarning(
                        "Could not delete template '{Name}' from Meta — " +
                        "deleting locally only", template.Name);
            }
        }

        _db.Templates.Remove(template);
        await _db.SaveChangesAsync();

        return ServiceResult<bool>.Ok(true);
    }

    // ── Preview ───────────────────────────────────────────────

    public async Task<ServiceResult<TemplatePreviewResponse>> PreviewAsync(
        CreateTemplateRequest request)
    {
        var validation = ValidateRequest(request);
        if (validation != null)
            return ServiceResult<TemplatePreviewResponse>.Fail(validation);

        // Build a temporary entity just for preview
        var temp = BuildTemplateEntity(Guid.Empty, request);

        // Render body with example values filled in
        var rendered = RenderWithExamples(
            request.Body.Text,
            request.Body.VariableExamples);

        var renderedHeader = request.Header?.Format.ToUpper() == "TEXT"
            ? RenderWithExamples(
                request.Header.Text ?? string.Empty,
                request.Header.TextExamples)
            : null;

        // Build the actual Meta payload for preview
        var metaPayload = BuildMetaPayloadForPreview(request);

        var preview = new TemplatePreviewResponse
        {
            Name           = request.Name,
            Category       = request.Category.ToString(),
            Language       = request.Language,
            HeaderType     = request.Header?.Format,
            HeaderText     = request.Header?.Text,
            BodyText       = request.Body.Text,
            FooterText     = request.Footer?.Text,
            RenderedBody   = rendered,
            RenderedHeader = renderedHeader,
            MetaPayload    = metaPayload,
            Buttons = request.Buttons?.Select(b => new PreviewButton
            {
                Type        = b.Type,
                Text        = b.Text,
                Url         = b.Url,
                PhoneNumber = b.PhoneNumber
            }).ToList() ?? new(),
            CarouselCards = request.CarouselCards?.Select((c, i) =>
                new PreviewCarouselCard
                {
                    Index        = i,
                    HeaderFormat = c.Header.Format,
                    BodyText     = c.Body.Text,
                    Buttons      = c.Buttons?.Select(b => new PreviewButton
                    {
                        Type        = b.Type,
                        Text        = b.Text,
                        Url         = b.Url,
                        PhoneNumber = b.PhoneNumber
                    }).ToList() ?? new()
                }).ToList() ?? new()
        };

        return ServiceResult<TemplatePreviewResponse>.Ok(preview);
    }

    // ── Private helpers ───────────────────────────────────────

    private static string? ValidateRequest(CreateTemplateRequest request)
    {
        // Validate template name (Meta requirement: lowercase, underscores)
        var nameCleaned = request.Name.ToLower().Replace(" ", "_");
        if (!Regex.IsMatch(nameCleaned, @"^[a-z0-9_]+$"))
            return "Template name can only contain lowercase letters, numbers, and underscores";

        if (string.IsNullOrWhiteSpace(request.Body?.Text))
            return "Body text is required";

        if (request.Buttons?.Count > 10)
            return "Maximum 10 buttons allowed";

        if (request.CarouselCards?.Count > 10)
            return "Maximum 10 carousel cards allowed";

        if (request.CarouselCards?.Any(c => c.Buttons?.Count > 2) == true)
            return "Maximum 2 buttons per carousel card";

        return null;
    }

    private static Template BuildTemplateEntity(
        Guid wabaId, CreateTemplateRequest request)
    {
        var name = request.Name.ToLower().Replace(" ", "_");

        // Determine header type
        TemplateHeaderType headerType = TemplateHeaderType.None;
        string? headerContent = null;

        if (request.Header != null)
        {
            headerType = request.Header.Format.ToUpper() switch
            {
                "TEXT"     => TemplateHeaderType.Text,
                "IMAGE"    => TemplateHeaderType.Image,
                "VIDEO"    => TemplateHeaderType.Video,
                "DOCUMENT" => TemplateHeaderType.Document,
                "LOCATION" => TemplateHeaderType.None,
                _          => TemplateHeaderType.None
            };
            headerContent = request.Header.Text ?? request.Header.MediaExampleUrl;
        }

        return new Template
        {
            Id          = Guid.NewGuid(),
            WabaId      = wabaId,
            Name        = name,
            Category    = request.Category,
            Language    = request.Language,
            Status      = TemplateStatus.Draft,
            HeaderType  = headerType,
            HeaderContent = headerContent,
            BodyText    = request.Body.Text,
            FooterText  = request.Footer?.Text,
            ButtonsJson = request.Buttons != null
                ? JsonSerializer.Serialize(request.Buttons, JsonOpts)
                : null,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };
    }

    private static CreateTemplateRequest RebuildRequest(Template template)
    {
        var request = new CreateTemplateRequest
        {
            Name     = template.Name,
            Category = template.Category,
            Language = template.Language,
            Body     = new TemplateBodyComponent { Text = template.BodyText },
        };

        if (!string.IsNullOrEmpty(template.FooterText))
            request.Footer = new TemplateFooterComponent
                { Text = template.FooterText };

        if (template.HeaderType != TemplateHeaderType.None)
            request.Header = new TemplateHeaderComponent
            {
                Format = template.HeaderType.ToString().ToUpper(),
                Text   = template.HeaderType == TemplateHeaderType.Text
                    ? template.HeaderContent : null
            };

        if (!string.IsNullOrEmpty(template.ButtonsJson))
        {
            try
            {
                request.Buttons = JsonSerializer
                    .Deserialize<List<TemplateButtonComponent>>(
                        template.ButtonsJson, JsonOpts);
            }
            catch { /* ignore */ }
        }

        return request;
    }

    private static string RenderWithExamples(string text, List<string>? examples)
    {
        if (examples == null || !examples.Any()) return text;

        var rendered = text;
        for (int i = 0; i < examples.Count; i++)
            rendered = rendered.Replace($"{{{{{i + 1}}}}}", examples[i]);

        return rendered;
    }

    private static object BuildMetaPayloadForPreview(CreateTemplateRequest request)
    {
        // Reuse MetaTemplateService logic via static helper
        var components = new List<object>();

        if (request.Header != null)
            components.Add(new
            {
                type   = "HEADER",
                format = request.Header.Format.ToUpper(),
                text   = request.Header.Text
            });

        components.Add(new { type = "BODY", text = request.Body.Text });

        if (request.Footer != null)
            components.Add(new { type = "FOOTER", text = request.Footer.Text });

        if (request.Buttons?.Any() == true)
            components.Add(new
            {
                type    = "BUTTONS",
                buttons = request.Buttons.Select(b => new
                {
                    type         = b.Type.ToUpper(),
                    text         = b.Text,
                    url          = b.Url,
                    phone_number = b.PhoneNumber
                })
            });

        return new
        {
            name       = request.Name.ToLower().Replace(" ", "_"),
            category   = request.Category.ToString().ToUpper(),
            language   = request.Language,
            components
        };
    }

    private static TemplateStatus MapMetaStatus(string status) =>
        status.ToUpper() switch
        {
            "APPROVED"        => TemplateStatus.Approved,
            "REJECTED"        => TemplateStatus.Rejected,
            "PENDING"         => TemplateStatus.Pending,
            "PAUSED"          => TemplateStatus.Paused,
            "DISABLED"        => TemplateStatus.Paused,
            "FLAGGED"         => TemplateStatus.Paused,
            _                 => TemplateStatus.Pending
        };

    private static TemplateCategory MapMetaCategory(string category) =>
        category.ToUpper() switch
        {
            "MARKETING"      => TemplateCategory.Marketing,
            "UTILITY"        => TemplateCategory.Utility,
            "AUTHENTICATION" => TemplateCategory.Authentication,
            _                => TemplateCategory.Utility
        };

    private static string ExtractBodyFromComponents(MetaTemplateRecord template)
    {
        if (template.Components == null) return string.Empty;
        foreach (var comp in template.Components)
        {
            try
            {
                if (comp.TryGetProperty("type", out var type) &&
                    type.GetString()?.ToUpper() == "BODY" &&
                    comp.TryGetProperty("text", out var text))
                    return text.GetString() ?? string.Empty;
            }
            catch { /* skip */ }
        }
        return string.Empty;
    }

    private static TemplateResponse ToResponse(Template t) => new()
    {
        Id              = t.Id,
        WabaId          = t.WabaId,
        Name            = t.Name,
        Category        = t.Category.ToString(),
        Language        = t.Language,
        Status          = t.Status.ToString(),
        MetaTemplateId  = t.MetaTemplateId,
        RejectionReason = t.RejectionReason,
        HeaderType      = t.HeaderType.ToString(),
        HeaderContent   = t.HeaderContent,
        BodyText        = t.BodyText,
        FooterText      = t.FooterText,
        ButtonsJson     = t.ButtonsJson,
        TimesUsed       = t.TimesUsed,
        LastUsedAt      = t.LastUsedAt,
        SubmittedAt     = t.SubmittedAt,
        ApprovedAt      = t.ApprovedAt,
        RejectedAt      = t.RejectedAt,
        CreatedAt       = t.CreatedAt,
        UpdatedAt       = t.UpdatedAt,
    };
}


// ============================================================
//  ChatRelay — TemplatesController
// ============================================================


