# FlowForge backend

This is the .NET 10 modular-monolith backend. It includes PostgreSQL connectivity, OpenAPI, health checks, and the first Identity endpoints.

## Projects

- `src/FlowForge.Api`: HTTP host, auth endpoints, and client-facing request/response DTOs in `Contracts/Identity`.
- `src/FlowForge.Application`: use cases and module contracts.
- `src/FlowForge.Domain`: business rules and domain models.
- `src/FlowForge.Infrastructure`: EF Core context and PostgreSQL provider; future persistence and external integrations.
- `tests/FlowForge.Api.Tests`: scaffold smoke tests.

The references point inward: Application depends on Domain, Infrastructure depends on Application, and API composes Application and Infrastructure. Add feature code by business module within these projects, following [the architecture document](../docs/architecture.md). Do not create cross-module circular dependencies.

## Run with Docker Compose

Prerequisite: Docker with the Compose plugin. From `backend/`:

```sh
test -f .env || cp .env.example .env
# Edit .env and set POSTGRES_PASSWORD and JWT_SIGNING_KEY.
# Generate a signing key with: openssl rand -base64 48
docker compose up --build -d
docker compose ps
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready
curl http://localhost:8080/openapi/v1.json
```

Compose binds the API and PostgreSQL to localhost only. Change `API_PORT` or `POSTGRES_PORT` in `.env` if those ports are occupied. PostgreSQL data is kept in the `postgres_data` volume. Stop the stack with `docker compose down`; use `docker compose down -v` only when you intend to delete local database data.

`/health/live` checks the host. `/health/ready` checks that PostgreSQL accepts a connection. OpenAPI JSON is available only in Development. Compose applies EF Core migrations when the API starts.

## Run the API on the host

Prerequisite: the .NET 10 SDK. Start PostgreSQL with `docker compose up -d db`, then set the connection string to match the password and host port in `.env`. For example, with the default `POSTGRES_PORT=5432`:

```sh
export ConnectionStrings__FlowForge='Host=localhost;Port=5432;Database=flowforge;Username=flowforge;Password=your-local-password'
export Jwt__SigningKey="$(openssl rand -base64 48)"
dotnet ef database update --project src/FlowForge.Infrastructure --startup-project src/FlowForge.Api
dotnet run --project src/FlowForge.Api --urls http://localhost:8080
```

Replace `your-local-password` with the value in `.env`. If `API_PORT=8080` is already in use by the Compose API container, stop that container with `docker compose stop api` first or choose a different URL for `dotnet run`. The connection string and a signing key of at least 32 bytes are required at startup. Host runs use the explicit migration command above. Keep the signing key stable across restarts so existing access tokens remain valid.

## Authentication API

Endpoints under `/api/v1/auth`:

| Method | Path | Request | Result |
| --- | --- | --- | --- |
| POST | `/register` | `email`, `password`, `displayName` | 201 and tokens |
| POST | `/login` | `email`, `password` | 200 and tokens |
| POST | `/refresh` | `refreshToken` | 200 and rotated tokens |
| POST | `/logout` | `refreshToken` | 204; revokes that session family |
| GET | `/me` | `Authorization: Bearer <accessToken>` | Current user |

Registration requires a 12–128 character password. Access tokens expire after 15 minutes; refresh tokens expire after 30 days and rotate on use. The API stores only hashes of passwords and refresh tokens. Reusing a rotated refresh token revokes its entire session family. Logout revokes refresh sessions; issued access tokens remain valid until expiry. Use HTTPS outside local development and keep tokens out of logs.

## Build and test

GitHub Actions runs these commands on pushes and pull requests; the current tests do not need PostgreSQL or application secrets.

```sh
dotnet restore FlowForge.sln -m:1
dotnet build FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
```
