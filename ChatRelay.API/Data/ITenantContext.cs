// ============================================================
//  ChatRelay — ITenantContext
//  Resolves current tenant/waba/user from JWT on every request
// ============================================================

namespace ChatRelay.API.Context;

public interface ITenantContext
{
    // Resolved from JWT claims
    Guid TenantId { get; }
    Guid UserId { get; }
    string UserRole { get; }
    string UserEmail { get; }
    string FullName { get; }

    // Optional — set when user is operating on a specific WABA
    // Comes from X-Waba-Id request header or route param
    Guid? WabaId { get; }

    // Helpers
    bool IsSuperAdmin { get; }
    bool IsTenantAdmin { get; }
    bool IsAuthenticated { get; }

    // Validates that the given WabaId belongs to current tenant
    // Throws UnauthorizedAccessException if not
    Task ValidateWabaAccessAsync(Guid wabaId);
}