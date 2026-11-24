using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EquipmentLibrary.Api.Identity;

internal sealed class LocalIdentityHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? bearer = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(bearer))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        (string? Subject, string[] Permissions) identity = bearer switch
        {
            "Bearer demo-borrower-a" => ("a0000000-0000-4000-8000-000000000001", ["Borrower"]),
            "Bearer demo-borrower-b" => ("a0000000-0000-4000-8000-000000000002", ["Borrower"]),
            "Bearer demo-staff" => ("a0000000-0000-4000-8000-000000000003", ["Staff", "Catalog.Manage", "Lending.Enroll", "Pickup.Manage", "Pickup.Operate"]),
            "Bearer demo-pickup" => ("a0000000-0000-4000-8000-000000000004", ["Staff", "Pickup.Manage"]),
            _ => (null, [])
        };
        if (identity.Subject is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown local equipment-library identity."));
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, identity.Subject) };
        claims.AddRange(identity.Permissions.Select(permission => new Claim("permission", permission)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
