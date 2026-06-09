// Cost = (tokens / 1,000,000) * per-million-token rate.
export function computeCost(
  inputTokens: number,
  outputTokens: number,
  inputRate: number,
  outputRate: number,
) {
  const inputCost = (inputTokens / 1_000_000) * inputRate;
  const outputCost = (outputTokens / 1_000_000) * outputRate;
  return {
    inputCost,
    outputCost,
    totalCost: inputCost + outputCost,
  };
}

export function formatUSD(n: number): string {
  return n.toLocaleString("en-US", {
    style: "currency",
    currency: "USD",
    minimumFractionDigits: 2,
    maximumFractionDigits: n < 1 ? 4 : 2,
  });
}

export function formatTokens(n: number): string {
  return n.toLocaleString("en-US");
}
