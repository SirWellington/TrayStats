# Status Log — agent notes

Slack-style daily status log tool. Paste raw notes → Claude formats them →
appended into the day's running log.

## Stack
- Next.js 15 (App Router, Server Actions) · TypeScript · Tailwind v4
- SQLite via `better-sqlite3` + Drizzle ORM (single file under `data/`)
- `@anthropic-ai/sdk` — model `claude-opus-4-7` (see `lib/defaults.ts`)
- shadcn/ui components (built on **Base UI**, not Radix — use the `render`
  prop, not `asChild`)
- recharts for the usage chart

## Layout
- `app/` — routes: `/` (today), `/[date]`, `/settings`, `/usage`; `actions.ts`
  holds every Server Action.
- `db/` — Drizzle `schema.ts`, auto-migrating client `index.ts`, `seed.ts`.
- `lib/` — `entries`, `settings`, `usage`, `slack` (templating), `pricing`,
  `env` (API key in `.env.local`), `defaults` (model + system prompt).
- `components/` — UI; `ui/` is shadcn.

## Conventions
- Server-only data modules import `"server-only"`; never import them from a
  standalone script (`db/seed.ts` is self-contained for that reason).
- DB migrations + default settings are applied automatically on first DB
  access (`db/index.ts`), so `npm run dev` works with zero setup.
- The API key lives in `.env.local`, never the DB. Rates + system prompt +
  memory rules live in the `settings`/`memory_rules` tables.

## Scripts
`npm run dev` · `npm run build` · `npm run lint` · `npm run db:generate` ·
`npm run db:migrate` · `npm run db:seed`
