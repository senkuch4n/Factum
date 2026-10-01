import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  allowedDevOrigins: ["172.17.0.97","192.168.9.17","192.168.100.174"],
  env: {
    NEXT_PUBLIC_BACKEND_URL: process.env.NEXT_PUBLIC_BACKEND_URL || "http://localhost:8080",
    NEXT_PUBLIC_AGENT_URL: process.env.NEXT_PUBLIC_AGENT_URL || "http://localhost:8765",
  },
};

export default nextConfig;
