using FieldOps.Application.Common;
using FieldOps.Domain.WorkOrderDetails;

namespace FieldOps.Infrastructure.WorkOrderDetails;

internal static class DetailRules
{
    public static FieldOpsException Missing(string code, string message) => new(ErrorKind.NotFound, code, message);
    public static FieldOpsException Invalid(string code, string message) => new(ErrorKind.Validation, code, message);
    public static FieldOpsException Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);

    public static void Apply(Action action) => Apply(() =>
    {
        action();
        return true;
    });

    public static T Apply<T>(Func<T> action)
    {
        try { return action(); }
        catch (DetailRuleException exception)
        {
            throw new FieldOpsException(exception.IsConflict ? ErrorKind.Conflict : ErrorKind.Validation,
                exception.Code, exception.Message);
        }
    }
}
