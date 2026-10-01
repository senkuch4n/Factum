import type { Config } from 'tailwindcss'

export default {
  darkMode: 'class',
  content: ['./src/renderer/src/**/*.{ts,tsx}', './src/renderer/index.html'],
  theme: {
    extend: {
      colors: {
        brand: { DEFAULT: '#003366', light: '#0055a5' },
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', 'sans-serif'],
        mono: ['"IBM Plex Mono"', 'monospace'],
      },
    },
  },
} satisfies Config
