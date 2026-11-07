using FieldOps.Application.Common;

namespace FieldOps.Application.Operations;

public sealed record PageRequest(int Page = 1, int PageSize = 25)
{
    public int Offset => (Page - 1) * PageSize;

    public void Validate()
    {
        if (Page < 1 || PageSize is < 1 or > 100 || (Page - 1L) * PageSize > int.MaxValue)
            throw new FieldOpsException(ErrorKind.Validation, "invalid_page", "Page must be positive and page size must be between 1 and 100.");
    }
}

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
