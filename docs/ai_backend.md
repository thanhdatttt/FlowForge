# Phase 6: provider-independent AI backend

The backend supports AI task proposals and project summaries through `FlowForge.Application.Ai.IAiProvider`. The selected transport is now **Ollama Cloud**, using the built-in `OllamaCloudAiProvider` and the native HTTPS chat API. **No API key or model is supplied or selected automatically.** With either setting missing, the adapter is unconfigured and makes no network calls; generation returns `503`. Existing project management, dashboards, workflows and notifications continue to work without AI. No vendor SDK or new dependency is installed.

Milestones/documents, advanced metrics and AI/frontend UI remain outside this slice. The provider interface remains replaceable.

## Configure Ollama Cloud yourself

For Docker Compose, fill these values in `backend/.env` (the tracked `.env.example` intentionally leaves both blank):

```dotenv
OLLAMA_API_KEY=your-own-key
OLLAMA_MODEL=your-chosen-cloud-api-model-name
```

Compose maps them to `Ollama__ApiKey` and `Ollama__Model` inside the API container. Rebuild/recreate the API after updating configuration:

```sh
cd backend
docker compose up --build -d api
```

For a host `dotnet` run, supply the configuration directly to the process; `.NET` does not automatically load `backend/.env`:

```sh
export Ollama__ApiKey='your-own-key'
export Ollama__Model='your-chosen-cloud-api-model-name'
dotnet run --project src/FlowForge.Api --urls http://localhost:8080
```

Keep the existing database/JWT configuration required by the backend README. Create your key in [Ollama key settings](https://ollama.com/settings/keys), and choose an exact model name from `https://ollama.com/api/tags`. Model names used directly by the Cloud API may differ from CLI names. See the [official Cloud documentation](https://docs.ollama.com/cloud). No local Ollama installation is needed. This update does not read or modify your existing `.env`, store a real key, select a model, or call a real AI service during verification.

The adapter posts to the fixed `https://ollama.com/api/chat` endpoint with Bearer authentication, `stream: false`, a system instruction and the authorized JSON context as a separate user message. It reads the completed assistant `message.content`; it sends no tools and executes no tool calls. HTTP redirects are disabled so credentials cannot be forwarded to another host. There is no configurable arbitrary endpoint or fallback model.

Ollama Cloud [currently does not support structured outputs](https://docs.ollama.com/capabilities/structured-outputs). The adapter therefore does not send `format`/JSON-schema options. Its system instructions request the exact JSON contracts below, and the existing server-side validation rejects malformed content with `502`. It does not repair JSON, remove Markdown fences, silently choose another model, or retry generations. Model compliance/quality must be checked with your own chosen model.

Provider envelopes are bounded to 1 MiB, even without a Content-Length header. Only completed assistant content is accepted; completion text retains the existing 131072-character limit. `401`/`403` (credentials), `404` (unavailable model) and provider `5xx` map to unavailable `503`; `408`/`504` map to timeout `504`; `429` preserves a bounded Retry-After value; other unsuccessful responses/malformed envelopes map to `502`. Error bodies and keys are not exposed/logged. Request cancellation reaches HTTP; the existing `Ai:TimeoutSeconds` limit owns request timeout.

## Replace the adapter if needed

Implement [IAiProvider](../backend/src/FlowForge.Application/Ai/IAiProvider.cs) in your chosen integration and register it **after** `AddInfrastructure` in `Program.cs`:

```csharp
builder.Services.AddScoped<IAiProvider, YourAiProvider>();
```

The interface is:

```csharp
bool IsConfigured { get; }
Task<string> GenerateTasksAsync(AiTaskGenerationContext context, CancellationToken cancellationToken);
Task<string> SummarizeProjectAsync(AiProjectSummaryContext context, CancellationToken cancellationToken);
```

Return `IsConfigured == false` when the adapter lacks configuration/credentials. Honor the supplied cancellation token, including transport cancellation. Translate provider-side throttling to `AiProviderException(AiProviderFailure.RateLimited, retryAfterSeconds)`, credential/service unavailability to `Unavailable`, timeouts to `Timeout`, and invalid completions to `InvalidResponse`. Unexpected exceptions are sanitized by the API. Do not put prompts, completions, credentials or provider response bodies in logs/exception messages.

The adapter receives immutable request records with only scoped context. It must treat all feature descriptions, project/item text and model completions as untrusted data: do not execute instructions, scripts, commands or tools embedded in them, retrieve additional project data, or let the model persist tasks. These methods have no persistence or execution capabilities. Return **only** the JSON envelope below, without Markdown fences. The built-in Ollama adapter follows this contract; another custom implementation can replace it through DI.

Limits use `Ai:TimeoutSeconds` (1–120, default 30), `Ai:PreviewLifetimeMinutes` (1–60, default 15) and `Ai:MaxContextItems` (1–100, default 50). Host environment variables use double underscores, e.g. `Ai__TimeoutSeconds=30`; Compose exposes the corresponding `AI_TIMEOUT_SECONDS`, `AI_PREVIEW_LIFETIME_MINUTES`, `AI_MAX_CONTEXT_ITEMS` variables. Invalid limits fail startup, even without a provider. These limits do not select a model. Provider output is limited to 131072 characters and JSON depth 16; unknown or duplicate JSON properties are rejected.

## API and permissions

Base: `/api/v1/workspaces/{workspaceId}/projects/{projectId}/ai`. All routes require Bearer authentication and existing project access.

| Method | Suffix | Behavior |
| --- | --- | --- |
| POST | `/tasks/propose` | Body `{ "featureDescription": "..." }`; return validated proposals, an opaque `previewToken`, `expiresAt` and `isAiGenerated: true`. Does not persist tasks, activity or events. |
| POST | `/tasks/accept` | Body `{ "previewToken": "...", "accepted": true, "tasks": [...] }`; explicitly accept an edited selection. Return `batchId` and created proposal-ID/item-ID/readable-key mappings. No provider call is made. |
| POST | `/summary` | No input fields required. Return server-calculated facts separately from an AI narrative, timestamp, UTC date, status, `isAiGenerated`, `contextItemCount` and `contextTruncated`. Does not write business data. |

Propose/accept require current resource-write permission: Owners/Admins, assigned Managers/Developers. Viewers receive `403`. Archived projects reject task generation/acceptance with `409`; summary reads remain available. Summary supports all project readers, including assigned Viewers. Anonymous callers receive `401`; inaccessible/mismatched workspace/project IDs and removed/inactive users receive `404` before any provider call. Rights are checked again after provider completion before returning private output. Acceptance reloads project state and permissions under the workspace lock.

Propose/summary share a local fixed-window rate limit of **10 requests per minute per client IP**, with no queued requests. Provider throttling is separate. Acceptance uses normal project write controls and does not consume an AI generation request.

## Task proposals, edits and confirmation

`AiTaskGenerationContext` contains workspace/project IDs, project name and a trimmed feature description (1–10000 characters). It contains no other projects, comments, attachments, user credentials or session tokens. The adapter returns:

```json
{
  "tasks": [
    { "id": "foundation", "title": "Create the foundation", "description": "Description", "priority": "High", "dependsOn": [] },
    { "id": "feature", "title": "Implement the feature", "description": null, "priority": "Medium", "dependsOn": ["foundation"] }
  ]
}
```

Validation applies both to provider output and the accepted client-edited task list:

- 1–20 tasks, each with a unique, case-sensitive local ID: 1–40 characters, starting with a letter, followed by letters/digits/`_`/`-`.
- Title: trimmed 1–200 characters. Optional description: at most 20000 characters. Priority: exactly `Low`, `Medium`, `High` or `Critical`.
- `dependsOn` is a required array (empty is valid), containing at most 19 unique local IDs. Reject null/missing dependencies, self-dependencies, missing references and cycles.
- On acceptance, IDs must belong to that preview. Users can edit title/description/priority/dependencies and select a subset, but dependencies must stay inside the final selected set. Client-supplied assignees, statuses, external resource IDs and executable actions are not part of this contract.

The API preview is the backend equivalent of a preview screen; a later client displays it and lets the user edit/select tasks. Sending `accepted: false`, omitting acceptance, or sending an invalid list returns `400 ValidationProblem` and creates nothing. The default application does not simulate AI content. Without both an Ollama key and model configured (or a configured replacement adapter), proposing tasks returns `503`.

Preview tokens use ASP.NET Core Data Protection with a dedicated purpose. Tokens contain only batch/user/workspace/project IDs, local proposal IDs and expiration. They are bound to the requesting user and exact project, valid for 15 minutes by default. Missing/malformed/tampered tokens return `400`; a different user/project returns `404`; expired tokens return `410`. Persist Data Protection keys (the existing Compose key volume) to retain previews across restarts. Sharing keys/application name is necessary for multiple instances.

Acceptance creates unassigned `Task` items in the project's active `Todo` status, with the accepting user as reporter. It trims titles/descriptions, allocates normal project item numbers, links dependencies and persists normal work-item/dependency activity plus `WorkItemCreated` domain events/outbox entries. Existing workflows may therefore react after commit. No AI action bypasses normal project write rules.

All effects and an `ai.tasks.accepted` receipt commit atomically under the existing workspace row lock. A repeated confirmation of the same token and normalized selected content returns the same IDs without duplicate tasks/events. Reordering tasks/dependencies does not change its identity. Reusing the token with different selected content returns `409`. An expired preview stays expired, including on retries. No proposal table or migration is added: the receipt uses existing append-only activity records and stores a content hash and created-ID mappings, not the preview token or original feature description. Retain receipts for at least the preview lifetime; ordinary application operations do not delete them.

## Project summaries and context

Authorization and context reads share a PostgreSQL repeatable-read snapshot, committed before awaiting the provider. No workspace write lock or database transaction is held across provider work. Summary context is rebuilt per request; no cross-project cache or retrieval index is used.

`AiProjectSummaryContext` contains project identity/name, snapshot time/UTC date, server-calculated `AiProjectFacts`, and at most `MaxContextItems` non-archived items ordered by updated time descending then ID. Each item includes readable key, title, type, status, completed flag, priority, due date, dependency-blocked flag and a description truncated to 2000 characters. It excludes comments, attachments, emails, session secrets and all other projects/workspaces. `contextTruncated` explicitly identifies incomplete item sampling; facts always cover all included items, not just the sample.

Facts follow [dashboard definitions](dashboard.md): total/completed item counts, completion percentage, overdue count. `blockedItems` counts unfinished items in status `Blocked` or with an unfinished prerequisite (same dependency semantics as work-item reads). Numeric facts come only from PostgreSQL/application code; the model cannot supply or override them.

Expected provider JSON:

```json
{
  "overview": "Interpretation of project progress using the supplied facts and sample.",
  "completedWork": "Completed work visible in the supplied sample.",
  "blockers": "Observed blockers; state when details are missing.",
  "overdueWork": "Observed overdue work; do not invent unseen items.",
  "risks": ["Potential risk, identified as an interpretation."]
}
```

All four narrative text fields are required/nonblank, max 4000 characters each. `risks` is a required array with 0–10 nonblank strings, max 1000 characters each. The response labels this narrative as AI-generated and returns it separately from factual metrics. Treat narrative text as untrusted plain text in future clients.

An empty project returns `200`, `status: "InsufficientData"`, zero facts, `narrative: null`, `isAiGenerated: false`, without calling a provider—even if AI is unconfigured. A nonempty project with no configured adapter returns `503`. A truncated context does not mean facts are incomplete; adapters should qualify interpretations when sample detail is insufficient.

## Failures and verification

Input validation returns field-specific `400` errors. Provider/configuration failures return safe problem details: unavailable/unconfigured `503`, provider throttle `429` (bounded `Retry-After`, 1–3600 seconds), timeout `504`, malformed/oversized/invalid output or unexpected provider failure `502`. Client cancellation is propagated. There are no automatic provider retries. A timeout bounds API waiting even if an adapter ignores cancellation; adapters remain responsible for stopping their own network work when cancelled.

Feature tests inject a fake provider only in the test host. Ollama adapter tests use an in-memory HTTP handler to verify endpoint/auth/model/nonstreaming payloads, task/summary prompts, envelope/content limits, status classification, throttling and cancellation; a configured host test exercises the actual DI adapter plus API output validation without network access. Existing tests cover preview/confirmation transactions, permissions, scoped facts, failures and no-model behavior. Full regressions include dashboard/workflow/notification tests. Real Cloud connectivity, credential validity, model access and output quality are intentionally unverified until you supply configuration. See [test results](../backend/TEST_RESULTS.md).

From `backend/`, with an isolated `FLOWFORGE_TEST_CONNECTION` allowed to create/drop test databases:

```sh
dotnet test FlowForge.sln --configuration Release --no-restore -m:1 /p:UseSharedCompilation=false --filter 'FullyQualifiedName~PhaseSixAi'
dotnet test FlowForge.sln --configuration Release --no-restore -m:1 /p:UseSharedCompilation=false
```
