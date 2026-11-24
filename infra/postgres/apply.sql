BEGIN;
SELECT pg_advisory_xact_lock(hashtext('equipment-library-migrations'), hashtext(:'module'));
SELECT EXISTS (SELECT FROM library_meta.migrations WHERE module = :'module' AND script = :'script') AS applied,
       EXISTS (SELECT FROM library_meta.migrations WHERE module = :'module' AND script = :'script' AND checksum <> :'checksum') AS changed
\gset
\if :changed
    \echo 'Previously applied migration has changed:' :module :script
    SELECT 1 / 0 AS migration_checksum_mismatch;
\elif :applied
    \echo 'Already applied:' :module :script
\else
    SET LOCAL ROLE :"owner";
    \i :file
    RESET ROLE;
    INSERT INTO library_meta.migrations (module, script, checksum) VALUES (:'module', :'script', :'checksum');
\endif
COMMIT;
