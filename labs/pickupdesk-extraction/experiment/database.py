"""Disposable databases and restricted runtime roles in the local Compose PostgreSQL."""
import os
from pathlib import Path
import secrets
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[3]
MODULES = {"Catalog": "catalog", "Lending": "lending", "PickupDesk": "pickup_desk"}


def sql(database, statement):
    command = ["docker", "compose", "exec", "-T", "postgres", "psql",
               "-XAtq", "-v", "ON_ERROR_STOP=1", "-U", "library_admin", "-d", database]
    return subprocess.run(command, cwd=ROOT, input=statement, text=True,
                          stdout=subprocess.PIPE, check=True, timeout=30).stdout.strip()


class Databases:
    def __init__(self, cleanup):
        self.prefix = "lab_" + uuid.uuid4().hex[:16]
        self.cleanup = cleanup
        self.credentials = {}
        for module, schema in MODULES.items():
            role = self.prefix + "_" + schema
            password = secrets.token_hex(16)
            sql("postgres", f"CREATE ROLE {role} LOGIN PASSWORD '{password}'")
            cleanup.callback(sql, "postgres", f"DROP ROLE {role}")
            self.credentials[module] = role, password

    def create(self, suffix, modules):
        database = self.prefix + "_" + suffix
        sql("postgres", f"CREATE DATABASE {database}")
        self.cleanup.callback(sql, "postgres", f"DROP DATABASE {database} WITH (FORCE)")
        sql("postgres", f"REVOKE ALL ON DATABASE {database} FROM PUBLIC")
        connections = {}
        for module in modules:
            schema = MODULES[module]
            role, password = self.credentials[module]
            sql("postgres", f"GRANT CONNECT ON DATABASE {database} TO {role}")
            migrations = sorted((ROOT / "src/Modules" / module / "Migrations").glob("*.sql"))
            statements = [f"CREATE SCHEMA {schema};"] + [path.read_text() for path in migrations] + [
                f"GRANT USAGE ON SCHEMA {schema} TO {role};",
                f"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO {role};",
                f"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {schema} TO {role};"
            ]
            sql(database, "\n".join(statements))
            port = int(os.getenv("LIBRARY_PG_PORT", "55432"))
            connections["ConnectionStrings__" + module] = (
                f"Host=127.0.0.1;Port={port};Database={database};Username={role};Password={password}")
        return database, connections
