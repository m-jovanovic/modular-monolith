using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace EquipmentLibrary.Lab.JetStream;

public static class HoldStream
{
    public const string Subject = "library.lending.holds.v1";
    public const string Stream = "LIBRARY_HOLDS";
    public const string Consumer = "PICKUP_DESK";

    public static void AddHoldStream(this IServiceCollection services, string url)
    {
        services.AddSingleton(_ => new NatsConnection(NatsOpts.Default with { Url = url }));
        services.AddSingleton(sp => new NatsJSContext(sp.GetRequiredService<NatsConnection>()));
    }

    public static async Task PrepareAsync(this NatsJSContext jetStream)
    {
        await jetStream.CreateOrUpdateStreamAsync(new StreamConfig(Stream, [Subject])
        {
            Storage = StreamConfigStorage.File,
            Retention = StreamConfigRetention.Workqueue,
            Discard = StreamConfigDiscard.New,
            MaxBytes = 64 * 1024 * 1024
        });
        await jetStream.CreateOrUpdateConsumerAsync(Stream, new ConsumerConfig(Consumer)
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromSeconds(10),
            FilterSubject = Subject
        });
    }
}
