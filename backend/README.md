# FlowForge backend

This is the .NET 10 modular-monolith scaffold. It includes PostgreSQL connectivity, OpenAPI, and health checks. No product endpoints, data models, migrations, authentication, or jobs have been implemented.

## Projects

- `src/FlowForge.Api`: HTTP host and future API composition.
- `src/FlowForge.Application`: use cases and module contracts.
- `src/FlowForge.Domain`: business rules and domain models.
- `src/FlowForge.Infrastructure`: EF Core context and PostgreSQL provider; future persistence and external integrations.
- `tests/FlowForge.Api.Tests`: scaffold smoke tests.

The references point inward: Application depends on Domain, Infrastructure depends on Application, and API composes Application and Infrastructure. Add feature code by business module within these projects, following [the architecture document](../docs/architecture.md). Do not create cross-module circular dependencies.

## Run with Docker Compose

Prerequisite: Docker with the Compose plugin. From `backend/`:

```sh
test -f .env || cp .env.example .env
# Edit .env and set POSTGRES_PASSWORD to a local development password.
docker compose up --build -d
docker compose ps
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready
curl http://localhost:8080/openapi/v1.json
```

Compose binds the API and PostgreSQL to localhost only. Change `API_PORT` or `POSTGRES_PORT` in `.env` if those ports are occupied. PostgreSQL data is kept in the `postgres_data` volume. Stop the stack with `docker compose down`; use `docker compose down -v` only when you intend to delete local database data.

`/health/live` checks the host. `/health/ready` checks that PostgreSQL accepts a connection. OpenAPI JSON is available only in Development; it contains no product operations yet.

## Run the API on the host

Prerequisite: the .NET 10 SDK. Start PostgreSQL with `docker compose up -d db`, then set the connection string to match the password and host port in `.env`. For example, with the default `POSTGRES_PORT=5432`:

```sh
export ConnectionStrings__FlowForge='Host=localhost;Port=5432;Database=flowforge;Username=flowforge;Password=your-local-password'
dotnet run --project src/FlowForge.Api --urls http://localhost:8080
```

Replace `your-local-password` with the value in `.env`. If `API_PORT=8080` is already in use by the Compose API container, stop that container with `docker compose stop api` first or choose a different URL for `dotnet run`. The connection string is required at startup. The API does not create tables or run migrations automatically.

## Build and test

```sh
dotnet restore FlowForge.sln -m:1
dotnet build FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
```
