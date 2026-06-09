import "server-only";
import { db } from "@/db";
import { settings, memoryRules } from "@/db/schema";
import { asc, eq } from "drizzle-orm";
import {
  DEFAULT_SYSTEM_PROMPT,
  DEFAULT_INPUT_RATE,
  DEFAULT_OUTPUT_RATE,
} from "@/lib/defaults";

export function getSetting(key: string): string | null {
  const row = db
    .select()
    .from(settings)
    .where(eq(settings.key, key))
    .get();
  return row?.value ?? null;
}

export function setSetting(key: string, value: string): void {
  db.insert(settings)
    .values({ key, value })
    .onConflictDoUpdate({ target: settings.key, set: { value } })
    .run();
}

export function getSystemPrompt(): string {
  return getSetting("system_prompt") ?? DEFAULT_SYSTEM_PROMPT;
}

export function getInputRate(): number {
  return Number(getSetting("input_rate") ?? DEFAULT_INPUT_RATE);
}

export function getOutputRate(): number {
  return Number(getSetting("output_rate") ?? DEFAULT_OUTPUT_RATE);
}

export function getMemoryRules() {
  return db
    .select()
    .from(memoryRules)
    .orderBy(asc(memoryRules.position), asc(memoryRules.id))
    .all();
}

/** Joins all memory rules into the block appended to the system prompt. */
export function memoryRulesJoined(): string {
  const rules = getMemoryRules();
  if (rules.length === 0) return "";
  return (
    "ADDITIONAL MEMORY RULES (these override and extend the rules above):\n" +
    rules.map((r) => `- ${r.rule}`).join("\n")
  );
}
