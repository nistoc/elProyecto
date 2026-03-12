# Authorization (placeholder)

This document is a placeholder for future authentication and authorization.

- **Token/session:** The UI is prepared to send an auth token or session cookie when a single gateway is introduced.
- **Headers:** Optional `Authorization: Bearer <token>` or custom header can be added in the API layer (`src/api.ts`).
- **Environment:** Token or API key can be read from `import.meta.env` (e.g. `VITE_AUTH_TOKEN`) when needed.

No auth is required for local development against XtractManager API.
