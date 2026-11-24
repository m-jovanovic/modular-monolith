using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace EquipmentLibrary.Api.Identity;

internal static class LibraryAuthentication
{
    internal static void AddLibraryAuthentication(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            builder.Services.AddAuthentication("LocalLibrary")
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, LocalIdentityHandler>("LocalLibrary", null);
        }
        else
        {
            IConfigurationSection settings = builder.Configuration.GetRequiredSection("Authentication");
            string authority = settings["Authority"] ?? throw new InvalidOperationException("Configure Authentication:Authority.");
            string audience = settings["Audience"] ?? throw new InvalidOperationException("Configure Authentication:Audience.");
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = authority;
                    options.Audience = audience;
                    options.RequireHttpsMetadata = true;
                });
        }

        AuthorizationBuilder authorization = builder.Services.AddAuthorizationBuilder();
        foreach (string permission in new[] { "Catalog.Manage", "Lending.Enroll", "Pickup.Manage", "Pickup.Operate", "Borrower", "Staff" })
        {
            authorization.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().RequireClaim("permission", permission));
        }
    }
}
