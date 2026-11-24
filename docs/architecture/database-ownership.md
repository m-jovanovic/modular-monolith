# Database ownership

Run `./tests/Persistence/verify.sh` with Docker available. The script creates a disposable PostgreSQL cluster with the same database engine as the app. It applies the production migration scripts to an empty database and to a second database seeded at the oldest supported schema. It checks preservation of data, a repeat application, and rejection of a changed historical migration. The cluster is removed at exit.

Each module has an owner role that applies its migrations and a separate runtime login. Runtime queries on the module’s own rows must succeed. The suite connects separately as each runtime login and requires SQLSTATE 42501 (insufficient privilege) on foreign reads, writes, joins and views, on DDL, on the migration ledger, and when attempting to assume the owner role. Test-only sentinel writes are rolled back. [Executable role matrix](../../tests/Persistence/inside.sh)
