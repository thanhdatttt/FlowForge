<p align="center"><h1 align="center">FlowForge</h1></p>

**A modular project management platform with configurable workflows, real-time collaboration, and optional AI assistance.**

## Table of contents

- [Table of contents](#table-of-contents)
- [Overview](#overview)
- [Features](#features)
- [Tech stack](#tech-stack)
- [Quickstart](#quickstart)
  - [Prerequisites](#prerequisites)
  - [1. Clone the repository](#1-clone-the-repository)
  - [2. Configure and start the backend](#2-configure-and-start-the-backend)
  - [3. Start the frontend](#3-start-the-frontend)
  - [Local endpoints](#local-endpoints)
- [Project setup](#project-setup)
  - [Run the backend with the .NET SDK](#run-the-backend-with-the-net-sdk)
  - [Use a different API port](#use-a-different-api-port)
- [Configuration](#configuration)
- [Project structure](#project-structure)
- [Development and testing](#development-and-testing)
  - [Frontend](#frontend)
  - [Backend](#backend)
  - [Continuous integration](#continuous-integration)
- [Deployment notes](#deployment-notes)
- [Documentation](#documentation)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [Contributors](#contributors)
- [License](#license)

## Overview

FlowForge brings workspaces, projects, tasks, issues, and team activity into one application. Teams can organize work, control access, automate recurring actions, and track project progress.

FlowForge is built as a modular monolith: one backend application with separate API, application, domain, and infrastructure layers. The frontend is organized by product feature, and PostgreSQL stores application data and durable workflow history.

The project currently includes end-to-end workspace and project management, work-item collaboration, notifications, workflow automation, basic dashboards, and an optional Ollama Cloud integration. AI-generated task proposals require review and explicit acceptance before they become project tasks.

The repository provides local development tooling and separate frontend/backend CI workflows. Production delivery remains part of the roadmap.

## Features

| Area | Capabilities |
| --- | --- |
| Authentication | Registration, sign-in, rotating refresh tokens, HttpOnly session cookies, email verification, and password recovery through OTP |
| Workspaces and access | Owner, Admin, Manager, Developer, and Viewer roles; membership management, invitations, and ownership transfer |
| Projects | Project membership, lead assignment, access controls, and archive management |
| Work items | Tasks and issues, labels, subtasks, dependencies, priorities, assignments, and due dates |
| Collaboration | Comments, mentions, private attachments, time entries, and activity history |
| Notifications | In-app notifications, read state, background email delivery, and overdue checks |
| Real-time updates | Authorized SignalR subscriptions and state recovery after reconnecting |
| Workflow automation | Configurable triggers, conditions, and actions; enable/disable controls, versioning, and durable execution history |
| Dashboards | Project progress, overdue work, workload, and recent activity |
| Optional AI | Editable task proposals with explicit acceptance, plus project summaries using authorized project context |

Email delivery requires SMTP configuration. AI generation requires an Ollama Cloud API key and model; core project management works without AI.

## Tech stack

| Layer | Technologies |
| --- | --- |
| Frontend | React 19, TypeScript 6, Vite 8 |
| UI and styling | Tailwind CSS 4, shadcn/ui, Base UI, Lucide icons |
| Client state | TanStack Query for server state; Zustand for session state |
| Backend | C#, .NET 10, ASP.NET Core, versioned REST API, OpenAPI |
| Database | PostgreSQL 17, Entity Framework Core 10, Npgsql |
| Authentication | JWT access tokens, rotating refresh tokens, server-side authorization |
| Background processing | ASP.NET Core hosted services with persisted job and execution state |
| Real-time communication | ASP.NET Core SignalR |
| Integrations | Gmail SMTP, optional Ollama Cloud through a replaceable AI provider interface |
| Testing and quality | xUnit, Node.js test runner, Oxlint, TypeScript checks |
| Development and CI | Docker Compose, npm, GitHub Actions |

## Quickstart

Run the API and PostgreSQL with Docker Compose, then start the frontend locally.

### Prerequisites

- Git
- Docker with the Docker Compose plugin
- Node.js 24 and npm; the frontend version is recorded in [`frontend/.nvmrc`](frontend/.nvmrc)
- OpenSSL to generate a JWT signing key, or another secure key generator

The .NET SDK is only needed when running or developing the backend outside Docker.

### 1. Clone the repository

```sh
git clone https://github.com/thanhdatttt/FlowForge.git
cd FlowForge
```

### 2. Configure and start the backend

```sh
cd backend
test -f .env || cp .env.example .env
openssl rand -base64 48
```

Edit `backend/.env` before starting the containers:

- Set `POSTGRES_PASSWORD` to a local database password.
- Set `JWT_SIGNING_KEY` to the generated value. Keep it stable across restarts.

```sh
docker compose up --build -d
docker compose ps
curl --fail http://localhost:8080/health/ready
```

Compose starts PostgreSQL, applies EF Core migrations when the API starts, and runs the API on port `8080`. The readiness endpoint should return `Healthy`.

### 3. Start the frontend

In a second terminal, from the repository root:

```sh
cd frontend
npm ci
npm run dev
```

Open [FlowForge at localhost:5173](http://localhost:5173). Use `/register` to create an account, then sign in to access the workspace application. Configure SMTP below to use email verification, password recovery, and email delivery.

### Local endpoints

| Service | URL |
| --- | --- |
| Frontend | <http://localhost:5173> |
| API base | <http://localhost:8080/api/v1> |
| API liveness | <http://localhost:8080/health/live> |
| Database readiness | <http://localhost:8080/health/ready> |
| OpenAPI JSON, Development only | <http://localhost:8080/openapi/v1.json> |
| SignalR hub | <http://localhost:8080/hubs/updates> |

The Vite development server proxies `/api`, `/hubs`, and `/health` to the API, including WebSocket traffic.

To stop the backend, run `docker compose down` from `backend/`. Data remains in Docker volumes. Adding `-v` deletes the volumes, including the local database, attachments, and email protection keys.

## Project setup

### Run the backend with the .NET SDK

Install the **.NET 10 SDK** and the EF Core CLI tool if it is not already installed:

```sh
dotnet tool install --global dotnet-ef --version '10.0.*'
```

From `backend/`, prepare `.env` as described in the quickstart and start only PostgreSQL. Stop the Compose API first if it is already using port `8080`:

```sh
docker compose stop api
docker compose up -d db
```

Set the API process environment and apply migrations:

```sh
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__FlowForge='Host=localhost;Port=5432;Database=flowforge;Username=flowforge;Password=your-local-password'
export Jwt__SigningKey='your-generated-signing-key'

dotnet restore FlowForge.sln
dotnet ef database update --project src/FlowForge.Infrastructure --startup-project src/FlowForge.Api
dotnet run --project src/FlowForge.Api --urls http://localhost:8080
```

Replace the password, signing key, and database port with your local values. Unlike Docker Compose, `dotnet run` does not automatically load `backend/.env`. Export optional integration settings using .NET's double-underscore names; see the [backend guide](backend/README.md).

### Use a different API port

Change `API_PORT` in `backend/.env` for Compose, or the `--urls` argument for a host run. From `frontend/`, create a local proxy configuration:

```sh
test -f .env.local || cp .env.example .env.local
```

Set `API_URL` in `frontend/.env.local` to the API address and restart Vite. If `frontend/.env.local` already exists, edit that file directly.

## Configuration

[`backend/.env.example`](backend/.env.example) lists the Compose settings. [`frontend/.env.example`](frontend/.env.example) contains the frontend development proxy setting.

| Setting | Purpose | Default or requirement |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | Local PostgreSQL password | Required; replace the template value |
| `JWT_SIGNING_KEY` | JWT signing and verification-code protection | Required; at least 32 bytes |
| `POSTGRES_PORT` / `API_PORT` | Host ports for PostgreSQL and the API | `5432` / `8080` |
| `PUBLIC_API_URL` | Base URL for generated invitation links | `http://localhost:8080` |
| `SMTP_USERNAME` / `SMTP_APP_PASSWORD` | Gmail address and App Password | Empty; configure for email features |
| `NOTIFICATIONS_ENABLED` | Notification background processing | `true` |
| `WORKFLOWS_ENABLED` | Workflow background processing | `true` |
| `OLLAMA_API_KEY` / `OLLAMA_MODEL` | Optional AI provider credentials and exact model name | Empty; both required for AI generation |
| `API_URL` | Vite development proxy target, in the frontend environment | `http://localhost:8080` |

For Gmail, use an App Password for an account with 2-Step Verification enabled. See [SMTP setup and troubleshooting](backend/README.md#gmail-smtp-email-verification). See [AI configuration](docs/ai_backend.md) for provider settings and request limits.

After changing backend Compose settings, recreate the API container from `backend/`:

```sh
docker compose up -d --build --no-deps --force-recreate api
```

Keep credentials in local environment files or a secret manager. Variables prefixed with `VITE_` are public browser configuration and must not contain secrets. If PostgreSQL has already initialized its volume, changing `POSTGRES_PASSWORD` in `.env` alone does not change the database password.

## Project structure

```text
FlowForge/
├── .github/workflows/              # Frontend and backend CI
├── backend/
│   ├── src/
│   │   ├── FlowForge.Api/          # HTTP endpoints, DTOs, validation, SignalR, workers
│   │   ├── FlowForge.Application/  # Use cases and module contracts
│   │   ├── FlowForge.Domain/       # Domain models and business rules
│   │   └── FlowForge.Infrastructure/
│   │                              # EF Core, migrations, storage, integrations
│   ├── tests/
│   │   ├── FlowForge.Api.Tests/    # Unit, API, and PostgreSQL integration tests
│   │   └── FlowForge.Api.SmokeTests/ # Explicit live API journey runner
│   ├── compose.yaml               # Local API and PostgreSQL services
│   ├── Dockerfile
│   └── FlowForge.sln
├── frontend/
│   ├── src/
│   │   ├── app/                   # Application providers
│   │   ├── components/            # Shared UI and branding
│   │   ├── features/              # Auth, workspaces, work items, workflows,
│   │   │                          # notifications, dashboards, and AI
│   │   └── lib/                   # HTTP client and shared utilities
│   ├── tests/                     # Client tests and opt-in live scenarios
│   └── package.json
├── docs/                          # Architecture, API contracts, and verification notes
└── README.md
```

Backend references point inward: Application depends on Domain, Infrastructure depends on Application, and API composes the application and infrastructure layers. See [architecture](docs/architecture.md) for module boundaries and key flows.

## Development and testing

### Frontend

Run from `frontend/`:

```sh
npm run lint
npm run typecheck
npm test
npm run build
```

The production build is written to `frontend/dist/`. Normal frontend tests use mocked HTTP responses and need no backend, database, or SMTP credentials. Live scenario runners are documented in the feature guides below and require an explicitly disposable environment.

### Backend

Run from `backend/`:

```sh
dotnet restore FlowForge.sln -m:1
dotnet build FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
```

PostgreSQL integration tests are skipped unless `FLOWFORGE_TEST_CONNECTION` is set. To include them, point it at a dedicated test PostgreSQL server whose user can create databases, then rerun the test command:

```sh
export FLOWFORGE_TEST_CONNECTION='Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=your-test-password'
```

The fixtures create and drop isolated test databases. See [backend testing](backend/README.md#build-and-test) and [recorded test results](backend/TEST_RESULTS.md) for details.

### Continuous integration

[Frontend CI](.github/workflows/frontend-ci.yml) runs dependency installation, lint, type checks, tests, and a production build. [Backend CI](.github/workflows/backend-ci.yml) restores, builds, and tests with an isolated PostgreSQL service.

Both workflows target pushes and pull requests to `main`, with path filters for their respective directories. Changes only to root documentation do not trigger either workflow.

## Deployment notes

The checked-in Compose configuration is intended for local development and binds API/database ports to localhost. For production, serve the frontend and API on the same HTTPS origin, proxy `/api` and `/hubs` to the backend with WebSocket support, and fall back to `index.html` for frontend routes.

Persist the database, private attachments, and Data Protection keys. The Vite development proxy is not included in `frontend/dist/`; `npm run preview` only previews the static build. See the [frontend hosting notes](frontend/README.md#backend-development-proxy) for the browser/session requirements.

## Documentation

| Guide | Contents |
| --- | --- |
| [Backend](backend/README.md) | API setup, authentication, permissions, integrations, and testing |
| [Frontend](frontend/README.md) | Client setup, proxy configuration, session behavior, and checks |
| [Architecture](docs/architecture.md) | Layers, module boundaries, and key flows |
| [Feature specification](docs/feature_specification.md) | Product requirements and delivery phases |
| [Database design](docs/database.md) | Proposed schema and data relationships |
| [Frontend design](docs/frontend-design.md) | Interface design and session boundaries |
| [Workspaces and projects](docs/phase2_frontend.md) | Membership, invitations, ownership, and project UI |
| [Work items and collaboration](docs/phase3_frontend.md) | Tasks, issues, comments, attachments, and activity |
| [Notifications and real-time updates](docs/phase4_frontend.md) | Notification UI, subscriptions, and reconnect behavior |
| [Workflow engine](docs/workflow_engine.md) / [Workflow UI](docs/phase5_frontend.md) | Automation contracts, configuration, and execution history |
| [Dashboards](docs/dashboard.md) | Metrics and dashboard API definitions |
| [AI backend](docs/ai_backend.md) / [Dashboard and AI UI](docs/phase6_frontend.md) | Provider configuration, task acceptance, summaries, and verification |

Design documents include planned work; consult the implementation and feature verification notes for current behavior.

## Roadmap

The following areas remain deferred:

- Milestones and project documents
- Advanced reporting and analytics
- Production deployment and delivery tooling

See the [feature specification](docs/feature_specification.md) for the broader product scope.

## Contributing

Contributions can include bug fixes, tests, documentation, and focused feature improvements.

1. Open an [issue](https://github.com/thanhdatttt/FlowForge/issues) to describe a bug or discuss a substantial change.
2. Create a branch from `main` and keep the change focused.
3. Follow the existing feature organization and backend dependency boundaries.
4. Run the relevant frontend or backend checks and update documentation when behavior or setup changes.
5. Submit a pull request with the problem, the change, and validation results. Include screenshots for visible UI changes.

Use `npm ci` with the committed lockfile, include EF Core migrations for database changes, and keep local credentials out of commits.

## Contributors

FlowForge is maintained by [@thanhdatttt](https://github.com/thanhdatttt). See the [contributor history](https://github.com/thanhdatttt/FlowForge/graphs/contributors) for everyone who has contributed to the project.

## License

This project is developed for educational purposes only.

Copyright © 2026.

This software may not be copied, modified, or distributed for commercial purposes without permission from the authors.
