import { sql } from "drizzle-orm";
import { sqliteTable, text, integer, real } from "drizzle-orm/sqlite-core";

// One row per day. `date` is the ISO `YYYY-MM-DD` key, `body` is the full
// Slack-formatted markdown for that day's running log.
export const entries = sqliteTable("entries", {
  date: text("date").primaryKey(),
  body: text("body").notNull(),
  createdAt: text("created_at")
    .notNull()
    .default(sql`(current_timestamp)`),
  updatedAt: text("updated_at")
    .notNull()
    .default(sql`(current_timestamp)`),
});

// Additional do/don't rules that get appended to the system prompt on every
// call. They accumulate over time and are ordered by `position`.
export const memoryRules = sqliteTable("memory_rules", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  rule: text("rule").notNull(),
  position: integer("position").notNull().default(0),
  createdAt: text("created_at")
    .notNull()
    .default(sql`(current_timestamp)`),
});

// One row per Anthropic API call (success or failure) for the cost dashboard.
export const usage = sqliteTable("usage", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  createdAt: text("created_at")
    .notNull()
    .default(sql`(current_timestamp)`),
  model: text("model").notNull(),
  inputTokens: integer("input_tokens").notNull().default(0),
  outputTokens: integer("output_tokens").notNull().default(0),
  inputCost: real("input_cost").notNull().default(0),
  outputCost: real("output_cost").notNull().default(0),
  totalCost: real("total_cost").notNull().default(0),
  entryDate: text("entry_date").notNull(),
  success: integer("success", { mode: "boolean" }).notNull().default(true),
  pastedText: text("pasted_text").notNull().default(""),
  error: text("error"),
});

// Key/value store for the editable system prompt, per-million token rates, etc.
export const settings = sqliteTable("settings", {
  key: text("key").primaryKey(),
  value: text("value").notNull(),
});

export type Entry = typeof entries.$inferSelect;
export type MemoryRule = typeof memoryRules.$inferSelect;
export type UsageRow = typeof usage.$inferSelect;
