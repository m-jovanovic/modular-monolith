using EquipmentLibrary.Lending.Domain;

namespace EquipmentLibrary.Lending.Application.Maintenance;

public sealed record MaintenanceOrder(Guid Id, Guid CopyId, string Reason, DateTimeOffset OpenedAt,
    string? Outcome, string? Resolution, DateTimeOffset? ClosedAt);

public abstract record MaintenanceResult
{
    private MaintenanceResult() { }

    public sealed record Saved(MaintenanceOrder Order) : MaintenanceResult;
    public sealed record InvalidNote : MaintenanceResult;
    public sealed record CopyMissing : MaintenanceResult;
    public sealed record CopyUnavailable : MaintenanceResult;
    public sealed record OrderMissing : MaintenanceResult;
    public sealed record AlreadyResolved : MaintenanceResult;
}

public interface IMaintenanceOrders
{
    Task<MaintenanceResult> OpenAsync(Guid copyId, string reason, DateTimeOffset now, CancellationToken cancellationToken);

    Task<MaintenanceResult> ResolveAsync(Guid orderId, MaintenanceResolution decision, string note,
        DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record OpenMaintenanceOrder(Guid CopyId, string? Reason);
public sealed record ResolveMaintenanceOrder(Guid OrderId, MaintenanceResolution Decision, string? Note);

public sealed class MaintenanceOrderHandler(IMaintenanceOrders orders, TimeProvider clock)
{
    public Task<MaintenanceResult> HandleAsync(OpenMaintenanceOrder command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Reason?.Trim() is { Length: > 0 and <= 500 } reason
            ? orders.OpenAsync(command.CopyId, reason, clock.GetUtcNow(), cancellationToken)
            : Task.FromResult<MaintenanceResult>(new MaintenanceResult.InvalidNote());
    }

    public Task<MaintenanceResult> HandleAsync(ResolveMaintenanceOrder command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Decision);
        return command.Note?.Trim() is { Length: > 0 and <= 500 } note
            ? orders.ResolveAsync(command.OrderId, command.Decision, note, clock.GetUtcNow(), cancellationToken)
            : Task.FromResult<MaintenanceResult>(new MaintenanceResult.InvalidNote());
    }
}
