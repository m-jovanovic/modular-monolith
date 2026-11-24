# Run PickupDesk as an independent service

Compare the reference host with two independently executable .NET 11 services connected by NATS JetStream. Both paths run the production Catalog and Lending HTTP use cases, outbox relay, PickupDesk inbox and staff queue endpoints. The extracted publisher has Catalog and Lending database connections. The extracted PickupDesk host receives its own database and user.

```sh
docker compose up -d postgres
./labs/pickupdesk-extraction/run.py
```

The experiment requires the repository's .NET SDK, Python 3 and Docker Compose. It creates unique databases, roles and loopback ports. It starts a pinned single-node broker container for the extracted run. To use the same broker outside Docker, set `NATS_SERVER` to the path of the v2.15.3 executable. The harness verifies its version. The broker credentials and storage belong to this run. The harness removes its resources on completion. [Pinned broker release](https://github.com/nats-io/nats-server/releases/tag/v2.15.3)

## What happens

1. In the baseline, the usual host creates a catalog item, enrolls 24 copies and accepts a hold on each. The harness waits for all 24 to appear on the staff queue.
2. The harness stops the host, probes availability, then restarts it with `Workers:Enabled=false` to run the same cancellation workload with projection paused. Twelve cancellations commit and stay in the source outbox. The running projection still showed 24 holds just before the stop.
3. Resuming workers requires a baseline process restart. Staff see 12 active holds and 12 closed holds after catch-up.
4. In the extracted profile, stopping only PickupDesk leaves Catalog and Lending available. The publisher sends all 12 cancellations during this outage. JetStream retains them. After restarting PickupDesk, its receiver stores the events in the production inbox and acknowledges each broker delivery. The production projection worker processes those receipts.
5. The harness verifies the recovered hold identities on the staff history route, 36 completed durable receipts and a drained broker.

The output includes healthy event-to-projection p50 and p95, outstanding messages at the paused boundary, source availability and elapsed process restart plus catch-up time. Healthy latency uses the event's UTC occurrence time and the PickupDesk receipt's completion time. Recovery uses a monotonic stopwatch and includes process startup. [Workload assertions](experiment/workload.py) · [Observed results and decision](../../docs/decisions/extracting-pickupdesk.md)

## Inspect the deployment boundary

The publisher registers the regular modules and replaces the in-process subscription with a JetStream publisher that awaits a broker storage acknowledgement. PickupDesk runs its regular staff routes and workers. Its broker receiver acknowledges only after durable inbox acceptance. A crash between acceptance and acknowledgement can redeliver the envelope. The production inbox deduplicates by subscriber and message ID. [Publisher](Publisher/JetStreamSubscription.cs) · [Receiver](PickupService/ReceiveHoldMessages.cs) · [Broker configuration](Transport/HoldTransport.cs)

This profile uses a single broker and loopback development identities for a local experiment. Operating it across machines adds workload authentication, network security, replicated broker storage, compatible contract rollout, two independent release pipelines and on-call ownership of replay and redrive.
