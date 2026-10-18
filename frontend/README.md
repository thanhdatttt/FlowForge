# FlowForge frontend

Frontend foundation for the stack defined in [the architecture document](../docs/architecture.md): React, TypeScript, TanStack Query, Zustand, and shadcn/ui. Vite provides development and production builds; Tailwind CSS provides shadcn/ui styling. The SignalR client package is installed for future live updates.

This scaffold mounts an empty app. It includes the TanStack Query provider, strict TypeScript configuration, `@/` imports, shadcn/ui configuration and base styles, and lint/build commands. Zustand stores, API clients, authentication, SignalR connections, routes, pages, and product components are not implemented.

## Run locally

Use Node.js 24 LTS and npm. From this directory:

```sh
npm ci
npm run dev
```

Open <http://localhost:5173>. A blank page is expected at this stage. The scaffold can start without the backend because it makes no network requests.

## Backend development proxy

Start the API separately using [the backend instructions](../backend/README.md). Vite forwards these paths to `http://localhost:8080` by default:

| Browser path | Purpose |
| --- | --- |
| `/api` | Versioned REST endpoints under `/api/v1` |
| `/hubs` | SignalR negotiation and WebSocket traffic at `/hubs/updates` |
| `/health` | API health endpoints |

Future browser code should use relative URLs such as `/api/v1/workspaces`. This keeps browser requests on the frontend origin; the backend currently has no CORS policy.

To use another API port, copy `.env.example` to `.env.local`, update `API_URL`, and restart Vite. This value configures the development proxy and is not exposed as a browser variable. Keep backend database, JWT signing, and SMTP secrets in the backend configuration. Vite variables prefixed with `VITE_` are public.

For production, route `/api` and `/hubs` to the API through the HTTPS reverse proxy, including WebSocket upgrades. The Vite development proxy is not part of the production build. `npm run preview` serves the built frontend for local inspection; it is not the production server or the backend proxy.

## Source layout

```text
src/
  app/providers.tsx  TanStack Query provider
  lib/utils.ts       shadcn/ui class-name utility
  App.tsx            Empty application root
  index.css          Tailwind and shadcn/ui base styles
  main.tsx           React entry point
```

`components.json` configures future shadcn/ui additions. Components can be added when a feature needs them; none are included in this setup. Use TanStack Query for backend data and Zustand for client state when implementing features.

See [the backend README](../backend/README.md) for Bearer authentication, rotating refresh tokens, API errors, and SignalR subscription/reconnect contracts. No token storage or refresh behavior is selected by this scaffold.

## Checks

```sh
npm run lint
npm run typecheck
npm run build
```

Commit `package-lock.json` and use `npm ci` for reproducible installs. The production output is `dist/`.

The shadcn CLI and build-time CSS dependency is pinned to `4.22.0`. The import `shadcn/tailwind.css` in `src/index.css` requires this package's CSS export; `shadcn@1.0.0` does not provide it and causes both development CSS loading and production builds to fail. The current npm audit report can suggest downgrading to `1.0.0` with `npm audit fix --force`; that downgrade is incompatible with this setup. Review dependency changes before accepting an audit fix.

The setup follows the official [Vite guide](https://vite.dev/guide/) and [shadcn/ui Vite installation](https://ui.shadcn.com/docs/installation/vite).
