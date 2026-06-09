import "server-only";
import { db } from "@/db";
import { usage } from "@/db/schema";
import { desc } from "drizzle-orm";
import { format, subDays } from "date-fns";
import { getInputRate, getOutputRate } from "@/lib/settings";
import type { UsageRow } from "@/db/schema";

export type RunItem = {
  id: number;
  createdAt: string;
  inputTokens: number;
  outputTokens: number;
  totalCost: number;
  entryDate: string;
  pastedText: string;
  success: boolean;
  error: string | null;
};

export type DayBucket = {
  date: string;
  runs: number;
  inputTokens: number;
  outputTokens: number;
  cost: number;
  items: RunItem[];
};

export type UsageData = {
  monthSpend: number;
  allTimeSpend: number;
  monthTokens: number;
  monthRuns: number;
  daily: { date: string; cost: number }[];
  perDay: DayBucket[];
  inputRate: number;
  outputRate: number;
};

/** A run's calendar day, derived from its stored timestamp. */
function runDate(row: UsageRow): string {
  return (row.createdAt ?? "").slice(0, 10);
}

export function buildUsageData(): UsageData {
  const rows = db.select().from(usage).orderBy(desc(usage.createdAt), desc(usage.id)).all();

  const today = format(new Date(), "yyyy-MM-dd");
  const month = today.slice(0, 7);

  let monthSpend = 0;
  let allTimeSpend = 0;
  let monthTokens = 0;
  let monthRuns = 0;

  const byDay = new Map<string, DayBucket>();

  for (const row of rows) {
    const d = runDate(row);
    allTimeSpend += row.totalCost;
    if (d.slice(0, 7) === month) {
      monthSpend += row.totalCost;
      monthTokens += row.inputTokens + row.outputTokens;
      monthRuns += 1;
    }

    let bucket = byDay.get(d);
    if (!bucket) {
      bucket = {
        date: d,
        runs: 0,
        inputTokens: 0,
        outputTokens: 0,
        cost: 0,
        items: [],
      };
      byDay.set(d, bucket);
    }
    bucket.runs += 1;
    bucket.inputTokens += row.inputTokens;
    bucket.outputTokens += row.outputTokens;
    bucket.cost += row.totalCost;
    bucket.items.push({
      id: row.id,
      createdAt: row.createdAt,
      inputTokens: row.inputTokens,
      outputTokens: row.outputTokens,
      totalCost: row.totalCost,
      entryDate: row.entryDate,
      pastedText: row.pastedText,
      success: row.success,
      error: row.error,
    });
  }

  // Last 30 days, zero-filled, oldest -> newest for the chart.
  const daily: { date: string; cost: number }[] = [];
  for (let i = 29; i >= 0; i--) {
    const d = format(subDays(new Date(), i), "yyyy-MM-dd");
    daily.push({ date: d, cost: byDay.get(d)?.cost ?? 0 });
  }

  const perDay = [...byDay.values()].sort((a, b) =>
    a.date < b.date ? 1 : a.date > b.date ? -1 : 0,
  );

  return {
    monthSpend,
    allTimeSpend,
    monthTokens,
    monthRuns,
    daily,
    perDay,
    inputRate: getInputRate(),
    outputRate: getOutputRate(),
  };
}
