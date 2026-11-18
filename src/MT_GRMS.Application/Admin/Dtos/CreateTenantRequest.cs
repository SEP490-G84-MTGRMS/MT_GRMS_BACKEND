namespace MT_GRMS.Application.Admin.Dtos;

public record CreateTenantRequest(
    string Name,
    string Subdomain,
    string OwnerName,
    string OwnerEmail,
    string OwnerPhone,
    string Password,
    string? Address = null,
    string? City = null,
    string? TaxCode = null
);
