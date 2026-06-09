// Standalone seed script: applies migrations and creates today's empty entry.
// Self-contained (no Next "server-only" modules) so it runs under plain tsx.
// Run with `npm run db:seed`.
import Database from "better-sqlite3";
import { drizzle } from "drizzle-orm/better-sqlite3";
import { migrate } from "drizzle-orm/better-sqlite3/migrator";
import path from "node:path";
import fs from "node:fs";
import { entries, settings } from "./schema";
import { newDayBody, todayISO } from "../lib/slack";
import {
  DEFAULT_SYSTEM_PROMPT,
  DEFAULT_INPUT_RATE,
  DEFAULT_OUTPUT_RATE,
} from "../lib/defaults";

const dataDir = path.join(process.cwd(), "data");
fs.mkdirSync(dataDir, { recursive: true });

const sqlite = new Database(path.join(dataDir, "status-log.db"));
sqlite.pragma("journal_mode = WAL");
const db = drizzle(sqlite);

migrate(db, { migrationsFolder: path.join(process.cwd(), "drizzle") });

const defaults: Record<string, string> = {
  system_prompt: DEFAULT_SYSTEM_PROMPT,
  input_rate: DEFAULT_INPUT_RATE,
  output_rate: DEFAULT_OUTPUT_RATE,
};
for (const [key, value] of Object.entries(defaults)) {
  db.insert(settings).values({ key, value }).onConflictDoNothing().run();
}

const today = todayISO();
const body = newDayBody(today);
db.insert(entries).values({ date: today, body }).onConflictDoNothing().run();

console.log(`Seeded defaults + today's entry (${today}):\n`);
console.log(body);
