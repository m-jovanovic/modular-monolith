-- All database initializers take this transaction lock in the same maintenance database.
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('equipment-library-role-bootstrap'), hashtext(:'owner'));
SELECT format('CREATE ROLE %I NOLOGIN', :'owner')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'owner') \gexec
\getenv password MODULE_PASSWORD
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'app', :'password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'app') \gexec
COMMIT;
