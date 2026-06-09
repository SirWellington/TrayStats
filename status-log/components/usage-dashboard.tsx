"use client";

import { useState, useTransition, Fragment } from "react";
import { useRouter } from "next/navigation";
import {
  ResponsiveContainer,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
} from "recharts";
import { format, parse } from "date-fns";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  DialogClose,
} from "@/components/ui/dialog";
import { ChevronRight, Loader2, Trash2 } from "lucide-react";
import { formatUSD, formatTokens } from "@/lib/pricing";
import type { UsageData } from "@/lib/usage";
import { saveRatesAction, resetUsageAction } from "@/app/actions";

function shortDay(date: string) {
  if (!date) return "—";
  try {
    return format(parse(date, "yyyy-MM-dd", new Date()), "MMM d");
  } catch {
    return date;
  }
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <Card>
      <CardHeader className="pb-2">
        <CardDescription>{label}</CardDescription>
      </CardHeader>
      <CardContent>
        <div className="text-2xl font-semibold tabular-nums">{value}</div>
      </CardContent>
    </Card>
  );
}

function CostChart({ daily }: { daily: UsageData["daily"] }) {
  const data = daily.map((d) => ({ ...d, label: shortDay(d.date) }));
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Cost per day</CardTitle>
        <CardDescription>Last 30 days</CardDescription>
      </CardHeader>
      <CardContent>
        <div className="h-64 w-full">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data} margin={{ top: 4, right: 8, left: 0, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" vertical={false} className="stroke-border" />
              <XAxis
                dataKey="label"
                tick={{ fontSize: 11 }}
                interval={4}
                tickLine={false}
                axisLine={false}
              />
              <YAxis
                tick={{ fontSize: 11 }}
                tickFormatter={(v: number) => `$${v.toFixed(2)}`}
                width={56}
                tickLine={false}
                axisLine={false}
              />
              <Tooltip
                formatter={(v) => [formatUSD(Number(v)), "Cost"]}
                labelFormatter={(l) => String(l)}
                contentStyle={{ fontSize: 12 }}
              />
              <Bar dataKey="cost" fill="var(--primary)" radius={[3, 3, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </CardContent>
    </Card>
  );
}

function RatesPanel({
  inputRate,
  outputRate,
}: {
  inputRate: number;
  outputRate: number;
}) {
  const [input, setInput] = useState(String(inputRate));
  const [output, setOutput] = useState(String(outputRate));
  const [pending, start] = useTransition();
  const router = useRouter();

  function save() {
    start(async () => {
      const res = await saveRatesAction(input, output);
      if (res.ok) {
        toast.success("Rates updated.");
        router.refresh();
      } else {
        toast.error(res.error ?? "Couldn't save rates.");
      }
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Model cost reference</CardTitle>
        <CardDescription>
          Per-million-token rates used for the math. Editable.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1">
            <Label htmlFor="input-rate">Input $/M</Label>
            <Input
              id="input-rate"
              type="number"
              min="0"
              step="0.01"
              value={input}
              onChange={(e) => setInput(e.target.value)}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="output-rate">Output $/M</Label>
            <Input
              id="output-rate"
              type="number"
              min="0"
              step="0.01"
              value={output}
              onChange={(e) => setOutput(e.target.value)}
            />
          </div>
        </div>
        <Button onClick={save} disabled={pending} className="w-full">
          {pending ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
          Save rates
        </Button>
      </CardContent>
    </Card>
  );
}

function BreakdownTable({ perDay }: { perDay: UsageData["perDay"] }) {
  const [open, setOpen] = useState<Set<string>>(new Set());

  function toggle(date: string) {
    setOpen((prev) => {
      const next = new Set(prev);
      if (next.has(date)) next.delete(date);
      else next.add(date);
      return next;
    });
  }

  if (perDay.length === 0) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Per-day breakdown</CardTitle>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-muted-foreground">
            No formatting runs yet.
          </p>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Per-day breakdown</CardTitle>
        <CardDescription>Click a row to see individual runs.</CardDescription>
      </CardHeader>
      <CardContent>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-8" />
              <TableHead>Date</TableHead>
              <TableHead className="text-right">Runs</TableHead>
              <TableHead className="text-right">Input</TableHead>
              <TableHead className="text-right">Output</TableHead>
              <TableHead className="text-right">Cost</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {perDay.map((day) => {
              const isOpen = open.has(day.date);
              return (
                <Fragment key={day.date}>
                  <TableRow
                    className="cursor-pointer"
                    onClick={() => toggle(day.date)}
                  >
                    <TableCell>
                      <ChevronRight
                        className={`h-4 w-4 text-muted-foreground transition-transform ${
                          isOpen ? "rotate-90" : ""
                        }`}
                      />
                    </TableCell>
                    <TableCell className="font-medium">
                      {shortDay(day.date)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {day.runs}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {formatTokens(day.inputTokens)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {formatTokens(day.outputTokens)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {formatUSD(day.cost)}
                    </TableCell>
                  </TableRow>
                  {isOpen &&
                    day.items.map((item) => (
                      <TableRow key={item.id} className="bg-muted/30">
                        <TableCell />
                        <TableCell colSpan={5} className="py-3">
                          <div className="space-y-1.5">
                            <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
                              <span>
                                {item.createdAt
                                  ? item.createdAt.slice(11, 16)
                                  : ""}{" "}
                                UTC
                              </span>
                              <span>
                                for <span className="font-medium">{item.entryDate}</span>
                              </span>
                              <span className="tabular-nums">
                                in {formatTokens(item.inputTokens)} / out{" "}
                                {formatTokens(item.outputTokens)}
                              </span>
                              <span className="tabular-nums">
                                {formatUSD(item.totalCost)}
                              </span>
                              {!item.success && (
                                <span className="rounded bg-red-100 px-1.5 py-0.5 font-medium text-red-700 dark:bg-red-950 dark:text-red-300">
                                  failed
                                </span>
                              )}
                            </div>
                            {item.error ? (
                              <p className="rounded bg-red-50 px-2 py-1 font-mono text-xs text-red-700 dark:bg-red-950/50 dark:text-red-300">
                                {item.error}
                              </p>
                            ) : (
                              <p className="line-clamp-3 whitespace-pre-wrap rounded bg-background px-2 py-1 text-xs">
                                {item.pastedText || (
                                  <span className="text-muted-foreground">
                                    (no input recorded)
                                  </span>
                                )}
                              </p>
                            )}
                          </div>
                        </TableCell>
                      </TableRow>
                    ))}
                </Fragment>
              );
            })}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function ResetButton() {
  const [pending, start] = useTransition();
  const router = useRouter();

  function reset() {
    start(async () => {
      await resetUsageAction();
      toast.success("Usage data wiped.");
      router.refresh();
    });
  }

  return (
    <Dialog>
      <DialogTrigger render={<Button variant="outline" size="sm" />}>
        <Trash2 className="h-4 w-4" /> Reset usage data
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Reset usage data?</DialogTitle>
          <DialogDescription>
            This permanently deletes every logged run. Your status logs are not
            affected. This cannot be undone.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
          <DialogClose
            render={
              <Button
                variant="destructive"
                onClick={reset}
                disabled={pending}
              />
            }
          >
            {pending ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
            Wipe everything
          </DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

export function UsageDashboard({ data }: { data: UsageData }) {
  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Usage &amp; cost</h1>
          <p className="text-sm text-muted-foreground">
            Every formatting run, logged.
          </p>
        </div>
        <ResetButton />
      </div>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <Stat label="Spend this month" value={formatUSD(data.monthSpend)} />
        <Stat label="Spend all-time" value={formatUSD(data.allTimeSpend)} />
        <Stat label="Tokens this month" value={formatTokens(data.monthTokens)} />
        <Stat label="Runs this month" value={String(data.monthRuns)} />
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="lg:col-span-2">
          <CostChart daily={data.daily} />
        </div>
        <RatesPanel inputRate={data.inputRate} outputRate={data.outputRate} />
      </div>

      <BreakdownTable perDay={data.perDay} />
    </div>
  );
}
