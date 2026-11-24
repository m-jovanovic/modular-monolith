BEGIN;
-- Share the module lock with apply.sql so grants and module DDL cannot overlap.
SELECT pg_advisory_xact_lock(hashtext('equipment-library-migrations'), hashtext(:'module'));
CREATE SCHEMA IF NOT EXISTS :"schema" AUTHORIZATION :"owner";
GRANT USAGE ON SCHEMA :"schema" TO :"app";
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA :"schema"
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app";
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA :"schema"
    GRANT USAGE, SELECT ON SEQUENCES TO :"app";
SELECT current_database() AS database_name \gset
ALTER ROLE :"app" IN DATABASE :"database_name" SET search_path = :"schema", pg_catalog;
COMMIT;
