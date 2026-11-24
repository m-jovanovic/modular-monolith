-- The owner applies module migrations. Requests use a separate login per schema.
SELECT format('CREATE ROLE %I NOLOGIN', :'owner')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'owner') \gexec
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'app', :'password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'app') \gexec
CREATE SCHEMA IF NOT EXISTS :"schema" AUTHORIZATION :"owner";
GRANT USAGE ON SCHEMA :"schema" TO :"app";
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA :"schema"
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app";
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA :"schema"
    GRANT USAGE, SELECT ON SEQUENCES TO :"app";
ALTER ROLE :"app" SET search_path = :"schema", pg_catalog;
