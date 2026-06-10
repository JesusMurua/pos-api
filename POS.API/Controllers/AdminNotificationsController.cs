using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Auth;
using POS.Services.Notifications;

namespace POS.API.Controllers;

/// <summary>
/// Read-only listing of the code-owned notification templates (PR-5, OQ-7: DB-editable
/// templates are deferred). For the future fino-admin UI. Authenticated via X-Admin-Token.
/// </summary>
[ApiController]
[Route("api/Admin/notification-templates")]
[Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)]
public class AdminNotificationsController : ControllerBase
{
    private readonly INotificationTemplateRegistry _registry;

    public AdminNotificationsController(INotificationTemplateRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Lists the available templates with their code + default recipient.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public IActionResult List() =>
        Ok(_registry.All().Select(t => new
        {
            code = t.Code,
            defaultRecipient = t.DefaultRecipient.ToString()
        }));
}
