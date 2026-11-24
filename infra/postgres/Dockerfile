FROM postgres:17.12-alpine3.23 AS sources
COPY src/Modules/ /modules/
RUN find /modules -type f ! -name '*.sql' -delete

FROM postgres:17.12-alpine3.23
WORKDIR /library
COPY infra/postgres/ infra/postgres/
COPY --from=sources /modules/ src/Modules/
ENTRYPOINT ["/library/infra/postgres/migrate.sh"]
