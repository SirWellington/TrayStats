# Status Log

A Slack-style daily status log tool. Paste in raw work notes, hit **Format**,
and Claude (`claude-opus-4-7`) returns a clean Slack-formatted update that gets
merged into the day's running log — appended above the
`*🔮 Coming up tomorrow*` footer, without disturbing existing sections.

Runs entirely locally. Zero external infra: SQLite in a single file.

## Stack

- **Next.js 15** (App Router) · TypeScript · Tailwind v4
- **SQLite** via `better-sqlite3`, schema/queries via **Drizzle ORM**
- **`@anthropic-ai/sdk`** for the formatting calls
- **shadcn/ui** components · **recharts** for the cost chart
- **Server Actions** for everything that mutates — no API routes

## Getting started

```bash
npm install
npm run dev
```

Open <http://localhost:3000>. The database and default settings are created
automatically on first run.

Then go to **Settings** and paste your Anthropic API key. It's written to
`.env.local` (git-ignored) and never displayed back.

## Routes

| Route        | What it does                                                        |
| ------------ | ------------------------------------------------------------------- |
| `/`          | Today's log + paste box. Format, copy, inline-edit.                 |
| `/[date]`    | Any past day, e.g. `/2026-06-05`. New days auto-create.             |
| `/settings`  | API key, editable system prompt, accumulating memory rules.         |
| `/usage`     | Spend/token dashboard: stats, 30-day chart, per-run breakdown.      |

## How a format run works

1. Your pasted notes + the current day's log + the system prompt (with memory
   rules appended) are sent to `claude-opus-4-7`.
2. Claude returns the **full updated day's log**, which replaces the body.
3. The call is logged to the `usage` table (tokens, cost, the input given).

Cost = `(input_tokens / 1e6) * input_rate + (output_tokens / 1e6) * output_rate`.
Default rates ($15/M input, $75/M output) are stored in the `settings` table and
editable on `/usage`.

## Scripts

| Script                | Purpose                                       |
| --------------------- | --------------------------------------------- |
| `npm run dev`         | Start the dev server                          |
| `npm run build`       | Production build                              |
| `npm run lint`        | ESLint                                        |
| `npm run db:generate` | Generate a Drizzle migration from the schema  |
| `npm run db:migrate`  | Apply migrations                              |
| `npm run db:seed`     | Apply migrations + create today's empty entry |

## Data

The SQLite file lives at `data/status-log.db` (git-ignored). Schema:

- `entries` — one row per day (`date` PK, `body`, timestamps)
- `memory_rules` — extra do/don't rules appended to the system prompt
- `usage` — one row per API call (tokens, costs, input, success/error)
- `settings` — system prompt, per-million token rates
