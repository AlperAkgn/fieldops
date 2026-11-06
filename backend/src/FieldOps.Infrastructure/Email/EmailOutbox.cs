using FieldOps.Application.Identity;
using FieldOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace FieldOps.Infrastructure.Email;

internal sealed class EmailOutbox(FieldOpsDbContext database, IDataProtectionProvider protection, TimeProvider clock) : IEmailOutbox
{
    public void Enqueue(EmailMessage message)
    {
        var now = clock.GetUtcNow();
        database.OutboundEmails.Add(new OutboundEmail
        {
            ToEmail = message.ToEmail,
            Subject = message.Subject,
            ProtectedBody = protection.CreateProtector("FieldOps.EmailOutbox.v1").Protect(message.Body),
            CreatedAt = now,
            NextAttemptAt = now
        });
    }
}
