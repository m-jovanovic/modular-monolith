using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace EquipmentLibrary.Api.Bootstrap;

internal static class ApiDescription
{
    internal static void AddLibraryOpenApi(this IServiceCollection services)
    {
        const string scheme = "LibraryBearer";
        services.AddOpenApi(options =>
        {
            options.CreateSchemaReferenceId = type =>
            {
                string? id = OpenApiOptions.CreateDefaultSchemaReferenceId(type);
                return id is not null && type.Type.DeclaringType is { } feature ? feature.Name + id : id;
            };
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
            options.AddOperationTransformer(async (operation, context, cancellationToken) =>
            {
                IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
                {
                    operation.Security =
                    [
                        new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(scheme, context.Document)] = [] }
                    ];
                    IOpenApiSchema problem = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), cancellationToken: cancellationToken);
                    operation.Responses ??= new OpenApiResponses();
                    operation.Responses["401"] = AuthProblem("Authentication required.", problem);
                    operation.Responses["403"] = AuthProblem("Permission denied.", problem);

                    string[] policies = metadata.OfType<IAuthorizeData>().Select(rule => rule.Policy)
                        .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
                    operation.Description = policies.Length == 0
                        ? "Requires an authenticated library identity."
                        : $"Required permissions: {string.Join(", ", policies)}.";
                }
            });
        });
    }

    private static OpenApiResponse AuthProblem(string description, IOpenApiSchema schema) => new()
    {
        Description = description,
        Content = new Dictionary<string, IOpenApiMediaType>(StringComparer.Ordinal)
        {
            ["application/problem+json"] = new OpenApiMediaType { Schema = schema }
        }
    };
}
