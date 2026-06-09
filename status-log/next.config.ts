import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // better-sqlite3 is a native module — keep it out of the bundler so the
  // prebuilt binary is loaded directly at runtime.
  serverExternalPackages: ["better-sqlite3"],
};

export default nextConfig;
