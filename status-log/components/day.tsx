import { notFound } from "next/navigation";
import { getOrCreateEntry } from "@/lib/entries";
import { isValidISODate, monthDay, todayISO } from "@/lib/slack";
import { hasApiKey } from "@/lib/env";
import { DateNav } from "@/components/date-nav";
import { LogView } from "@/components/log-view";
import Link from "next/link";

/** Renders one day's log. Shared by `/` (today) and `/[date]`. */
export function Day({ date }: { date: string }) {
  if (!isValidISODate(date)) notFound();

  const entry = getOrCreateEntry(date);
  const isToday = date === todayISO();
  const keySet = hasApiKey();

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            {monthDay(date)}
            {isToday && (
              <span className="ml-2 text-sm font-normal text-muted-foreground">
                Today
              </span>
            )}
          </h1>
          <p className="text-sm text-muted-foreground">Status Update</p>
        </div>
        <DateNav date={date} />
      </div>

      {!keySet && (
        <div className="rounded-md border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-900/50 dark:bg-amber-950/40 dark:text-amber-200">
          No Anthropic API key set yet. Add one on the{" "}
          <Link href="/settings" className="font-medium underline">
            Settings
          </Link>{" "}
          page before formatting.
        </div>
      )}

      <LogView date={date} initialBody={entry.body} />
    </div>
  );
}
