"use client";

import { useRouter } from "next/navigation";
import { format, parse, addDays } from "date-fns";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ChevronLeft, ChevronRight } from "lucide-react";

function todayISO() {
  return format(new Date(), "yyyy-MM-dd");
}

function shift(date: string, days: number) {
  return format(addDays(parse(date, "yyyy-MM-dd", new Date()), days), "yyyy-MM-dd");
}

/** Routes to `/` for today and `/<date>` for any other day. */
export function DateNav({ date }: { date: string }) {
  const router = useRouter();
  const today = todayISO();

  function go(target: string) {
    router.push(target === today ? "/" : `/${target}`);
  }

  return (
    <div className="flex items-center gap-2">
      <Button
        variant="outline"
        size="icon"
        aria-label="Previous day"
        onClick={() => go(shift(date, -1))}
      >
        <ChevronLeft className="h-4 w-4" />
      </Button>
      <Input
        type="date"
        value={date}
        max={today}
        className="w-[10.5rem]"
        onChange={(e) => {
          if (e.target.value) go(e.target.value);
        }}
      />
      <Button
        variant="outline"
        size="icon"
        aria-label="Next day"
        disabled={date >= today}
        onClick={() => go(shift(date, 1))}
      >
        <ChevronRight className="h-4 w-4" />
      </Button>
      <Button
        variant="ghost"
        size="sm"
        disabled={date === today}
        onClick={() => go(today)}
      >
        Today
      </Button>
    </div>
  );
}
