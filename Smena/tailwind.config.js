/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './Pages/**/*.cshtml',
    './Pages/**/*.cs',
  ],
  darkMode: 'class',
  theme: {
    extend: {
      fontFamily: {
        sans: ['"Inter"', 'system-ui', 'sans-serif'],
        mono: ['"JetBrains Mono"', '"IBM Plex Mono"', 'monospace'],
      },
      colors: {
        glass: {
          50:  'rgba(255,255,255,.03)',
          100: 'rgba(255,255,255,.05)',
          200: 'rgba(255,255,255,.08)',
          300: 'rgba(255,255,255,.12)',
          400: 'rgba(255,255,255,.18)',
        },
        night: {
          950: '#06090f',
          900: '#0a0f1c',
          800: '#0e1528',
          700: '#141d38',
          600: '#1a2748',
        },
        accent: {
          DEFAULT: '#6c5ce7',
          light:   '#a29bfe',
          glow:    'rgba(108,92,231,.25)',
          dim:     'rgba(108,92,231,.10)',
        },
        mint: {
          DEFAULT: '#00cec9',
          glow:    'rgba(0,206,201,.20)',
        },
        rose: {
          DEFAULT: '#fd79a8',
          glow:    'rgba(253,121,168,.20)',
        },
        amber: {
          DEFAULT: '#fdcb6e',
          glow:    'rgba(253,203,110,.20)',
        },
      },
      borderRadius: {
        '2xl': '16px',
        '3xl': '20px',
      },
      backdropBlur: {
        xs: '2px',
      },
      animation: {
        'float': 'float 6s ease-in-out infinite',
        'glow-pulse': 'glow-pulse 2s ease-in-out infinite',
        'slide-up': 'slide-up .5s cubic-bezier(.16,1,.3,1) both',
        'fade-in': 'fade-in .3s ease both',
      },
      keyframes: {
        float: {
          '0%, 100%': { transform: 'translateY(0)' },
          '50%': { transform: 'translateY(-8px)' },
        },
        'glow-pulse': {
          '0%, 100%': { opacity: '1' },
          '50%': { opacity: '.4' },
        },
        'slide-up': {
          from: { opacity: '0', transform: 'translateY(20px)' },
          to:   { opacity: '1', transform: 'translateY(0)' },
        },
        'fade-in': {
          from: { opacity: '0', transform: 'translateY(-4px)' },
          to:   { opacity: '1', transform: 'translateY(0)' },
        },
      },
    },
  },
  plugins: [],
}
