# FlowForge

A modular project management platform. The backend includes authentication, Gmail SMTP email verification, workspace roles and invitations, ownership transfer, projects with explicit membership and archive controls, work items and collaboration, notifications, background email delivery, overdue checks, and authorized SignalR updates. The frontend has not been implemented.

## Run locally with Docker

Install Docker with the Compose plugin, then run these commands from the repository root:

```sh
cd backend
test -f .env || cp .env.example .env
# Set POSTGRES_PASSWORD and JWT_SIGNING_KEY in .env for local development.
# Generate JWT_SIGNING_KEY with: openssl rand -base64 48
docker compose up --build -d
docker compose ps
```

Open or check:

- API liveness: <http://localhost:8080/health/live>
- PostgreSQL readiness: <http://localhost:8080/health/ready>
- OpenAPI JSON: <http://localhost:8080/openapi/v1.json>

Both health endpoints should return `Healthy`. The OpenAPI document includes the versioned authentication, workspace, invitation, project, work-item, collaboration, and notification endpoints. The API and PostgreSQL ports are bound to localhost; edit `API_PORT` and `POSTGRES_PORT` in `backend/.env` if needed.

Stop the containers from `backend/` with `docker compose down`. PostgreSQL data stays in a Docker volume; `docker compose down -v` also deletes that local data. If PostgreSQL has already initialized its volume, keep the existing password in `.env` unless you also change it in PostgreSQL.

## Run the API with the .NET SDK

Install the .NET 10 SDK. Start only PostgreSQL with `docker compose up -d db` from `backend/`, set `ConnectionStrings__FlowForge` for the API process, then run:

```sh
cd backend
dotnet run --project src/FlowForge.Api --urls http://localhost:8080
```

See [backend/README.md](backend/README.md) for the connection string, build and test commands, and project structure. The product design is in [docs/](docs/feature_specification.md).
