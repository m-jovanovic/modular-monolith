DO $$
DECLARE
    line jsonb;
BEGIN
    FOR line IN SELECT entry FROM fixture_event ORDER BY position LOOP
        PERFORM pg_temp.append_event((line->>'copyId')::uuid, (line->>'version')::integer - 1,
            line->>'type', (line->>'schemaVersion')::integer, line->'data');
    END LOOP;
END;
$$;

CREATE TEMP TABLE replayed (LIKE conventional_audit INCLUDING ALL);
CREATE FUNCTION pg_temp.rebuild() RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    TRUNCATE pg_temp.replayed;
    INSERT INTO pg_temp.replayed
    SELECT snapshot.* FROM pg_temp.copy_event e
    CROSS JOIN LATERAL pg_temp.replay(e.copy_id, e.version) snapshot;
END;
$$;

DO $$
DECLARE
    copy uuid := (SELECT copy_id FROM pg_temp.stream_head);
    old_snapshot pg_temp.copy_summary;
    before_correction pg_temp.copy_summary;
    after_correction pg_temp.copy_summary;
BEGIN
    FOR attempt IN 1..2 LOOP
        PERFORM pg_temp.rebuild();
        IF EXISTS ((TABLE pg_temp.conventional_audit EXCEPT TABLE pg_temp.replayed)
            UNION ALL (TABLE pg_temp.replayed EXCEPT TABLE pg_temp.conventional_audit)) THEN
            RAISE EXCEPTION 'Rebuilt revisions disagree with the conventional snapshots';
        END IF;
    END LOOP;

    old_snapshot := pg_temp.replay(copy, 3);
    before_correction := pg_temp.replay(copy, 4);
    after_correction := pg_temp.replay(copy, 5);
    IF old_snapshot.state <> 'on-loan' OR before_correction.barcode <> 'DRILL-018' OR after_correction.barcode <> 'DRILL-081' THEN
        RAISE EXCEPTION 'Historical reconstruction failed';
    END IF;
    BEGIN
        PERFORM pg_temp.append_event(copy, 7, 'hold-placed', 1, '{}');
        RAISE EXCEPTION 'A stale writer was accepted';
    EXCEPTION WHEN serialization_failure THEN NULL;
    END;
    BEGIN
        PERFORM pg_temp.append_event(copy, 8, 'hold-placed', 1, '{}');
        RAISE EXCEPTION 'Retired copy accepted a hold';
    EXCEPTION WHEN invalid_parameter_value THEN NULL;
    END;
    BEGIN
        PERFORM pg_temp.append_event(copy, 8, 'copy-retired', 99, '{}');
        RAISE EXCEPTION 'Unsupported schema was accepted';
    EXCEPTION WHEN invalid_parameter_value THEN NULL;
    END;
    IF (SELECT version FROM pg_temp.stream_head WHERE copy_id = copy) <> 8 THEN
        RAISE EXCEPTION 'Failed append advanced the stream';
    END IF;
END;
$$;

TABLE replayed ORDER BY version;
SELECT '8 revisions matched; 2 rebuilds; stale and invalid appends rolled back' AS result;
ROLLBACK;
