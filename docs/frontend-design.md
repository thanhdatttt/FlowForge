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
- Password recovery and OAuth are not implemented server-side. Sign-in help explains the recovery limitation; no invented endpoint or success state is used.
- Signed-in users see an account confirmation; workspace/project UI is a later slice.

### Verification

Run `npm test`, `npm run lint`, `npm run typecheck`, and `npm run build` in `frontend/` with Node 24. Tests exercise the real auth client with mocked HTTP responses; they do not claim a live database/SMTP integration. Browser checks should cover desktop/mobile layouts, validation and focus, show/hide password, help dialog dismissal/focus restoration, mode switching/history, and unavailable-service feedback. Live account creation and OTP delivery additionally require the configured backend and SMTP.
