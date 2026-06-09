import "server-only";
import fs from "node:fs";
import path from "node:path";

const ENV_PATH = path.join(process.cwd(), ".env.local");

/**
 * Read the Anthropic API key. Prefers the live process env, then falls back to
 * parsing `.env.local` so a freshly-saved key works without restarting dev.
 */
export function getApiKey(): string | null {
  if (process.env.ANTHROPIC_API_KEY) return process.env.ANTHROPIC_API_KEY;
  try {
    const content = fs.readFileSync(ENV_PATH, "utf8");
    const m = content.match(/^\s*ANTHROPIC_API_KEY\s*=\s*(.*)$/m);
    if (m) {
      const v = m[1].trim().replace(/^["']|["']$/g, "");
      return v || null;
    }
  } catch {
    // .env.local may not exist yet
  }
  return null;
}

export function hasApiKey(): boolean {
  return !!getApiKey();
}

/** Write/replace ANTHROPIC_API_KEY in `.env.local` and update the live env. */
export function setApiKey(key: string): void {
  const line = `ANTHROPIC_API_KEY=${key}`;
  let content = "";
  try {
    content = fs.readFileSync(ENV_PATH, "utf8");
  } catch {
    // file does not exist yet
  }
  if (/^\s*ANTHROPIC_API_KEY\s*=.*$/m.test(content)) {
    content = content.replace(/^\s*ANTHROPIC_API_KEY\s*=.*$/m, line);
  } else {
    content = content ? content.replace(/\n*$/, "\n") + line + "\n" : line + "\n";
  }
  fs.writeFileSync(ENV_PATH, content, "utf8");
  process.env.ANTHROPIC_API_KEY = key;
}
