import "server-only";
import Database from "better-sqlite3";
import { drizzle } from "drizzle-orm/better-sqlite3";
import { migrate } from "drizzle-orm/better-sqlite3/migrator";
import path from "node:path";
import fs from "node:fs";
import * as schema from "./schema";
import { settings } from "./schema";
import {
  DEFAULT_SYSTEM_PROMPT,
  DEFAULT_INPUT_RATE,
  DEFAULT_OUTPUT_RATE,
} from "@/lib/defaults";

const dataDir = path.join(process.cwd(), "data");
fs.mkdirSync(dataDir, { recursive: true });

const sqlite = new Database(path.join(dataDir, "status-log.db"));
sqlite.pragma("journal_mode = WAL");

export const db = drizzle(sqlite, { schema });

// Apply migrations + seed default settings once per process. Makes `npm run
// dev` work with zero manual setup steps.
function init() {
  const migrationsFolder = path.join(process.cwd(), "drizzle");
  if (fs.existsSync(migrationsFolder)) {
    migrate(db, { migrationsFolder });
  }

  const defaults: Record<string, string> = {
    system_prompt: DEFAULT_SYSTEM_PROMPT,
    input_rate: DEFAULT_INPUT_RATE,
    output_rate: DEFAULT_OUTPUT_RATE,
  };
  for (const [key, value] of Object.entries(defaults)) {
    db.insert(settings).values({ key, value }).onConflictDoNothing().run();
  }
}

init();

export { schema };
