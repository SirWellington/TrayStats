"use server";

import Anthropic from "@anthropic-ai/sdk";
import { revalidatePath } from "next/cache";
import { db } from "@/db";
import { memoryRules, usage } from "@/db/schema";
import { eq } from "drizzle-orm";
import { getApiKey, setApiKey } from "@/lib/env";
import {
  getSystemPrompt,
  memoryRulesJoined,
  getInputRate,
  getOutputRate,
  setSetting,
} from "@/lib/settings";
import { getOrCreateEntry, saveEntryBody } from "@/lib/entries";
import { computeCost } from "@/lib/pricing";
import { isValidISODate } from "@/lib/slack";
import { MODEL } from "@/lib/defaults";

type FormatResult =
  | { ok: true; body: string }
  | { ok: false; error: string };

/**
 * Send the pasted notes + current day's log + system prompt to Claude, log the
 * usage row, persist the returned body, and return it. Claude returns the full
 * updated day's log, ready to replace the body.
 */
export async function formatEntry(
  date: string,
  pastedText: string,
): Promise<FormatResult> {
  if (!isValidISODate(date)) return { ok: false, error: "Invalid date." };
  if (!pastedText.trim()) return { ok: false, error: "Nothing to format." };

  const apiKey = getApiKey();
  if (!apiKey) {
    return {
      ok: false,
      error: "No Anthropic API key set. Add one on the Settings page.",
    };
  }

  const entry = getOrCreateEntry(date);
  const existingBody = entry.body;
  const systemPrompt = getSystemPrompt();
  const rules = memoryRulesJoined();
  const system = rules ? systemPrompt + "\n\n" + rules : systemPrompt;
  const inputRate = getInputRate();
  const outputRate = getOutputRate();

  const anthropic = new Anthropic({ apiKey });

  try {
    const response = await anthropic.messages.create({
      model: MODEL,
      max_tokens: 4000,
      system,
      messages: [
        {
          role: "user",
          content: `Current day's log:\n\n${existingBody}\n\nNew notes to incorporate:\n\n${pastedText}`,
        },
      ],
    });

    const updatedBody = response.content
      .filter((b): b is Anthropic.TextBlock => b.type === "text")
      .map((b) => b.text)
      .join("")
      .trim();

    const inputTokens = response.usage.input_tokens;
    const outputTokens = response.usage.output_tokens;
    const { inputCost, outputCost, totalCost } = computeCost(
      inputTokens,
      outputTokens,
      inputRate,
      outputRate,
    );

    db.insert(usage)
      .values({
        model: MODEL,
        inputTokens,
        outputTokens,
        inputCost,
        outputCost,
        totalCost,
        entryDate: date,
        success: true,
        pastedText,
        error: null,
      })
      .run();

    saveEntryBody(date, updatedBody);
    revalidatePath("/");
    revalidatePath(`/${date}`);
    revalidatePath("/usage");

    return { ok: true, body: updatedBody };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    db.insert(usage)
      .values({
        model: MODEL,
        inputTokens: 0,
        outputTokens: 0,
        inputCost: 0,
        outputCost: 0,
        totalCost: 0,
        entryDate: date,
        success: false,
        pastedText,
        error: message,
      })
      .run();
    revalidatePath("/usage");
    return { ok: false, error: message };
  }
}

/** Persist a hand-edited day's log. */
export async function saveEntry(date: string, body: string) {
  if (!isValidISODate(date)) return { ok: false, error: "Invalid date." };
  saveEntryBody(date, body);
  revalidatePath("/");
  revalidatePath(`/${date}`);
  return { ok: true };
}

// ---- Settings -------------------------------------------------------------

export async function saveSystemPrompt(prompt: string) {
  setSetting("system_prompt", prompt);
  revalidatePath("/settings");
  return { ok: true };
}

export async function saveApiKeyAction(key: string) {
  const trimmed = key.trim();
  if (!trimmed) return { ok: false, error: "API key cannot be empty." };
  setApiKey(trimmed);
  revalidatePath("/settings");
  return { ok: true };
}

export async function addMemoryRuleAction(rule: string) {
  const trimmed = rule.trim();
  if (!trimmed) return { ok: false, error: "Rule cannot be empty." };
  const maxPos = db
    .select({ position: memoryRules.position })
    .from(memoryRules)
    .all()
    .reduce((m, r) => Math.max(m, r.position), -1);
  db.insert(memoryRules)
    .values({ rule: trimmed, position: maxPos + 1 })
    .run();
  revalidatePath("/settings");
  return { ok: true };
}

export async function updateMemoryRuleAction(id: number, rule: string) {
  const trimmed = rule.trim();
  if (!trimmed) return { ok: false, error: "Rule cannot be empty." };
  db.update(memoryRules)
    .set({ rule: trimmed })
    .where(eq(memoryRules.id, id))
    .run();
  revalidatePath("/settings");
  return { ok: true };
}

export async function deleteMemoryRuleAction(id: number) {
  db.delete(memoryRules).where(eq(memoryRules.id, id)).run();
  revalidatePath("/settings");
  return { ok: true };
}

// ---- Usage ----------------------------------------------------------------

export async function saveRatesAction(inputRate: string, outputRate: string) {
  const i = Number(inputRate);
  const o = Number(outputRate);
  if (!Number.isFinite(i) || i < 0 || !Number.isFinite(o) || o < 0) {
    return { ok: false, error: "Rates must be non-negative numbers." };
  }
  setSetting("input_rate", String(i));
  setSetting("output_rate", String(o));
  revalidatePath("/usage");
  return { ok: true };
}

export async function resetUsageAction() {
  db.delete(usage).run();
  revalidatePath("/usage");
  return { ok: true };
}
