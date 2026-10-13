# ForgeFlow — Project Proposal

## 1. Overview
**ForgeFlow** is a workflow-based project management platform inspired by Jira, Linear, and Trello. It helps teams manage projects, tasks, issues, and collaboration while automating repetitive work through configurable workflows.

## 2. Problem & Pain Points
- Project work is scattered across tasks, comments, and status updates.
- Teams spend time on repetitive actions such as assigning work, sending notifications, and updating project status.
- Limited visibility into blockers, overdue work, and project progress makes coordination harder.
- Fixed workflows often fail to match different teams’ processes.

## 3. Core Features
- **Workspace & Access Control:** Workspaces, members, roles, JWT authentication, refresh tokens, and role-based permissions.
- **Project & Task Management:** Projects, tasks, issues, milestones, subtasks, dependencies, labels, comments, attachments, and time tracking.
- **Configurable Workflow Engine:** Define triggers, conditions, and actions; execute rules asynchronously with retries, idempotency, and execution logs.
- **Real-time Collaboration:** Live task updates and notifications using SignalR.
- **Activity & Audit Logs:** Track important changes with actor, timestamp, and before/after values.
- **Dashboard & Analytics:** Project progress, overdue tasks, workload, cycle time, and task distribution.
- **AI Assistant (supporting feature):** Generate task breakdowns and summarize project progress, blockers, and risks.
- **Testing & Delivery:** Unit/integration tests, Docker-based development, CI/CD, monitoring, and cloud deployment.

## 4. Technology Stack
- **Backend:** ASP.NET Core 10 Web API, Entity Framework Core
- **Database & Cache:** PostgreSQL, Redis
- **Background Processing:** BackgroundService / Hangfire
- **Real-time:** SignalR
- **Authentication:** JWT, refresh tokens, RBAC, optional OAuth
- **Frontend:** React, TypeScript, TanStack Query, Zustand, shadcn/ui
- **Infrastructure:** Docker, Docker Compose, Nginx, GitHub Actions, cloud hosting

## 5. Architecture
Start with a **Modular Monolith** to keep development and deployment manageable for a solo developer. Organize the code around business modules such as Identity, Workspace, Project, Task, Workflow, Notification, Activity, and AI.

```text
React / TypeScript
        |
        v
ASP.NET Core Web API
        |
        +-- Identity & Access
        +-- Workspace / Projects / Tasks
        +-- Workflow Engine
        +-- Notifications / Activity
        +-- AI Assistant
        |
        +------ PostgreSQL
        +------ Redis
        +------ Background Jobs
        +------ SignalR
```

Use clear module boundaries and separate API, application, domain, and infrastructure concerns. Introduce distributed services only if a demonstrated requirement justifies the added operational complexity.

## 6. Suggested Delivery Phases
1. **Foundation:** API, database, authentication, Docker, CI.
2. **Core Product:** Workspaces, projects, tasks, comments, permissions.
3. **Collaboration:** Audit logs, notifications, background jobs, SignalR.
4. **Differentiator:** Workflow rules, execution history, retries, idempotency.
5. **Polish:** AI assistant, analytics, automated tests, monitoring, deployment.

## 7. CV Value
Demonstrates practical experience with domain modeling, authorization, asynchronous processing, event-driven workflows, real-time collaboration, testing, and deployment—not only CRUD operations.
