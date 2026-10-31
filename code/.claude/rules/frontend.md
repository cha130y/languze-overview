---
paths:
  - "apps/web/**/*.{ts,tsx}"
---

# Frontend Rules

- Follow the existing Next.js App Router structure before introducing a new pattern.
- Prefer Server Components; use `use client` only when required by interactivity, browser APIs, or client state.
- Keep server state in TanStack Query where the project uses it; do not duplicate the same remote state in ad-hoc global state.
- Keep forms with React Hook Form + Zod where form complexity justifies them.
- Reuse existing shadcn/ui and project components before creating duplicates.
- Keep API calls in the existing API/data-access layer rather than scattering fetch logic across components.
- Handle loading, error, empty, and disabled states deliberately.
- Keep accessibility semantics, keyboard interaction, and responsive behavior in scope.
- Do not move server-only secrets or provider SDK usage into client bundles.
