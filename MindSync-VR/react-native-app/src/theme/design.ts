// "Still Water at Night" design system — the single source of visual truth for
// the reworked voice check-in. Depth comes from layered translucency + soft
// light, not borders. New screens/components import from here; the legacy
// theme.ts stays for screens not yet migrated.

export const palette = {
  // Base — deep and layered, never one flat blue
  bg900: '#050813',
  bg800: '#0A1024',
  bg700: '#111A38',
  surface: 'rgba(255,255,255,0.06)', // glass card fill
  surfaceHi: 'rgba(255,255,255,0.10)',
  // Opaque-ish card fill for text-bearing cards, so the bright aurora blooms
  // don't shine through the words and read as a highlighter swipe. Sits over
  // bg800 so cards still feel layered, not flat black.
  cardFill: 'rgba(9,14,30,0.62)',
  hairline: 'rgba(255,255,255,0.10)', // top inner edge only

  // Accent — Sarah's identity
  aqua: '#3DE0D0',
  aquaDeep: '#12A8A0',
  violet: '#8B7BF0',
  violetDeep: '#5B4BC4',

  // State
  calm: '#34D399',
  mild: '#7DD3FC',
  moderate: '#FBBF24',
  high: '#F87171',
  danger: '#EF4444',

  // Type
  textHi: '#FFFFFF',
  textMid: 'rgba(255,255,255,0.72)',
  textLow: 'rgba(255,255,255,0.45)',
} as const;

// Large blurred radial blooms behind content — the main flatness killer.
export const aurora = {
  a: {color: '#1E3A8A', size: 420, opacity: 0.55},
  b: {color: '#0F766E', size: 360, opacity: 0.4},
} as const;

export const type = {
  display: {fontSize: 34, fontWeight: '700' as const, lineHeight: 40, letterSpacing: -0.5},
  h1: {fontSize: 26, fontWeight: '700' as const, lineHeight: 32, letterSpacing: -0.3},
  h2: {fontSize: 20, fontWeight: '600' as const, lineHeight: 26},
  body: {fontSize: 16, fontWeight: '400' as const, lineHeight: 24},
  bodyMid: {fontSize: 15, fontWeight: '500' as const, lineHeight: 22},
  caption: {fontSize: 13, fontWeight: '500' as const, lineHeight: 18},
  label: {fontSize: 11, fontWeight: '700' as const, lineHeight: 14, letterSpacing: 1.2},
  metric: {fontSize: 48, fontWeight: '700' as const, lineHeight: 52, letterSpacing: -1.5},
  metricXL: {fontSize: 68, fontWeight: '700' as const, lineHeight: 72, letterSpacing: -2},
} as const;

export const space = {xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32, xxxl: 48} as const;
export const radius = {sm: 12, md: 16, lg: 24, xl: 32, pill: 999} as const;

export const elevation = {
  card: {shadowColor: '#000', shadowOpacity: 0.35, shadowRadius: 24, shadowOffset: {width: 0, height: 8}, elevation: 8},
  float: {shadowColor: '#000', shadowOpacity: 0.45, shadowRadius: 40, shadowOffset: {width: 0, height: 16}, elevation: 16},
  glow: (c: string) => ({shadowColor: c, shadowOpacity: 0.55, shadowRadius: 28, shadowOffset: {width: 0, height: 0}, elevation: 12}),
} as const;

export const motion = {
  breathMs: 4000,
  enterMs: 420,
  exitMs: 240,
  spring: {damping: 18, stiffness: 140},
} as const;
