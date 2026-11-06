using FieldOps.Application.Identity;
using FieldOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Email;

internal sealed class EmailDispatcher(FieldOpsDbContext database, IDataProtectionProvider protection,
    IEmailDelivery delivery, TimeProvider clock, ILogger<EmailDispatcher> logger) : IEmailDispatcher
{
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        using var system = database.BeginSystemScope();
        var delivered = 0;
        var now = clock.GetUtcNow();
        await database.OutboundEmails.Where(mail => mail.CreatedAt < now.AddDays(-7)).ExecuteDeleteAsync(cancellationToken);
        for (var index = 0; index < 10; index++)
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var email = await database.OutboundEmails.FromSqlInterpolated($"""
                SELECT * FROM "OutboundEmails"
                WHERE NOT "IsDeadLetter" AND "NextAttemptAt" <= {now}
                ORDER BY "CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).SingleOrDefaultAsync(cancellationToken);
            if (email is null)
            {
                await transaction.CommitAsync(cancellationToken);
                break;
            }

            try
            {
                var body = protection.CreateProtector("FieldOps.EmailOutbox.v1").Unprotect(email.ProtectedBody);
                await delivery.SendAsync(new EmailMessage(email.ToEmail, email.Subject, body), cancellationToken);
                database.OutboundEmails.Remove(email);
                delivered++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                email.Attempts++;
                email.IsDeadLetter = email.Attempts >= 8;
                email.NextAttemptAt = now.AddSeconds(Math.Min(3600, Math.Pow(2, email.Attempts)));
                email.LastFailureType = exception.GetType().Name;
                logger.LogWarning("Email delivery deferred with {FailureType}; attempt {Attempt}", email.LastFailureType, email.Attempts);
            }

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return delivered;
    }
}
