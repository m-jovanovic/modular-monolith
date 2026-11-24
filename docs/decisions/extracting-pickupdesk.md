# Keep PickupDesk in process until independent operation pays for itself

**Decision.** The equipment library's default remains one API process. Staff and borrower workflows can ship together. The public event contract and idempotent PickupDesk inbox give a future team a tested place to introduce transport when it needs its own deployment cadence or failure boundary.

**Experiment.** Both profiles ran 24 real hold placements while healthy. Each then accepted 12 cancellations during a paused projection. The stock host combines the command endpoints and projection worker. Stopping that process took its HTTP API offline. The harness restarted it with workers paused so its command endpoints could accept the identical cancellation workload. In the extracted profile the harness stopped only the PickupDesk process and left Lending running. [Runnable experiment](../../labs/pickupdesk-extraction/README.md)

| Observation | Stock host | Extracted PickupDesk |
| --- | --- | --- |
| Healthy occurrence-to-projection p50 / p95 | 342.3 / 415.6 ms | 170.1 / 181.5 ms |
| Command host answered while the process containing PickupDesk was stopped | No | Yes |
| Pending cancellations with projection paused | 12 in Lending outbox | 12 in broker, zero in Lending outbox |
| Active holds before / after recovery | 24 / 12 | 24 / 12 |
| Restart plus catch-up | 494 ms | 588 ms |
| Completed unique inbox receipts after recovery | 36 | 36 |

These observations came from one local arm64 run of .NET 11 with PostgreSQL 17.12 and NATS Server 2.15.3. The 250 ms polling intervals and process startup timing influence the measured latencies. Twenty-four sequential writes on one machine provide evidence about this workload. They cannot predict networked production performance or establish a general speed advantage.

**Operational consequences.** A separately staffed pickup desk can release its interface or restart its projector while borrowers keep placing holds. The operator now owns two process deployments, broker capacity and retention, a durable consumer and two acknowledgement boundaries. A broker storage acknowledgement lets Lending finish an outbox delivery. It says nothing about whether a staff member can already see the effect. Operators should inspect broker backlog and the PickupDesk inbox as well as the Lending outbox. The sample broker caps file-backed storage at 64 MiB and refuses new messages when full. Production use would need alerting, replication, retention sizing, service authentication and tested broker recovery.

**Deployment.** The baseline builds one API and migrates three module schemas. The extracted profile builds and releases the publisher and PickupDesk independently, migrates the source and projection databases, provisions JetStream and configures a versioned subject and durable consumer. This experiment provisions the stream and consumer on host startup. A production operator can pre-provision them and give each service narrower broker permissions. The unchanged v1 hold envelope is the compatibility boundary during staggered deployment. [JetStream acknowledgements](https://nats-io.github.io/nats.net/documentation/jetstream/consume.html) · [Stream and durable consumer management](https://nats-io.github.io/nats.net/documentation/jetstream/manage.html)
