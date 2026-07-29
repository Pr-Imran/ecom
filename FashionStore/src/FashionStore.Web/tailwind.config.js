/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './Views/**/*.cshtml',
    './wwwroot/js/**/*.js'
  ],
  theme: {
    extend: {
      colors: {
        'brand-primary': 'var(--color-primary)',
        'brand-secondary': 'var(--color-secondary)',
        'brand-accent': 'var(--color-accent)',
        'brand-bg': 'var(--color-bg)',
        'brand-surface': 'var(--color-surface)',
        'brand-border': 'var(--color-border)',
        'brand-text-primary': 'var(--color-text-primary)',
        'brand-text-muted': 'var(--color-text-muted)',
        'brand-success': 'var(--color-success)',
        'brand-warning': 'var(--color-warning)',
        'brand-danger': 'var(--color-danger)',
        'brand-info': 'var(--color-info)',
      },
      borderRadius: {
        'brand': 'var(--radius-brand)',
        'brand-sm': 'var(--radius-brand-sm)',
        'brand-lg': 'var(--radius-brand-lg)',
      },
      boxShadow: {
        'brand-sm': 'var(--shadow-sm)',
        'brand': 'var(--shadow-brand)',
        'brand-md': 'var(--shadow-md)',
        'brand-lg': 'var(--shadow-lg)',
      },
      maxWidth: {
        'container-sm': '640px',
        'container-md': '768px',
        'container-lg': '1024px',
        'container-xl': '1280px',
      },
      fontSize: {
        '2xs': ['0.625rem', { lineHeight: '0.875rem' }],
        '3xl': ['1.875rem', { lineHeight: '2.25rem' }],
        '4xl': ['2.25rem', { lineHeight: '2.75rem' }],
      },
      spacing: {
        'safe-top': 'env(safe-area-inset-top)',
        'safe-bottom': 'env(safe-area-inset-bottom)',
        'safe-left': 'env(safe-area-inset-left)',
        'safe-right': 'env(safe-area-inset-right)',
        '18': '4.5rem',
      },
      transitionDuration: {
        'brand': 'var(--transition-brand)',
      },
      zIndex: {
        'dropdown': '100',
        'sticky': '200',
        'fixed': '300',
        'modal-backdrop': '400',
        'modal': '500',
        'popover': '600',
        'tooltip': '700',
        'toast': '800',
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', '-apple-system', 'sans-serif'],
      },
    },
  },
  plugins: [],
}
