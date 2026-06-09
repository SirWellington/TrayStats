import { Day } from "@/components/day";
import { todayISO } from "@/lib/slack";

export const dynamic = "force-dynamic";

export default function HomePage() {
  return <Day date={todayISO()} />;
}
