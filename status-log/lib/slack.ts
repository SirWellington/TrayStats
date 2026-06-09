import { format, parse, isValid } from "date-fns";

export const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

/** Local-time ISO date (`YYYY-MM-DD`) for "today". */
export function todayISO(): string {
  return format(new Date(), "yyyy-MM-dd");
}

export function isValidISODate(date: string): boolean {
  if (!DATE_RE.test(date)) return false;
  return isValid(parse(date, "yyyy-MM-dd", new Date()));
}

/** `2026-06-09` -> `June 9` */
export function monthDay(date: string): string {
  return format(parse(date, "yyyy-MM-dd", new Date()), "MMMM d");
}

export const FOOTER = "*🔮 Coming up tomorrow*\n- TBD";

/** The empty log for a brand-new day: header + blank line + footer. */
export function newDayBody(date: string): string {
  return `📅 *${monthDay(date)} — Status Update*\n\n${FOOTER}`;
}

/** Wrap the day's body in a Slack triple-backtick code block for copying. */
export function asCodeBlock(body: string): string {
  return "```\n" + body + "\n```";
}
