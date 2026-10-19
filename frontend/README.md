# FlowForge frontend

Frontend for the stack defined in [the architecture document](../docs/architecture.md): React, TypeScript, TanStack Query, Zustand, and shadcn/ui. Vite provides development and production builds; Tailwind CSS provides shadcn/ui styling. The SignalR client package is installed for future live updates.

The first product slice implements responsive sign-in, registration, account confirmation, email verification, rotating access/refresh tokens, and sign-out. Shared buttons and fields use the existing shadcn theme; Base UI handles accessible dialog behavior. Workspace/project screens and SignalR connections are not implemented yet. Design decisions and boundaries are in [the frontend design document](../docs/frontend-design.md).

## Run locally

Use Node.js 24 LTS and npm. From this directory:

```sh
npm ci
npm run dev
```

Open <http://localhost:5173>. `/` and `/login` show sign-in; `/register` shows account creation. The UI starts without the backend and reports an actionable error when a request cannot connect. Start the backend for real authentication. Production hosts must serve `index.html` for frontend routes.

### Reinstalling dependencies on Linux FUSE/NTFS drives

Stop this project's dev/preview server with `Ctrl+C` before running `npm ci`. That command removes and rebuilds `node_modules`; Vite loads native Rolldown/Tailwind binaries from that directory. On FUSE/NTFS, an open binary can remain as a `.fuse_hidden...` file and prevent npm from removing its directory, producing `ENOTEMPTY`. A failed install can also leave `vite` missing when running `npm run dev`.

If this happens, stop the FlowForge processes holding those files, then rerun `npm ci` followed by `npm run dev`. For the reported Rolldown directory, this read-only command identifies the process:

```sh
lsof +D node_modules/@rolldown/binding-linux-x64-gnu
```

Stop only the identified FlowForge process. Keep `package-lock.json`; this error does not require regenerating the lockfile or changing dependencies. If the next start reports port 5173 is in use, stop the previous FlowForge dev server before starting another instance.

## Backend development proxy

Start the API separately using [the backend instructions](../backend/README.md). Vite forwards these paths to `http://localhost:8080` by default:

| Browser path | Purpose |
| --- | --- |
| `/api` | Versioned REST endpoints under `/api/v1` |
| `/hubs` | SignalR negotiation and WebSocket traffic at `/hubs/updates` |
| `/health` | API health endpoints |

Browser code uses relative URLs such as `/api/v1/auth/login`. This keeps browser requests on the frontend origin; the backend currently has no CORS policy.

To use another API port, copy `.env.example` to `.env.local`, update `API_URL`, and restart Vite. This value configures the development proxy and is not exposed as a browser variable. Keep backend database, JWT signing, and SMTP secrets in the backend configuration. Vite variables prefixed with `VITE_` are public.

For production, route `/api` and `/hubs` to the API through the HTTPS reverse proxy, including WebSocket upgrades. The Vite development proxy is not part of the production build. `npm run preview` serves the built frontend for local inspection; it is not the production server or the backend proxy.

## Source layout

```text
src/
  app/providers.tsx  TanStack Query provider
  components/       Shared brand and accessible UI controls
  features/auth/    Forms, auth contracts, session store, verification, illustration
  lib/api.ts        HTTP errors and authenticated token refresh
  lib/utils.ts       shadcn/ui class-name utility
  App.tsx            Auth application root
  index.css          Tailwind, theme tokens, responsive auth styles
  main.tsx           React entry point
```

`components.json` configures future shadcn/ui additions. TanStack Query owns server state and mutations, Zustand holds session tokens, and individual components own form and display state.

See [the backend README](../backend/README.md) for Bearer authentication, rotating refresh tokens, API errors, and SignalR subscription/reconnect contracts. Tokens live only in memory: reloading requires sign-in. Concurrent refreshes share a request; invalid refresh credentials clear the session. No credentials are saved to browser storage. Sign-out revokes the session before clearing client state. A persistent session would require a separately designed backend cookie contract.

Verification sends a code only on request, preserves leading zeroes, and enforces a resend cooldown. It requires working backend SMTP configuration. Password recovery and OAuth have no backend endpoints, so the UI offers sign-in guidance rather than simulated recovery or social sign-in. After authentication, the account screen explains that workspace and project screens are next.

## Checks

```sh
npm test
npm run lint
npm run typecheck
npm run build
```

Tests require Node.js 24 and use its built-in TypeScript support. They exercise validation, actual API request contracts, error mapping, authorization headers, concurrent refresh/expiry, late refresh after sign-out, verification, and logout with mocked HTTP responses. They do not require a backend or SMTP service. Live account/verification checks require the configured backend.

Commit `package-lock.json` and use `npm ci` for reproducible installs. The production output is `dist/`.

## Continuous integration

The `frontend` job in [Frontend CI](../.github/workflows/frontend-ci.yml) runs on pushes to `main` and pull requests targeting `main` only when files under `frontend/` change. [Backend CI](../.github/workflows/backend-ci.yml) runs only for changes under `backend/`. Changes in both directories trigger both workflows; changes only outside those directories (including root documentation or workflow files) trigger neither. GitHub evaluates the changed files for the push or the whole pull request, not just its latest commit.

Frontend CI reads Node.js 24 from `.nvmrc`, caches npm downloads using `frontend/package-lock.json`, and runs `npm ci`, lint, typecheck, tests, and the production build. A failed step fails the job. Tests use mocked HTTP responses and require no backend, database, SMTP credentials, or Vercel secrets.

This covers the frontend's Phase 1 CI requirement. A successful local run does not confirm a GitHub-hosted run; the workflow must be committed and pushed to trigger GitHub Actions. End-to-end sign-in and verification against a real backend/SMTP service remain separate acceptance checks. This workflow validates the client; it does not deploy it.

The shadcn CLI and build-time CSS dependency is pinned to `4.22.0`. The import `shadcn/tailwind.css` in `src/index.css` requires this package's CSS export; `shadcn@1.0.0` does not provide it and causes both development CSS loading and production builds to fail. The current npm audit report can suggest downgrading to `1.0.0` with `npm audit fix --force`; that downgrade is incompatible with this setup. Review dependency changes before accepting an audit fix.

The setup follows the official [Vite guide](https://vite.dev/guide/) and [shadcn/ui Vite installation](https://ui.shadcn.com/docs/installation/vite).
