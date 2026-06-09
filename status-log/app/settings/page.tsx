import { SettingsForm } from "@/components/settings-form";
import { getSystemPrompt, getMemoryRules } from "@/lib/settings";
import { hasApiKey } from "@/lib/env";

export const dynamic = "force-dynamic";

export default function SettingsPage() {
  const keySet = hasApiKey();
  const systemPrompt = getSystemPrompt();
  const rules = getMemoryRules();

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Settings</h1>
        <p className="text-sm text-muted-foreground">
          API key, system prompt, and memory rules.
        </p>
      </div>
      <SettingsForm keySet={keySet} systemPrompt={systemPrompt} rules={rules} />
    </div>
  );
}
