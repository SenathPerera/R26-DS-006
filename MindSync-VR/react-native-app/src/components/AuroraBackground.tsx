// The background behind every reworked screen. A deep base plus two large,
// soft radial "aurora" blooms (via SVG radial gradients — no native blur lib,
// so it stays reload-only) positioned differently per stage, so each stage
// feels distinct instead of the same flat blue. A faint vignette adds depth.

import React, {ReactNode} from 'react';
import {Dimensions, StyleSheet, View} from 'react-native';
import {SafeAreaView} from 'react-native-safe-area-context';
import Svg, {Defs, RadialGradient, Rect, Stop, Circle} from 'react-native-svg';
import {aurora, palette} from '../theme/design';

export type AuroraVariant = 'intro' | 'room' | 'pre' | 'vr' | 'post' | 'report';

const {width: W, height: H} = Dimensions.get('window');

// Blob anchor points per stage (fractions of the screen), so the light moves.
const LAYOUT: Record<AuroraVariant, {a: [number, number]; b: [number, number]}> = {
  intro: {a: [0.2, 0.15], b: [0.9, 0.55]},
  room: {a: [0.85, 0.12], b: [0.15, 0.6]},
  pre: {a: [0.25, 0.1], b: [0.8, 0.7]},
  vr: {a: [0.5, 0.25], b: [0.5, 0.85]},
  post: {a: [0.8, 0.15], b: [0.2, 0.65]},
  report: {a: [0.15, 0.1], b: [0.9, 0.45]},
};

export function AuroraBackground({children, variant = 'intro', safe = true}: {children: ReactNode; variant?: AuroraVariant; safe?: boolean}) {
  const l = LAYOUT[variant];
  const inner = safe ? <SafeAreaView style={styles.fill} edges={['top', 'left', 'right']}>{children}</SafeAreaView> : children;
  return (
    <View style={styles.root}>
      <Svg width={W} height={H} style={StyleSheet.absoluteFill}>
        <Defs>
          <RadialGradient id="blobA" cx="50%" cy="50%" r="50%">
            <Stop offset="0%" stopColor={aurora.a.color} stopOpacity={aurora.a.opacity} />
            <Stop offset="100%" stopColor={aurora.a.color} stopOpacity={0} />
          </RadialGradient>
          <RadialGradient id="blobB" cx="50%" cy="50%" r="50%">
            <Stop offset="0%" stopColor={aurora.b.color} stopOpacity={aurora.b.opacity} />
            <Stop offset="100%" stopColor={aurora.b.color} stopOpacity={0} />
          </RadialGradient>
          <RadialGradient id="vignette" cx="50%" cy="42%" r="75%">
            <Stop offset="55%" stopColor="#000000" stopOpacity={0} />
            <Stop offset="100%" stopColor="#000000" stopOpacity={0.45} />
          </RadialGradient>
        </Defs>
        <Rect x={0} y={0} width={W} height={H} fill={palette.bg800} />
        <Circle cx={W * l.a[0]} cy={H * l.a[1]} r={aurora.a.size} fill="url(#blobA)" />
        <Circle cx={W * l.b[0]} cy={H * l.b[1]} r={aurora.b.size} fill="url(#blobB)" />
        {/* a second, tighter aqua bloom for a touch of Sarah's colour */}
        <Circle cx={W * l.b[0]} cy={H * l.b[1]} r={aurora.b.size * 0.5} fill="url(#blobB)" opacity={0.5} />
        <Rect x={0} y={0} width={W} height={H} fill="url(#vignette)" />
      </Svg>
      {inner}
    </View>
  );
}

const styles = StyleSheet.create({
  root: {flex: 1, backgroundColor: palette.bg900},
  fill: {flex: 1},
});
