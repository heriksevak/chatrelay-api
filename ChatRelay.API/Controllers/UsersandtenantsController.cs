// ============================================================
//  ChatRelay — UsersController
//  TenantAdmin creates/manages users within their tenant
//  SuperAdmin can manage all users across all tenants
// ============================================================

using ChatRelay.API.Context;
using ChatRelay.API.DTOs;
using ChatRelay.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


// ============================================================
//  ChatRelay — TenantsController
//  SuperAdmin only — full tenant lifecycle management
// ============================================================

    