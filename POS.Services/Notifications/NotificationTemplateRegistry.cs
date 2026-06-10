using POS.Domain.Exceptions;

namespace POS.Services.Notifications;

/// <inheritdoc />
public class NotificationTemplateRegistry : INotificationTemplateRegistry
{
    private readonly Dictionary<string, INotificationTemplate> _byCode;
    private readonly IReadOnlyList<INotificationTemplate> _all;

    public NotificationTemplateRegistry(IEnumerable<INotificationTemplate> templates)
    {
        _all = templates.ToList();
        _byCode = _all.ToDictionary(t => t.Code, StringComparer.Ordinal);
    }

    public INotificationTemplate Get(string code) =>
        _byCode.TryGetValue(code, out var t)
            ? t
            : throw new NotFoundException($"Notification template '{code}' is not registered.");

    public IReadOnlyList<INotificationTemplate> All() => _all;
}
