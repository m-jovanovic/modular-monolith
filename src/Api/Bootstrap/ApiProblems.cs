using Microsoft.AspNetCore.Diagnostics;

namespace EquipmentLibrary.Api.Bootstrap;

internal static class ApiProblems
{
    internal static Task WriteAsync(StatusCodeContext status)
    {
        HttpContext context = status.HttpContext;
        int code = context.Response.StatusCode;
        (string identifier, string title) = code switch
        {
            StatusCodes.Status400BadRequest => ("invalid-request", "The request is invalid."),
            StatusCodes.Status401Unauthorized => ("authentication-required", "Authentication required."),
            StatusCodes.Status403Forbidden => ("forbidden", "Permission denied."),
            StatusCodes.Status404NotFound => ("not-found", "The requested resource does not exist."),
            StatusCodes.Status405MethodNotAllowed => ("method-not-allowed", "This method is unavailable for the requested resource."),
            _ => ("request-failed", "The request could not be completed.")
        };

        return Results.Problem(type: $"urn:equipment-library:problem:{identifier}", title: title,
            statusCode: code, extensions: new Dictionary<string, object?> { ["code"] = identifier }).ExecuteAsync(context);
    }
}
