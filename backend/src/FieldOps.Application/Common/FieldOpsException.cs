namespace FieldOps.Application.Common;

public enum ErrorKind
{
    Validation,
    Unauthenticated,
    Forbidden,
    NotFound,
    Conflict,
    Unavailable
}

// Messages are deliberate, public-safe application errors; unexpected provider errors remain private.
public sealed class FieldOpsException(ErrorKind kind, string code, string message) : Exception(message)
{
    public ErrorKind Kind { get; } = kind;
    public string Code { get; } = code;
}
