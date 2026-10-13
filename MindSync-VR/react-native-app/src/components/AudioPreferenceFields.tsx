import React from 'react';
import {Text, View} from 'react-native';
import type {AudioControlPreferences} from '../types/domain';
import {AUDIO_INSTRUMENTS} from '../services/preferences/audioPreferences';
import {Card, ChoiceChip, PreferenceSlider, uiStyles} from './ui';

type Props = {value: AudioControlPreferences; onChange: (value: AudioControlPreferences) => void};

export function AudioPreferenceFields({value, onChange}: Props) {
  const change = <K extends keyof AudioControlPreferences>(key: K, next: AudioControlPreferences[K]) =>
    onChange({...value, [key]: next});
  const choices = <K extends keyof AudioControlPreferences>(
    key: K, title: string, options: ReadonlyArray<readonly [AudioControlPreferences[K], string]>,
  ) => <View><Text style={uiStyles.label}>{title}</Text><View style={uiStyles.wrap}>
    {options.map(([option, label]) => <ChoiceChip key={String(option)} label={label}
      selected={value[key] === option} onPress={() => change(key, option)} />)}
  </View></View>;
  return <>
    <Card>
      <Text style={uiStyles.value}>Your meditation sound</Text>
      <Text style={uiStyles.body}>Music will match the selected Japanese Temple Pond Garden. These choices personalize the meditation music, not the scene's fixed ambient recording. Music is instrumental.</Text>
      <Text style={uiStyles.label}>Instruments (choose at least one)</Text>
      <View style={uiStyles.wrap}>{AUDIO_INSTRUMENTS.map(instrument => <ChoiceChip key={instrument}
        label={instrument.charAt(0).toUpperCase() + instrument.slice(1)}
        selected={value.preferredInstruments.includes(instrument)} onPress={() => {
          const selected = value.preferredInstruments;
          if (selected.includes(instrument) && selected.length === 1) return;
          change('preferredInstruments', selected.includes(instrument)
            ? selected.filter(item => item !== instrument) : [...selected, instrument]);
        }} />)}</View>
      {choices('mood', 'Musical mood', [['calm', 'Calm'], ['focused', 'Focused'], ['sleepy', 'Sleepy'], ['energized', 'Energized']])}
      {choices('tempoPreference', 'Pacing', [['slow', 'Slow'], ['medium', 'Medium'], ['fast', 'Fast']])}
      <PreferenceSlider label="Musical intensity" value={value.audioIntensity} leftLabel="Soft / Sparse" rightLabel="Fuller / Stronger" onChange={next => change('audioIntensity', next)} />
    </Card>
    <Card>
      <Text style={uiStyles.value}>Balance and character</Text>
      {choices('ambientMusicBalance', 'Ambient and meditation balance', [['mostly_ambience', 'Mostly ambience'], ['balanced', 'Balanced'], ['mostly_music', 'Mostly music']])}
      {choices('brightnessPreference', 'Brightness', [['soft_dark', 'Soft / Dark'], ['neutral', 'Neutral'], ['bright_clear', 'Bright / Clear']])}
      {choices('rhythmPreference', 'Rhythm', [['minimal', 'Minimal'], ['gentle_pulse', 'Gentle pulse'], ['more_motion', 'More motion']])}
      {choices('natureSoundPreference', 'Nature ambience prominence', [['none', 'None'], ['low', 'Low'], ['medium', 'Medium'], ['high', 'High']])}
      <Text style={uiStyles.body}>The selected garden keeps its fixed ambient recording. This choice guides its target prominence within the safe mix.</Text>
      {choices('reverbPreference', 'Spaciousness', [['dry', 'Dry'], ['balanced', 'Balanced'], ['spacious', 'Spacious']])}
    </Card>
    <Card>
      <Text style={uiStyles.value}>Sound comfort</Text>
      {choices('volumePreference', 'Preferred listening level', [['low', 'Low'], ['medium', 'Medium'], ['high', 'High']])}
      <Text style={uiStyles.body}>This is a preference, not a measured sound-pressure level. Headset volume remains independently adjustable.</Text>
      <PreferenceSlider label="Subtle variation tolerance" value={value.noveltyTolerance} leftLabel="Very Familiar" rightLabel="More Variation" onChange={next => change('noveltyTolerance', next)} />
      {choices('avoidDissonance', 'Harmony', [[true, 'Avoid tension'], [false, 'Mild tension allowed']])}
    </Card>
  </>;
}
