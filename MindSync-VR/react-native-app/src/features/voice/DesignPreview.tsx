// TEMPORARY preview screen — shows the aurora depth and all four Sarah states in
// one shot for approval, before any screens are rebuilt. Remove once approved.

import React from 'react';
import {ScrollView, StyleSheet, Text, View} from 'react-native';
import {AuroraBackground} from '../../components/AuroraBackground';
import {SarahAvatar, SarahState} from './SarahAvatar';
import {elevation, palette, radius, space, type} from '../../theme/design';

const STATES: {state: SarahState; label: string; level: number}[] = [
  {state: 'idle', label: 'idle', level: 0},
  {state: 'listening', label: 'listening (pulls in)', level: 0.7},
  {state: 'speaking', label: 'speaking (pushes out)', level: 0.6},
  {state: 'thinking', label: 'thinking', level: 0},
];

export function DesignPreview() {
  return (
    <AuroraBackground variant="intro">
      <ScrollView contentContainerStyle={styles.content}>
        <Text style={styles.label}>DESIGN PREVIEW</Text>
        <Text style={styles.display}>Still water at night.</Text>
        <Text style={styles.body}>Depth from layered light, not borders. One focal point. No flat blue.</Text>

        <View style={styles.card}>
          <Text style={styles.h2}>Glass surface</Text>
          <Text style={styles.body}>Soft translucent fill, top-edge highlight, shadow — floating above the aurora.</Text>
        </View>

        <Text style={[styles.label, {marginTop: space.xl}]}>SARAH — FOUR STATES</Text>
        <View style={styles.grid}>
          {STATES.map(s => (
            <View key={s.state} style={styles.cell}>
              <SarahAvatar state={s.state} size={128} level={s.level} />
              <Text style={styles.caption}>{s.label}</Text>
            </View>
          ))}
        </View>

        <View style={styles.big}>
          <Text style={styles.metricXL}>8.9</Text>
          <Text style={styles.caption}>type scale — a 3× gap, top to bottom</Text>
        </View>
      </ScrollView>
    </AuroraBackground>
  );
}

const styles = StyleSheet.create({
  content: {padding: space.xl, paddingBottom: 80, gap: space.md},
  label: {...type.label, color: palette.textLow, textTransform: 'uppercase'},
  display: {...type.display, color: palette.textHi},
  h2: {...type.h2, color: palette.textHi},
  body: {...type.body, color: palette.textMid},
  caption: {...type.caption, color: palette.textLow, textAlign: 'center'},
  metricXL: {...type.metricXL, color: palette.aqua},
  card: {backgroundColor: palette.surface, borderRadius: radius.lg, padding: space.xl, gap: space.sm, borderTopWidth: 1, borderTopColor: 'rgba(255,255,255,0.12)', ...elevation.card, marginTop: space.md},
  grid: {flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'space-around', rowGap: space.xl, marginTop: space.md},
  cell: {width: '48%', alignItems: 'center', gap: space.sm},
  big: {alignItems: 'center', marginTop: space.xxl, gap: space.sm},
});
