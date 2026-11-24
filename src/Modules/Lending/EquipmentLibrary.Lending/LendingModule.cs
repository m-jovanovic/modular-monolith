using EquipmentLibrary.Lending.Copies;
using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentLibrary.Lending;

public static class LendingModule
{
    public static IServiceCollection AddLending(this IServiceCollection services)
    {
        services.AddScoped<LendingContext>();
        return services;
    }

    public static IEndpointRouteBuilder MapLending(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder lending = endpoints.MapGroup("/api/lending").WithTags("Lending");
        lending.MapPost("/copies", EnrollCopy.HandleAsync).RequireAuthorization("Lending.Enroll");
        lending.MapGet("/copies/{copyId:guid}", GetCopy.HandleAsync);
        lending.MapGet("/copies", FindCopies.HandleAsync);
        lending.MapGet("/availability/{catalogItemId:guid}", GetAvailability.HandleAsync);
        return endpoints;
    }
}
