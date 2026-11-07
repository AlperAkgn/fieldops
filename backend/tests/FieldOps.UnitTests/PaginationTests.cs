using FieldOps.Application.Common;
using FieldOps.Application.Operations;

namespace FieldOps.UnitTests;

public sealed class PaginationTests
{
    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void InvalidPagesFailBeforeOffsetArithmetic(int page, int size)
    {
        var exception = Assert.Throws<FieldOpsException>(() => new PageRequest(page, size).Validate());
        Assert.Equal(ErrorKind.Validation, exception.Kind);
    }

    [Fact]
    public void PageOffsetUsesOneBasedPageNumbers()
    {
        var page = new PageRequest(3, 25);
        page.Validate();
        Assert.Equal(50, page.Offset);
    }
}
