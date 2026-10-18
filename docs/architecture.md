# FlowForge technology and architecture

FlowForge is a workflow-based project management platform. The initial release uses a **modular monolith**: one deployable ASP.NET Core application with clear business-module boundaries. This keeps transactions and operations simple while allowing modules to evolve independently.

## Technology

| Area | Choice |
| --- | --- |
| Frontend | React, TypeScript, TanStack Query, Zustand, shadcn/ui |
| API | ASP.NET Core 10 Web API, versioned REST endpoints, OpenAPI |
| Persistence | PostgreSQL with Entity Framework Core migrations |
| Authentication | JWT access tokens, rotating refresh tokens, server-side role checks |
| Background work | Hangfire and/or `BackgroundService`, with one owner per job type |
| Live updates | SignalR; Redis backplane only for multiple API instances |
| Cache/coordination | Redis only where a measured need justifies it |
| Delivery | Docker Compose for development; Nginx and GitHub Actions for deployment |

## Structure

```text
React client
    |
ASP.NET Core API + SignalR
    |
Identity | Workspace | Project | WorkItems | Comments | Attachments
Workflow | Notifications | Activity | Reporting | optional AI
    |
PostgreSQL + background jobs + optional Redis
```

Each module separates API, application, domain, and infrastructure concerns. API handlers validate requests and authorize access; application services coordinate use cases and transactions; domain code holds business rules; infrastructure owns EF Core, files, messaging, and external providers. Modules communicate through explicit contracts and domain events, without circular dependencies.

## Key flows and boundaries

- **Request:** Authenticate, authorize workspace/project access, validate input, perform the use case, and commit related data and activity records together. Return DTOs and consistent problem-details errors; do not expose EF entities.
- **Automation:** Persist a domain event and outbox entry with the business change. Notification outbox delivery and workflow scheduling have independent durable checkpoints. A background worker schedules matching immutable workflow versions, evaluates validated conditions, and runs ordered actions with idempotency, bounded retries, execution history, and loop protection.
- **Collaboration:** Publish compact SignalR events after commit. Clients refetch current state after reconnecting. Check authorization before group joins, file downloads, notifications, workflow actions, and AI context reads.
- **Data:** PostgreSQL is the durable source of truth; Redis is not. Store attachment metadata in PostgreSQL and file bytes in private storage. Derive dashboard metrics from work-item state and history.

The optional AI assistant must work as an add-on: validate its output, show task proposals before saving, and use only context the requester may access. See [database.md](database.md) for the proposed schema and [feature_specification.md](feature_specification.md) for functional rules and delivery phases.
