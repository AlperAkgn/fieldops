using System.Net.Mail;
using FieldOps.Domain.Organizations;

namespace FieldOps.Domain.Catalog;

public sealed class Customer : ITenantEntity
{
    private Customer() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public CustomerType CustomerType { get; private set; }
    public string Name { get; private set; } = "";
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static Customer Create(Guid tenantId, CustomerDetails details, DateTimeOffset now)
    {
        CatalogFields.RequireId(tenantId, "Tenant");
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = tenantId, CreatedAt = now.ToUniversalTime() };
        customer.Update(details, now);
        return customer;
    }

    public void Update(CustomerDetails details, DateTimeOffset now)
    {
        if (!IsActive) throw new InvalidOperationException("An archived customer cannot be edited.");
        if (!Enum.IsDefined(details.CustomerType)) throw new ArgumentException("Customer type is invalid.");
        var name = CatalogFields.Required(details.Name, 160, "Name");
        var email = CatalogFields.Optional(details.Email, 254, "Email");
        if (email is not null && (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
            throw new ArgumentException("Email must be an email address.");
        var phone = CatalogFields.Optional(details.Phone, 50, "Phone");
        var notes = CatalogFields.Optional(details.Notes, 4000, "Notes", multiline: true);

        CustomerType = details.CustomerType;
        Name = name;
        Email = email;
        Phone = phone;
        Notes = notes;
        UpdatedAt = now.ToUniversalTime();
        Version++;
    }

    public void Archive(DateTimeOffset now)
    {
        if (!IsActive) return;
        IsActive = false;
        UpdatedAt = now.ToUniversalTime();
        Version++;
    }
}

public sealed record CustomerDetails(CustomerType CustomerType, string? Name, string? Email = null,
    string? Phone = null, string? Notes = null);
