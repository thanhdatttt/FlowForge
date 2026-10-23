# FlowForge

A modular project management platform. The backend includes authentication, Gmail SMTP email verification, workspace roles and invitations, ownership transfer, projects with explicit membership and archive controls, work items and collaboration, notifications, background email delivery, overdue checks, authorized SignalR updates, and configurable workflow automation with durable execution history. The frontend includes responsive authentication, Phase 2 workspace and project management, and Phase 3 tasks/issues, labels, subtasks, dependencies, comments/mentions, private attachments, time entries and activity, plus Phase 4 in-app notifications and authorized SignalR live updates with reconnect recovery. See [Phase 2 frontend notes](docs/phase2_frontend.md) and [Phase 3 scope and verification](docs/phase3_frontend.md), and [Phase 4 notifications/realtime](docs/phase4_frontend.md).

## Frontend setup

The `frontend/` folder uses React, TypeScript, TanStack Query, Zustand, and shadcn/ui with Vite. With Node.js 24 LTS installed, run:

```sh
cd frontend
npm ci
npm run dev
```

Open <http://localhost:5173> for sign-in, or `/register` to create an account. Start the backend for real authentication. The development server proxies `/api`, `/hubs`, and `/health` to the backend at `http://localhost:8080`. See [frontend/README.md](frontend/README.md) for configuration and checks, and [the auth design notes](docs/frontend-design.md) for design and session boundaries.

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

Both health endpoints should return `Healthy`. The OpenAPI document includes the versioned authentication, workspace, invitation, project, work-item, collaboration, notification, and workflow endpoints. The API and PostgreSQL ports are bound to localhost; edit `API_PORT` and `POSTGRES_PORT` in `backend/.env` if needed.

Stop the containers from `backend/` with `docker compose down`. PostgreSQL data stays in a Docker volume; `docker compose down -v` also deletes that local data. If PostgreSQL has already initialized its volume, keep the existing password in `.env` unless you also change it in PostgreSQL.

## Run the API with the .NET SDK

Install the .NET 10 SDK. Start only PostgreSQL with `docker compose up -d db` from `backend/`, set `ConnectionStrings__FlowForge` for the API process, then run:

```sh
cd backend
dotnet run --project src/FlowForge.Api --urls http://localhost:8080
```

See [backend/README.md](backend/README.md) for the connection string, build and test commands, and project structure. The product design is in [docs/](docs/feature_specification.md).

Phase 5 backend behavior and API contracts are documented in [docs/workflow_engine.md](docs/workflow_engine.md). Phase 6 basic dashboard backend APIs and metric definitions are documented in [docs/dashboard.md](docs/dashboard.md). AI task proposal/acceptance and summary APIs include a configurable Ollama Cloud adapter; set your own `OLLAMA_API_KEY` and `OLLAMA_MODEL` as described in [docs/ai_backend.md](docs/ai_backend.md). Both default to empty; no model is chosen automatically. Milestones/documents, advanced reporting metrics and production delivery remain deferred.
