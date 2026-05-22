using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace FitQuest.Api.Controllers;

public static class ControllerExtensions
{
    public static int CurrentUserId(this ControllerBase controller)
    {
        var id = controller.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var userId) ? userId : 0;
    }
}
