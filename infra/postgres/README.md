# PostgreSQL bootstrap and role ownership

The migrator connects as the provisioner. The three non-login roles own the module schemas and migrations. The API connects as `catalog_app`, `lending_app` or `pickup_desk_app`. Those roles receive table DML and sequence access within their own schemas. The migration ledger belongs to the provisioner.

PostgreSQL roles are cluster-wide. Two equipment databases on one PostgreSQL cluster therefore share these named logins and passwords. The first successful bootstrap creates each login with the supplied password. Subsequent migrations preserve it. For independent security identities, provision independent clusters or choose distinct role names and grants as part of your deployment design.

Every initializer connects to the same maintenance database, `postgres` by default, while creating the role pair for a module. Override `EQLIB_ROLE_DATABASE` consistently across all initializers when a provider supplies a different maintenance database. PostgreSQL advisory locks are scoped to a database. Taking the role lock in the common database coordinates clients even when their destination databases differ. The provisioner needs access to this database and permission to create roles and provision the target database.

Each target uses a separate transaction lock while creating its ledger. Module bootstrap and scripts share a per-module transaction lock in the target. The schema search path for each runtime login is set specifically for that database. A committed script is identified by its module, filename and checksum. A rolled-back script can be attempted again.

## Rotate a password

Change the login's password explicitly through your PostgreSQL administrator and update the secrets used by every deployment sharing that login. For an interactive administrator session, PostgreSQL's `\password catalog_app` command prompts for the new value. Roll the applications or refresh their connection pools so new connections use the updated secret. Existing authenticated connections can survive the change. Changing a Compose environment value and rerunning migrations preserves the existing password.

Run [the persistence suite](../../tests/Persistence/verify.sh) to exercise parallel first-time initialization across two databases, concurrent migration of one empty database, upgrades with prior data, altered-script rejection, module grants and cluster-wide credential rotation.
