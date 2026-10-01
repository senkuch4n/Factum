"use client";

import { ThemeContext, useThemeProviderValue } from "@/lib/theme";

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const value = useThemeProviderValue();
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}
