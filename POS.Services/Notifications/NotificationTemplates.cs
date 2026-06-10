using POS.Domain.Enums;

namespace POS.Services.Notifications;

/// <summary>
/// Base for the code-owned es-MX templates. Provides a branded HTML shell + a plain-text
/// fallback and a required-key accessor. The 15 concrete templates below are registered in DI.
/// </summary>
public abstract class NotificationTemplateBase : INotificationTemplate
{
    public abstract string Code { get; }
    public abstract NotificationRecipientType DefaultRecipient { get; }
    public abstract RenderedEmail Render(IReadOnlyDictionary<string, string> payload);

    /// <summary>Required payload accessor — throws (→ PermanentError) when a key is missing.</summary>
    protected static string P(IReadOnlyDictionary<string, string> payload, string key) =>
        payload.TryGetValue(key, out var v)
            ? v
            : throw new KeyNotFoundException($"Template payload is missing required key '{key}'.");

    protected static RenderedEmail Compose(string subject, string heading, params string[] paragraphs)
    {
        var htmlParas = string.Concat(paragraphs.Select(p =>
            $"<p style=\"color:#4b5563;font-size:16px;line-height:1.6;margin:0 0 16px;\">{p}</p>"));
        var html = $"""
            <!DOCTYPE html>
            <html lang="es"><head><meta charset="UTF-8"></head>
            <body style="font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f5f5f5;margin:0;padding:0;">
              <table width="100%" cellpadding="0" cellspacing="0" style="padding:40px 20px;"><tr><td align="center">
                <table width="600" cellpadding="0" cellspacing="0" style="background:#fff;border-radius:8px;overflow:hidden;">
                  <tr><td style="background:#6366f1;padding:28px;text-align:center;"><h1 style="color:#fff;margin:0;font-size:24px;">Fino POS</h1></td></tr>
                  <tr><td style="padding:36px 32px;">
                    <h2 style="color:#1f2937;margin:0 0 16px;">{heading}</h2>
                    {htmlParas}
                  </td></tr>
                  <tr><td style="background:#f9fafb;padding:20px 32px;text-align:center;"><p style="color:#9ca3af;font-size:12px;margin:0;">Fino POS &mdash; Tu punto de venta inteligente</p></td></tr>
                </table>
              </td></tr></table>
            </body></html>
            """;
        var text = heading + "\n\n" + string.Join("\n\n", paragraphs);
        return new RenderedEmail(subject, html, text);
    }
}

public sealed class WelcomeTemplate : NotificationTemplateBase
{
    public override string Code => "Welcome";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.Custom;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "Bienvenido a Fino POS",
        $"¡Hola, {P(p, "name")}!",
        $"Bienvenido a <strong>Fino POS</strong>. Tu cuenta para el negocio <strong>{P(p, "businessName")}</strong> ya está activa.",
        "Tu periodo de prueba gratuito de <strong>3 meses</strong> ya corre. Configura tus productos, categorías y mesas desde la app.",
        "Si tienes alguna pregunta, responde a este correo y te ayudamos.");
}

public sealed class InvoiceCreatedTemplate : NotificationTemplateBase
{
    public override string Code => "InvoiceCreated";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.BillingEmail;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        $"Nueva factura #{P(p, "invoiceNumber")}",
        "Tienes una nueva factura",
        $"Se generó la factura <strong>#{P(p, "invoiceNumber")}</strong> por <strong>{P(p, "totalPesos")}</strong>.",
        $"Fecha límite de pago: <strong>{P(p, "dueDate")}</strong>.");
}

public sealed class PaymentReceivedTemplate : NotificationTemplateBase
{
    public override string Code => "PaymentReceived";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.BillingEmail;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "Pago recibido",
        "Recibimos tu pago",
        $"Registramos un pago de <strong>{P(p, "amountPesos")}</strong> sobre la factura <strong>#{P(p, "invoiceNumber")}</strong>.",
        "¡Gracias!");
}

public sealed class PaymentOverdueTemplate : NotificationTemplateBase
{
    public override string Code => "PaymentOverdue";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.BillingEmail;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        $"Factura vencida #{P(p, "invoiceNumber")}",
        "Tu factura está vencida",
        $"La factura <strong>#{P(p, "invoiceNumber")}</strong> por <strong>{P(p, "totalPesos")}</strong> lleva <strong>{P(p, "daysLate")}</strong> día(s) de retraso.",
        "Por favor realiza el pago para evitar la suspensión del servicio.");
}

public sealed class PaymentFailedTemplate : NotificationTemplateBase
{
    public override string Code => "PaymentFailed";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.BillingEmail;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "Pago rechazado",
        "No pudimos procesar tu pago",
        $"El cargo de tu suscripción fue rechazado. Reintentaremos automáticamente en los próximos días.",
        "Si el problema persiste, actualiza tu método de pago.");
}

public sealed class SubscriptionPriceChangedTemplate : NotificationTemplateBase
{
    public override string Code => "SubscriptionPriceChanged";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.Owner;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "Cambio en tu tarifa",
        "Actualizamos tu tarifa",
        $"Tu tarifa cambió de <strong>{P(p, "beforePesos")}</strong> a <strong>{P(p, "afterPesos")}</strong>.",
        $"Vigente a partir de: <strong>{P(p, "effectiveDate")}</strong>.");
}

public sealed class PlanChangedTemplate : NotificationTemplateBase
{
    public override string Code => "PlanChanged";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.Owner;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "Cambio de plan",
        "Tu plan cambió",
        $"Tu plan pasó de <strong>{P(p, "oldPlan")}</strong> a <strong>{P(p, "newPlan")}</strong>.");
}

public sealed class AddOnActivatedTemplate : NotificationTemplateBase
{
    public override string Code => "AddOnActivated";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.Owner;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "Complemento activado",
        "Activamos un complemento",
        $"Se activó <strong>{P(p, "addOnName")}</strong> (cantidad: {P(p, "quantity")}) en tu suscripción.");
}

public sealed class AddOnDeactivatedTemplate : NotificationTemplateBase
{
    public override string Code => "AddOnDeactivated";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.Owner;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "Complemento desactivado",
        "Desactivamos un complemento",
        $"Se desactivó <strong>{P(p, "addOnName")}</strong> de tu suscripción.");
}

public sealed class TrialExpiring3dTemplate : NotificationTemplateBase
{
    public override string Code => "TrialExpiring3d";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.Owner;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "Tu prueba termina pronto",
        "Quedan pocos días de prueba",
        $"Tu periodo de prueba del plan <strong>{P(p, "plan")}</strong> termina en <strong>3 días</strong>.",
        "Activa tu suscripción para no perder acceso.");
}

public sealed class TrialExpiring1dTemplate : NotificationTemplateBase
{
    public override string Code => "TrialExpiring1d";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.Owner;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "Tu prueba termina mañana",
        "Último día de prueba",
        $"Tu periodo de prueba del plan <strong>{P(p, "plan")}</strong> termina <strong>mañana</strong>.",
        "Activa tu suscripción hoy para mantener tu servicio activo.");
}

public sealed class TrialExpiredTemplate : NotificationTemplateBase
{
    public override string Code => "TrialExpired";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.Owner;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "Tu prueba terminó",
        "Tu periodo de prueba terminó",
        $"Tu prueba del plan <strong>{P(p, "plan")}</strong> ha terminado. Activa tu suscripción para reanudar el acceso completo.");
}

public sealed class TrialConvertedTemplate : NotificationTemplateBase
{
    public override string Code => "TrialConverted";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.Owner;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "¡Tu suscripción está activa!",
        "¡Bienvenido oficialmente!",
        $"Tu suscripción al plan <strong>{P(p, "plan")}</strong> quedó activa. ¡Gracias por confiar en Fino POS!");
}

public sealed class SuspendedTemplate : NotificationTemplateBase
{
    public override string Code => "Suspended";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.Owner;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "Cuenta suspendida",
        "Tu cuenta fue suspendida",
        $"Tu cuenta de Fino POS fue suspendida. Motivo: <strong>{(p.TryGetValue("reason", out var r) && !string.IsNullOrWhiteSpace(r) ? r : "no especificado")}</strong>.",
        "Contáctanos para reactivarla.");
}

public sealed class ReactivatedTemplate : NotificationTemplateBase
{
    public override string Code => "Reactivated";
    public override NotificationRecipientType DefaultRecipient => NotificationRecipientType.Owner;
    public override RenderedEmail Render(IReadOnlyDictionary<string, string> p) => Compose(
        "Cuenta reactivada",
        "Tu cuenta está activa de nuevo",
        "Reactivamos tu cuenta de Fino POS. ¡Bienvenido de vuelta!");
}
