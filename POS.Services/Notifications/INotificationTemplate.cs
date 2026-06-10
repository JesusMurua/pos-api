using POS.Domain.Enums;

namespace POS.Services.Notifications;

/// <summary>The rendered output of a notification template.</summary>
public readonly record struct RenderedEmail(string Subject, string BodyHtml, string BodyText);

/// <summary>
/// A code-owned email template (OQ-7: DB-editable templates are deferred). Single language
/// (es-MX). Each template declares its <see cref="Code"/> (matches NotificationOutbox.TemplateCode)
/// and its default recipient, and renders from a flat string payload.
/// </summary>
public interface INotificationTemplate
{
    string Code { get; }
    NotificationRecipientType DefaultRecipient { get; }

    /// <summary>
    /// Renders subject + html + text. Throws if a required payload key is missing — the
    /// dispatcher treats a render failure as a PermanentError (a bad payload won't fix on retry).
    /// </summary>
    RenderedEmail Render(IReadOnlyDictionary<string, string> payload);
}

/// <summary>Resolves templates by code. Backed by the DI-registered template set.</summary>
public interface INotificationTemplateRegistry
{
    INotificationTemplate Get(string code);
    IReadOnlyList<INotificationTemplate> All();
}
