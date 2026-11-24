CREATE TEMP TABLE stream_head (copy_id uuid PRIMARY KEY, version integer NOT NULL);
CREATE TEMP TABLE copy_event (
    copy_id uuid NOT NULL REFERENCES stream_head,
    version integer NOT NULL CHECK (version > 0),
    kind text NOT NULL,
    schema_version integer NOT NULL,
    data jsonb NOT NULL,
    PRIMARY KEY (copy_id, version)
);

CREATE TYPE pg_temp.copy_summary AS (version integer, state text, barcode text, loans integer);

CREATE FUNCTION pg_temp.replay(copy uuid, through_version integer)
RETURNS pg_temp.copy_summary LANGUAGE plpgsql STABLE AS $$
DECLARE
    entry pg_temp.copy_event%ROWTYPE;
    snapshot pg_temp.copy_summary := (0, 'unregistered', NULL, 0);
BEGIN
    FOR entry IN SELECT * FROM pg_temp.copy_event e WHERE e.copy_id = copy AND e.version <= through_version ORDER BY e.version LOOP
        IF entry.version <> snapshot.version + 1 THEN
            RAISE EXCEPTION 'Missing version before %', entry.version USING ERRCODE = '22023';
        END IF;
        CASE
        WHEN entry.kind = 'copy-enrolled' AND snapshot.state = 'unregistered' AND entry.schema_version IN (1, 2) THEN
            snapshot.barcode := CASE entry.schema_version WHEN 1 THEN entry.data->>'assetTag' ELSE entry.data->>'barcode' END;
            snapshot.state := 'available';
        WHEN entry.kind = 'hold-placed' AND snapshot.state = 'available' AND entry.schema_version = 1 THEN
            snapshot.state := 'held';
        WHEN entry.kind = 'hold-collected' AND snapshot.state = 'held' AND entry.schema_version = 1 THEN
            snapshot.state := 'on-loan';
            snapshot.loans := snapshot.loans + 1;
        WHEN entry.kind = 'loan-returned' AND snapshot.state = 'on-loan' AND entry.schema_version = 1 THEN
            snapshot.state := 'available';
        WHEN entry.kind = 'hold-cancelled' AND snapshot.state = 'held' AND entry.schema_version IN (1, 2) THEN
            snapshot.state := 'available';
        WHEN entry.kind = 'barcode-corrected' AND snapshot.state <> 'unregistered' AND entry.schema_version = 2
             AND entry.data->>'previousBarcode' = snapshot.barcode AND length(entry.data->>'reason') > 0 THEN
            snapshot.barcode := entry.data->>'barcode';
        WHEN entry.kind = 'copy-retired' AND snapshot.state = 'available' AND entry.schema_version = 1 THEN
            snapshot.state := 'retired';
        ELSE
            RAISE EXCEPTION 'Unsupported transition % schema % from state %', entry.kind, entry.schema_version, snapshot.state USING ERRCODE = '22023';
        END CASE;
        IF coalesce(length(snapshot.barcode), 0) = 0 THEN
            RAISE EXCEPTION 'Missing barcode in version %', entry.version USING ERRCODE = '22023';
        END IF;
        snapshot.version := entry.version;
    END LOOP;
    IF snapshot.version <> through_version THEN
        RAISE EXCEPTION 'Missing version %', through_version USING ERRCODE = '22023';
    END IF;
    RETURN snapshot;
END;
$$;

CREATE FUNCTION pg_temp.append_event(copy uuid, expected_version integer, kind text, schema_version integer, data jsonb)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    next_version integer;
BEGIN
    INSERT INTO pg_temp.stream_head VALUES (copy, 0) ON CONFLICT (copy_id) DO NOTHING;
    UPDATE pg_temp.stream_head h SET version = h.version + 1
    WHERE h.copy_id = copy AND h.version = expected_version RETURNING version INTO next_version;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Stale copy version %', expected_version USING ERRCODE = '40001';
    END IF;
    INSERT INTO pg_temp.copy_event VALUES (copy, next_version, kind, schema_version, data);
    PERFORM pg_temp.replay(copy, next_version);
END;
$$;
