import React from 'react';
import {Text, View} from 'react-native';
import Svg, {Circle, Line, Polyline} from 'react-native-svg';
import {Card, Header, Metric, Screen, SectionHeader, StatusPill, uiStyles} from '../../components/ui';
import {useMindSyncStore} from '../../store/useMindSyncStore';
import {colors} from '../../theme/theme';

export function AnalyticsScreen() {
  const sessions = useMindSyncStore(state => state.sessions);
  const averageChange = sessions.length ? Math.round(sessions.reduce((sum, item) => sum + item.moodAfter - item.moodBefore, 0) / sessions.length) : 0;
  return (
    <Screen>
      <Header title="Trends" subtitle="Session outcomes are reflective signals, not medical diagnoses." />
      <Card>
        <View style={uiStyles.row}><Metric label="sessions" value={sessions.length} accent={colors.teal} /><Metric label="avg mood change" value={`+${averageChange}`} accent={colors.green} /><Metric label="completion" value={`${Math.round(sessions.reduce((sum, item) => sum + item.completionRate, 0) / Math.max(1, sessions.length))}%`} /></View>
      </Card>
      <Card><Text style={uiStyles.value}>Mood before and after</Text><TrendChart values={sessions.map(item => item.moodAfter)} /><Text style={uiStyles.label}>Recent post-session self-ratings</Text></Card>
      <SectionHeader title="Session history" />
      {sessions.map(session => <Card key={session.id}><View style={uiStyles.rowBetween}><View style={{flex: 1}}><Text style={uiStyles.value}>{session.title}</Text><Text style={uiStyles.body}>{session.date} · {session.durationMinutes} min</Text></View><StatusPill label={session.validationComplete ? 'Validated' : 'Pending'} tone={session.validationComplete ? 'good' : 'warning'} /></View><Text style={uiStyles.label}>{session.environment} · {session.audioProfile} · Mood {session.moodBefore} → {session.moodAfter}</Text></Card>)}
    </Screen>
  );
}

function TrendChart({values}: {values: number[]}) {
  const width = 310;
  const height = 140;
  const ordered = [...values].reverse();
  const points = ordered.map((value, index) => `${24 + index * ((width - 48) / Math.max(1, ordered.length - 1))},${height - 22 - value * 10}`).join(' ');
  return <View style={{alignItems: 'center'}}><Svg width="100%" height={height} viewBox={`0 0 ${width} ${height}`}>{[1, 5, 9].map(value => <Line key={value} x1="20" x2={width - 20} y1={height - 22 - value * 10} y2={height - 22 - value * 10} stroke={colors.borderSoft} strokeWidth="1" />)}<Polyline points={points} fill="none" stroke={colors.teal} strokeWidth="4" strokeLinejoin="round" strokeLinecap="round" />{ordered.map((value, index) => <Circle key={index} cx={24 + index * ((width - 48) / Math.max(1, ordered.length - 1))} cy={height - 22 - value * 10} r="5" fill={colors.violet} />)}</Svg></View>;
}
