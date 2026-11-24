namespace EquipmentLibrary.Lending.Delivery;

public sealed class HoldSubscriptions
{
    private readonly SortedDictionary<string, IHoldSubscriber> _receivers = new(StringComparer.Ordinal);

    public HoldSubscriptions(IEnumerable<IHoldSubscriber> receivers)
    {
        foreach (IHoldSubscriber receiver in receivers)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(receiver.Name);
            _receivers.Add(receiver.Name, receiver);
        }
    }

    public string[] SnapshotNames() => [.. _receivers.Keys];

    internal IHoldSubscriber Resolve(string name) => _receivers.TryGetValue(name, out IHoldSubscriber? receiver)
        ? receiver : throw new InvalidDataException($"The durable subscriber '{name}' is unavailable.");
}
