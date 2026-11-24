# Replay one physical copy

A drill changed barcodes after staff discovered a transcription error. At revision 3 the drill was on loan under its original barcode. At revision 5 the barcode changed while the copy was available. Replaying the same history at either position preserves what staff knew then.

Start the reference database, then run the standalone lab.

```sh
docker compose up -d postgres
./labs/copy-history/run.sh
```

The script creates temporary PostgreSQL objects inside a transaction and rolls it back. To use a separate PostgreSQL 17 instance and the local `psql` binary, set `EQLIB_LAB_PG_URL` to its connection URL.

1. Load the eight JSON records in file order. Each append takes an expected stream version and verifies the resulting domain transition. [Source stream](fixtures/drill.ndjson)
2. Reconstruct every revision, compare it to independently recorded snapshots and discard the projection. Rebuild it a second time and check each row again. [Conventional audit](fixtures/audit.csv) · [Projection and assertions](verify.sql)
3. Try a stale writer and invalid events. PostgreSQL rolls back the rejected appends, including the stream head update. [Append and replay functions](store.sql)

On PostgreSQL 17.12, both rebuilds matched all eight conventional audit rows. The script prints each revision and exits nonzero if an assertion fails. [Architecture decision](../../docs/decisions/copy-history.md)
