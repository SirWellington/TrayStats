"use client";

import { useState, useTransition } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Loader2, Plus, Trash2, Check, X, Pencil } from "lucide-react";
import type { MemoryRule } from "@/db/schema";
import {
  saveApiKeyAction,
  saveSystemPrompt,
  addMemoryRuleAction,
  updateMemoryRuleAction,
  deleteMemoryRuleAction,
} from "@/app/actions";

function ApiKeySection({ keySet }: { keySet: boolean }) {
  const [editing, setEditing] = useState(!keySet);
  const [value, setValue] = useState("");
  const [pending, start] = useTransition();

  function save() {
    if (!value.trim()) {
      toast.error("Enter an API key.");
      return;
    }
    start(async () => {
      const res = await saveApiKeyAction(value);
      if (res.ok) {
        setValue("");
        setEditing(false);
        toast.success("API key saved to .env.local.");
      } else {
        toast.error(res.error ?? "Couldn't save the key.");
      }
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Anthropic API key</CardTitle>
        <CardDescription>
          Stored in <code>.env.local</code>, never displayed back.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="flex items-center gap-2 text-sm">
          <span className="text-muted-foreground">Status:</span>
          {keySet ? (
            <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300">
              <Check className="h-3 w-3" /> Set
            </span>
          ) : (
            <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800 dark:bg-amber-950 dark:text-amber-300">
              Not set
            </span>
          )}
        </div>

        {editing ? (
          <div className="flex flex-col gap-2 sm:flex-row">
            <Input
              type="password"
              autoComplete="off"
              placeholder="sk-ant-…"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              className="font-mono"
            />
            <div className="flex gap-2">
              <Button onClick={save} disabled={pending}>
                {pending ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
                Save
              </Button>
              {keySet && (
                <Button
                  variant="outline"
                  onClick={() => {
                    setEditing(false);
                    setValue("");
                  }}
                  disabled={pending}
                >
                  Cancel
                </Button>
              )}
            </div>
          </div>
        ) : (
          <Button variant="outline" onClick={() => setEditing(true)}>
            <Pencil className="h-4 w-4" /> Replace key
          </Button>
        )}
      </CardContent>
    </Card>
  );
}

function SystemPromptSection({ initial }: { initial: string }) {
  const [value, setValue] = useState(initial);
  const [pending, start] = useTransition();

  function save() {
    start(async () => {
      const res = await saveSystemPrompt(value);
      if (res.ok) toast.success("System prompt saved.");
      else toast.error("Couldn't save.");
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>System prompt</CardTitle>
        <CardDescription>
          Sent on every formatting run. Memory rules below are appended to it.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <Textarea
          value={value}
          onChange={(e) => setValue(e.target.value)}
          className="min-h-[20rem] font-mono text-xs leading-relaxed"
          spellCheck={false}
        />
        <div className="flex justify-end">
          <Button onClick={save} disabled={pending}>
            {pending ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
            Save
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

function RuleRow({ rule }: { rule: MemoryRule }) {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(rule.rule);
  const [pending, start] = useTransition();

  function save() {
    start(async () => {
      const res = await updateMemoryRuleAction(rule.id, value);
      if (res.ok) {
        setEditing(false);
        toast.success("Rule updated.");
      } else {
        toast.error(res.error ?? "Couldn't update.");
      }
    });
  }

  function remove() {
    start(async () => {
      await deleteMemoryRuleAction(rule.id);
      toast.success("Rule removed.");
    });
  }

  return (
    <div className="flex items-start gap-2">
      {editing ? (
        <>
          <Textarea
            value={value}
            onChange={(e) => setValue(e.target.value)}
            className="min-h-[2.5rem] text-sm"
          />
          <Button size="icon" onClick={save} disabled={pending} aria-label="Save rule">
            <Check className="h-4 w-4" />
          </Button>
          <Button
            size="icon"
            variant="outline"
            onClick={() => {
              setValue(rule.rule);
              setEditing(false);
            }}
            disabled={pending}
            aria-label="Cancel"
          >
            <X className="h-4 w-4" />
          </Button>
        </>
      ) : (
        <>
          <p className="flex-1 rounded-md border bg-muted/30 px-3 py-2 text-sm">
            {rule.rule}
          </p>
          <Button
            size="icon"
            variant="outline"
            onClick={() => setEditing(true)}
            aria-label="Edit rule"
          >
            <Pencil className="h-4 w-4" />
          </Button>
          <Button
            size="icon"
            variant="outline"
            onClick={remove}
            disabled={pending}
            aria-label="Delete rule"
          >
            <Trash2 className="h-4 w-4" />
          </Button>
        </>
      )}
    </div>
  );
}

function MemoryRulesSection({ rules }: { rules: MemoryRule[] }) {
  const [value, setValue] = useState("");
  const [pending, start] = useTransition();

  function add() {
    if (!value.trim()) {
      toast.error("Enter a rule.");
      return;
    }
    start(async () => {
      const res = await addMemoryRuleAction(value);
      if (res.ok) {
        setValue("");
        toast.success("Rule added.");
      } else {
        toast.error(res.error ?? "Couldn't add.");
      }
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Memory rules</CardTitle>
        <CardDescription>
          Extra do/don&apos;t rules appended to the system prompt on every call.
          They accumulate over time.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {rules.length === 0 ? (
          <p className="text-sm text-muted-foreground">No rules yet.</p>
        ) : (
          <div className="space-y-2">
            {rules.map((r) => (
              <RuleRow key={r.id} rule={r} />
            ))}
          </div>
        )}
        <div className="flex items-start gap-2 border-t pt-3">
          <Textarea
            value={value}
            onChange={(e) => setValue(e.target.value)}
            placeholder="e.g. Don't mention internal ticket numbers."
            className="min-h-[2.5rem] text-sm"
          />
          <Button onClick={add} disabled={pending} aria-label="Add rule">
            {pending ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <Plus className="h-4 w-4" />
            )}
            Add
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

export function SettingsForm({
  keySet,
  systemPrompt,
  rules,
}: {
  keySet: boolean;
  systemPrompt: string;
  rules: MemoryRule[];
}) {
  return (
    <div className="space-y-6">
      <ApiKeySection keySet={keySet} />
      <SystemPromptSection initial={systemPrompt} />
      <MemoryRulesSection rules={rules} />
    </div>
  );
}
