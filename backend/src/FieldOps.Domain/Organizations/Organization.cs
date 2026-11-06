namespace FieldOps.Domain.Organizations;

public sealed class Organization
{
    private Organization() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TimeZoneId { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Organization Create(string name, string timeZoneId, DateTimeOffset now)
    {
        var organization = new Organization { Id = Guid.NewGuid(), CreatedAt = now.ToUniversalTime() };
        organization.Update(name, timeZoneId, now);
        return organization;
    }

    public void Update(string name, string timeZoneId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Trim().Length > 160 || name.Any(char.IsControl))
        {
            throw new ArgumentException("Organization name must contain at most 160 characters.", nameof(name));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        // Accept IANA identifiers on both Windows and Linux; Windows display names
        // are not a portable organization setting.
        if (timeZoneId.Length > 100 || (timeZoneId != "UTC" && !TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out _)))
        {
            throw new ArgumentException("A valid IANA time zone is required.", nameof(timeZoneId));
        }

        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        Name = name.Trim();
        TimeZoneId = timeZoneId;
        UpdatedAt = now.ToUniversalTime();
    }
}
