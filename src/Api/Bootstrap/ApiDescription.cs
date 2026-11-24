using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace EquipmentLibrary.Api.Bootstrap;

internal static class ApiDescription
{
    internal static void AddLibraryOpenApi(this IServiceCollection services)
    {
        const string scheme = "LibraryBearer";
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Equipment Library";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
                document.Components.SecuritySchemes[scheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    Description = "Use a library development persona locally or a token issued for the configured production audience."
                };
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
                {
                    operation.Security =
                    [
                        new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(scheme, context.Document)] = [] }
                    ];
                    string[] policies = metadata.OfType<IAuthorizeData>().Select(rule => rule.Policy)
                        .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
                    operation.Description = policies.Length == 0
                        ? "Requires an authenticated library identity."
                        : $"Required permissions: {string.Join(", ", policies)}.";
                }

                return Task.CompletedTask;
            });
        });
    }
}
