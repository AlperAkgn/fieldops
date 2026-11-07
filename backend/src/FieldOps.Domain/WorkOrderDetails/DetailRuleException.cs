namespace FieldOps.Domain.WorkOrderDetails;

public sealed class DetailRuleException(string code, string message, bool isConflict = false) : Exception(message)
{
    public string Code { get; } = code;
    public bool IsConflict { get; } = isConflict;
}

internal static class DetailText
{
    public static string Required(string? value, int limit, string code, bool multiline = false)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > limit ||
            value!.Any(character => char.IsControl(character) && !(multiline && character is '\n' or '\r' or '\t')))
            throw new DetailRuleException(code, $"Provide text between 1 and {limit} characters without unsupported control characters.");
        return trimmed;
    }
}
