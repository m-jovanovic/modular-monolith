using System.Security.Claims;

namespace EquipmentLibrary.Lending;

internal static class BorrowerIdentity
{
    internal static Guid? Read(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out Guid id) && id != Guid.Empty ? id : null;
}
