import { redirect } from "next/navigation";
import { Day } from "@/components/day";
import { todayISO } from "@/lib/slack";

export const dynamic = "force-dynamic";

export default async function DatePage({
  params,
}: {
  params: Promise<{ date: string }>;
}) {
  const { date } = await params;
  // Keep "today" canonical at `/`.
  if (date === todayISO()) redirect("/");
  return <Day date={date} />;
}
