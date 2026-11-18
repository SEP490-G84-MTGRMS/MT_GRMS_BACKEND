using System.Text.Json;
using FluentValidation;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Auth;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Admin.Services;

public sealed class TenantProvisioningService(
    ITenantRepository tenantRepository,
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IValidator<CreateTenantRequest> validator)
{
    public async Task<string> CreateAsync(
        CreateTenantRequest request,
        string actorId,
        string actorName,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var normalized = request with
        {
            Name = request.Name?.Trim() ?? string.Empty,
            Subdomain = request.Subdomain?.Trim().ToLowerInvariant() ?? string.Empty,
            OwnerName = request.OwnerName?.Trim() ?? string.Empty,
            OwnerEmail = request.OwnerEmail?.Trim().ToLowerInvariant() ?? string.Empty,
            OwnerPhone = request.OwnerPhone?.Trim() ?? string.Empty,
            Address = request.Address?.Trim(),
            City = request.City?.Trim(),
            TaxCode = request.TaxCode?.Trim()
        };
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
            throw new ArgumentException(string.Join(" ", validation.Errors.Select(error => error.ErrorMessage)));

        if (await tenantRepository.GetBySubdomainAsync(normalized.Subdomain, cancellationToken) != null)
            throw new InvalidOperationException("Subdomain này đã được sử dụng.");
        if (await userRepository.ExistsByPhoneOrEmailAsync(normalized.OwnerPhone, normalized.OwnerEmail, cancellationToken))
            throw new InvalidOperationException("Số điện thoại hoặc email chủ cửa hàng đã được đăng ký.");

        var tenant = new Tenant
        {
            Name = normalized.Name,
            Subdomain = normalized.Subdomain,
            Code = $"T-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            Hotline = normalized.OwnerPhone,
            Address = normalized.Address,
            City = normalized.City,
            TaxCode = normalized.TaxCode,
            Status = "ACTIVE",
            SubscriptionStatus = "NO_PLAN"
        };
        var owner = new User
        {
            UserName = normalized.OwnerPhone,
            FullName = normalized.OwnerName,
            Email = normalized.OwnerEmail,
            NormalizedEmail = normalized.OwnerEmail,
            Phone = normalized.OwnerPhone,
            NormalizedPhone = normalized.OwnerPhone,
            PasswordHash = passwordHasher.HashPassword(normalized.Password),
            IsActive = true,
            IsAdmin = false,
            IsEmailVerified = false,
            IsTwoFactorEnabled = false
        };
        var branch = new Branch
        {
            TenantId = tenant.Id,
            BranchCode = "BR-01",
            Name = "Chi nhánh chính",
            Phone = normalized.OwnerPhone,
            Address = normalized.Address,
            IsHeadquarter = true,
            IsActive = true
        };
        var audit = new AuditLog
        {
            TenantId = tenant.Id,
            ActorId = actorId,
            ActorName = actorName,
            ActorRole = UserRoleNames.Admin,
            ActionType = "TENANT_CREATED",
            TargetEntity = "Tenant",
            TargetId = tenant.Id,
            IpAddress = ipAddress,
            NewValuesJson = JsonSerializer.Serialize(new
            {
                tenant.Name,
                tenant.Subdomain,
                tenant.SubscriptionStatus,
                OwnerId = owner.Id,
                BranchId = branch.Id
            })
        };

        // The existing repository saves tenant, owner, membership, branch and audit in one transaction.
        await userRepository.RegisterStoreOwnerAsync(owner, tenant, branch, audit, cancellationToken);
        return tenant.Id;
    }
}
