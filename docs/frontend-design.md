# FlowForge frontend design

## Auth slice

Audience: project teams coordinating tasks and automating repetitive work. The screen's job is to help an existing member sign in or a new member create an account. Product scope and API behavior follow `feature_specification.md`, `architecture.md`, and the implemented Identity endpoint/DTO/validator sources.

### Direction and tokens

The selected direction is a quiet, practical workspace with an expressive project-board illustration. Palette: paper `#ffffff`, cool canvas `#f5f6fb`, graphite `#242736`, secondary ink `#697081`, indigo `#5146d6`, and hairline `#e5e7ef`. Use the corresponding CSS theme properties for shared controls. Status green and error red communicate outcomes, not decorative emphasis.

Keep the existing locally bundled Geist Variable: display at weight 550–600 with tight tracking, body at 400–500, and the system monospace stack for task keys and workflow metadata. Display typography is restrained to the brand and headings. Shared radii are 7px for controls and 10–13px for panels; borders and shadows stay subtle.

### Layout exploration and decision

Considered a centered form card and a split form/product view. The centered card is simple but says little about the workflow product. Select the split layout, with a slightly larger cool canvas on the left and a white form surface on the right:

```text
Brand / product cue          Account switch
                            
Good work. Great flow.       Account emblem
Product explanation         Heading / description
                            Fields / help
Example project board       Primary action
       |                    Product benefits
Completion -> notification  Account switch
Footer                      Security note
```

Signature: a slightly rotated project board connects to an automation card that demonstrates the actual status-change-to-notification concept. This is the one expressive device. Keep the form flat, aligned, and calm. Review removed generic social proof, invented customer logos, and unsupported OAuth controls. The board is a labeled illustration, not a simulated interactive workspace.

Below 761px hide the illustration and move the brand into the form header. Keep 16px input text on mobile, comfortable control heights, natural document scrolling, and no horizontal overflow. Short desktop screens may scroll vertically.

### Components and state

- `components/ui`: reusable button and labeled input, including loading, error, hint, and focus states. Base UI supplies the accessible help dialog, following the existing shadcn/Base UI foundation.
- `features/auth`: forms, contract functions, validation, session state, account/verification states, and product illustration.
- TanStack Query owns account reads and auth mutations. Zustand holds only in-memory session tokens. Form values, password visibility, mode, and verification cooldown stay local.
- Sign-in and registration use `/login` and `/register` browser paths with browser back/forward support. Hosts must serve `index.html` for these client paths. The root path also shows sign-in.
- All API calls use implemented relative `/api/v1/auth` endpoints. Authenticated requests refresh near expiry or once after a 401, sharing concurrent token refreshes. Invalid refresh credentials clear the session. Logout clears the account cache only after server revocation succeeds.
- Tokens do not enter localStorage, sessionStorage, URLs, or logs. Reloading requires sign-in. Persistent sessions require a separately designed backend cookie/session contract.
- Verification requests are explicit, leading zeroes are preserved, resends have a 60-second UI cooldown, and server validation remains authoritative. Already verified accounts skip verification. Code lifetime is configurable on the server, so the client makes no fixed expiry promise.
- Password recovery uses the implemented OTP/reset endpoints. OAuth remains unavailable.
- Signed-in users enter the workspace application; account verification is accessible from its sidebar. See the Phase 2 notes below.

### Verification

Run `npm test`, `npm run lint`, `npm run typecheck`, and `npm run build` in `frontend/` with Node 24. Tests exercise the real auth client with mocked HTTP responses; they do not claim a live database/SMTP integration. Browser checks should cover desktop/mobile layouts, validation and focus, show/hide password, help dialog dismissal/focus restoration, mode switching/history, and unavailable-service feedback. Live account creation and OTP delivery additionally require the configured backend and SMTP.

## Phase 2 workspace and project UI

Continue the auth palette and Geist typography with a white navigation sidebar, cool canvas, bordered content surfaces, indigo navigation state and restrained project keys. Keep forms and dialogs on the existing Button/FormField and Base UI foundation. The sidebar becomes a natural-flow header on mobile, with wrapping navigation and 16px inputs.

TanStack Query owns workspace/project/member/invitation data using workspace/project-scoped keys. Forms keep transient state locally. Freeze project fields and their optimistic version together while editing. Display server permission/conflict failures and refresh current data after writes. Invitation secrets stay in memory and fragment-based links, never persistent storage or query caches. The user approved frontend invitation links plus input support for existing backend links. Detailed routes, behavior and verification limits are in [phase2_frontend.md](phase2_frontend.md).

### Account and status presentation

Center the account card within the application content and align account headings, verification copy and the OTP field to its center. Keep identity details left-aligned within their row. Use red text and a pale red surface for sign-out, green for active/accepted states, amber for pending invitations and slate for archived/expired/revoked or disabled controls. Retain explicit labels alongside color. Give headings, descriptions, identity rows and verification hints enough vertical separation and comfortable line height on desktop and mobile.

## Phase 3 work items and collaboration

Extend the existing project with Overview, Work items, Labels and Activity navigation. Continue the workspace theme and native controls/Base UI dialogs. Use readable keys, wrapping item rows, explicit status/priority text and subtle badges. Label colors appear as small swatches with readable names rather than arbitrary text/background combinations. Keep dependency blocking separate from the item's status.

Item details group metadata, related work, comments, files, time and activity into the existing bordered surfaces, with anchor links for long pages. Comments are plain text with member mentions. Archived projects/items and Viewer access explicitly show read-only behavior. Snapshot item/comment forms preserve drafts on conflict and require reopening with the current version before saving. Scope, routes, API limitations and verification are in [phase3_frontend.md](phase3_frontend.md).

## Phase 6 dashboard and AI

Keep project Overview intact and add Dashboard and AI Assistant tabs. Use the existing bordered surfaces, theme, typography and native controls. Metrics use a four-column definition list on desktop and two columns on mobile, with a labeled completion progress bar and readable status/priority breakdowns. Overdue, workload and activity remain separate sections with server pagination where supported.

AI is an explicit form flow: describe, review/edit/select, confirm, then navigate to created tasks. Proposals retain stable local IDs and use named checkboxes for dependencies. An unresolved confirmation locks the submitted selection for a safe explicit retry. Facts and AI interpretation have separate headings; sample limitations and potentially stale summaries are visible. AI text renders as plain text, and generation is never triggered by background refresh. Detailed scope and verification are in [phase6_frontend.md](phase6_frontend.md).
