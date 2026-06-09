import "server-only";
import { db } from "@/db";
import { entries } from "@/db/schema";
import { desc, eq } from "drizzle-orm";
import { newDayBody } from "@/lib/slack";

export function getEntry(date: string) {
  return db.select().from(entries).where(eq(entries.date, date)).get() ?? null;
}

/** Fetch the day's entry, auto-creating an empty one (header + footer) if new. */
export function getOrCreateEntry(date: string) {
  const existing = getEntry(date);
  if (existing) return existing;
  const body = newDayBody(date);
  db.insert(entries).values({ date, body }).onConflictDoNothing().run();
  return getEntry(date)!;
}

export function saveEntryBody(date: string, body: string) {
  db.insert(entries)
    .values({ date, body })
    .onConflictDoUpdate({
      target: entries.date,
      set: { body, updatedAt: new Date().toISOString() },
    })
    .run();
}

/** All dates that have an entry, newest first — for the date navigator. */
export function listEntryDates(): string[] {
  return db
    .select({ date: entries.date })
    .from(entries)
    .orderBy(desc(entries.date))
    .all()
    .map((r) => r.date);
}
