import { UsageDashboard } from "@/components/usage-dashboard";
import { buildUsageData } from "@/lib/usage";

export const dynamic = "force-dynamic";

export default function UsagePage() {
  const data = buildUsageData();
  return <UsageDashboard data={data} />;
}
