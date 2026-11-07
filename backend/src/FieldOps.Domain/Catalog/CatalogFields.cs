namespace FieldOps.Domain.Catalog;

public enum CustomerType { Individual, Business }

public enum AssetStatus { Active, UnderMaintenance, OutOfService, Retired }

internal static class CatalogFields
{
    public static string Required(string? value, int maxLength, string field) =>
        Optional(value, maxLength, field) ?? throw new ArgumentException($"{field} is required.");

    public static string? Optional(string? value, int maxLength, string field, bool multiline = false)
    {
        if (value is null) return null;
        if (value.Any(character => char.IsControl(character) && !(multiline && character is '\r' or '\n' or '\t')))
            throw new ArgumentException($"{field} contains an invalid control character.");
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength) throw new ArgumentException($"{field} must contain at most {maxLength} characters.");
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static void RequireId(Guid id, string field)
    {
        if (id == Guid.Empty) throw new ArgumentException($"{field} is required.");
    }
}
