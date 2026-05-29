import type { Config } from 'tailwindcss'

export default {
  content: ['./index.html', './src/**/*.{vue,ts,tsx}'],
  theme: {
    extend: {
      colors: {
        // SLYPN brand palette derived from the logo (bright sky -> mid blue -> navy).
        slypn: {
          50:  '#EAF4FF',
          100: '#D5E8FF',
          200: '#A8CFFF',
          300: '#7BB5FF',
          400: '#4E9BFF',
          500: '#1E90FF', // bright sky from SL motif
          600: '#1565C0', // mid blue
          700: '#1E3A5F', // navy from YPN motif
          800: '#152844',
          900: '#0C1929',
        },
        tulip: '#FFFFFF', // white tulip accent on dark backgrounds
      },
      fontFamily: {
        sans:    ['Inter', 'system-ui', 'sans-serif'],
        display: ['Montserrat', 'Inter', 'system-ui', 'sans-serif'],
      },
    },
  },
  plugins: [],
} satisfies Config
