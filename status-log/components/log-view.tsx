"use client";

import { useState, useRef, useEffect, useTransition } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Sparkles, Copy, Pencil, Check, X, Loader2 } from "lucide-react";
import { asCodeBlock } from "@/lib/slack";
import { formatEntry, saveEntry } from "@/app/actions";

export function LogView({
  date,
  initialBody,
}: {
  date: string;
  initialBody: string;
}) {
  const [body, setBody] = useState(initialBody);
  const [draft, setDraft] = useState(initialBody);
  const [editing, setEditing] = useState(false);
  const [pasted, setPasted] = useState("");
  const [formatting, startFormat] = useTransition();
  const [saving, startSave] = useTransition();
  const editRef = useRef<HTMLTextAreaElement>(null);

  // Keep local state in sync if the server sends a different day's entry.
  useEffect(() => {
    setBody(initialBody);
    setDraft(initialBody);
    setEditing(false);
    setPasted("");
  }, [date, initialBody]);

  useEffect(() => {
    if (editing) editRef.current?.focus();
  }, [editing]);

  function handleFormat() {
    if (!pasted.trim()) {
      toast.error("Paste some notes first.");
      return;
    }
    startFormat(async () => {
      const res = await formatEntry(date, pasted);
      if (res.ok) {
        setBody(res.body);
        setDraft(res.body);
        setPasted("");
        toast.success("Formatted and merged into the day's log.");
      } else {
        toast.error(res.error);
      }
    });
  }

  function handleCopy() {
    navigator.clipboard
      .writeText(asCodeBlock(body))
      .then(() => toast.success("Copied code block to clipboard."))
      .catch(() => toast.error("Couldn't access the clipboard."));
  }

  function handleSave() {
    startSave(async () => {
      const res = await saveEntry(date, draft);
      if (res.ok) {
        setBody(draft);
        setEditing(false);
        toast.success("Saved.");
      } else {
        toast.error(res.error ?? "Couldn't save.");
      }
    });
  }

  function handleCancel() {
    setDraft(body);
    setEditing(false);
  }

  return (
    <div className="space-y-6">
      {/* Toolbar */}
      <div className="flex items-center justify-end gap-2">
        {editing ? (
          <>
            <Button
              size="sm"
              variant="outline"
              onClick={handleCancel}
              disabled={saving}
            >
              <X className="h-4 w-4" /> Cancel
            </Button>
            <Button size="sm" onClick={handleSave} disabled={saving}>
              {saving ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <Check className="h-4 w-4" />
              )}
              Save
            </Button>
          </>
        ) : (
          <>
            <Button size="sm" variant="outline" onClick={() => setEditing(true)}>
              <Pencil className="h-4 w-4" /> Edit
            </Button>
            <Button size="sm" variant="outline" onClick={handleCopy}>
              <Copy className="h-4 w-4" /> Copy
            </Button>
          </>
        )}
      </div>

      {/* The day's log — a monospace code block, click to edit */}
      {editing ? (
        <Textarea
          ref={editRef}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          className="min-h-[24rem] rounded-lg bg-muted/40 font-mono text-sm leading-relaxed"
          spellCheck={false}
        />
      ) : (
        <pre
          onClick={() => setEditing(true)}
          title="Click to edit"
          className="min-h-[12rem] cursor-text overflow-x-auto whitespace-pre-wrap break-words rounded-lg border bg-muted/40 p-4 font-mono text-sm leading-relaxed"
        >
          {body}
        </pre>
      )}

      {/* Paste box + Format */}
      <div className="space-y-2">
        <label className="text-sm font-medium text-muted-foreground">
          Raw notes
        </label>
        <Textarea
          value={pasted}
          onChange={(e) => setPasted(e.target.value)}
          placeholder="Paste raw work notes here, then hit Format…"
          className="min-h-[8rem] text-sm"
          disabled={formatting}
        />
        <div className="flex justify-end">
          <Button onClick={handleFormat} disabled={formatting}>
            {formatting ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <Sparkles className="h-4 w-4" />
            )}
            Format
          </Button>
        </div>
      </div>
    </div>
  );
}
