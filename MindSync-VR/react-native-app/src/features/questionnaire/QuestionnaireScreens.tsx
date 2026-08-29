import React, {useMemo, useState} from 'react';
import {Text, View} from 'react-native';
import {CheckCircle2, ClipboardCheck} from 'lucide-react-native';
import {Card, ChoiceChip, Field, Header, PrimaryButton, Screen, SecondaryButton, SectionHeader, StatusPill, uiStyles} from '../../components/ui';
import {questionnaireTemplates, useMindSyncStore} from '../../store/useMindSyncStore';
import {QuestionnaireQuestion} from '../../types/domain';
import {colors} from '../../theme/theme';

export function QuestionnaireHomeScreen({navigation}: any) {
  const pending = useMindSyncStore(state => state.pendingValidationCount);
  const submissions = useMindSyncStore(state => state.questionnaireSubmissions);
  return (
    <Screen>
      <Header title="Validation" subtitle="Private, non-judgmental check-ins linked to the research session." />
      <Card><View style={uiStyles.rowBetween}><View style={{flex: 1}}><Text style={uiStyles.value}>Component D</Text><Text style={uiStyles.body}>{pending ? 'A post-session reflection is ready.' : 'You are up to date.'}</Text></View><StatusPill label={`${pending} pending`} tone={pending ? 'warning' : 'good'} /></View>{pending ? <PrimaryButton label="Start validation" icon={ClipboardCheck} onPress={() => navigation.navigate('QuestionnaireForm', {templateId: 'component-d-post-v1'})} /> : null}</Card>
      <SectionHeader title="Available assessments" />
      {questionnaireTemplates.map(template => <Card key={template.id}><Text style={uiStyles.value}>{template.title}</Text><Text style={uiStyles.body}>{template.description}</Text><Text style={uiStyles.label}>Version {template.version} · Component {template.component}</Text><SecondaryButton label="Open" onPress={() => navigation.navigate('QuestionnaireForm', {templateId: template.id})} /></Card>)}
      <SectionHeader title="Completion history" />
      {submissions.length ? submissions.map(item => <Card key={item.id}><View style={uiStyles.row}><CheckCircle2 color={colors.green} /><View><Text style={uiStyles.value}>{item.templateId}</Text><Text style={uiStyles.label}>{new Date(item.submittedAt).toLocaleString()} · {item.synced ? 'Synced' : 'Saved locally for sync'}</Text></View></View></Card>) : <Card><Text style={uiStyles.body}>Completed evaluations will appear here, including offline sync state.</Text></Card>}
    </Screen>
  );
}

export function QuestionnaireFormScreen({navigation, route}: any) {
  const template = questionnaireTemplates.find(item => item.id === route.params?.templateId) ?? questionnaireTemplates[0];
  const activeSession = useMindSyncStore(state => state.activeSession);
  const submit = useMindSyncStore(state => state.submitQuestionnaire);
  const [answers, setAnswers] = useState<Record<string, string | number | string[]>>({});
  const [index, setIndex] = useState(0);
  const visible = useMemo(() => template.questions.filter(question => branchVisible(question, answers)), [template.questions, answers]);
  const question = visible[index] ?? visible[visible.length - 1];
  const value = answers[question.id];
  const valid = !question.required || value !== undefined && value !== '';
  const finish = () => { submit(template.id, activeSession?.id ?? null, answers); navigation.navigate('MainTabs', {screen: 'Validate'}); };
  return (
    <Screen>
      <Header title={template.title} subtitle={`Question ${index + 1} of ${visible.length}`} onBack={navigation.goBack} />
      <View style={{height: 5, backgroundColor: colors.borderSoft, borderRadius: 4}}><View style={{height: 5, width: `${((index + 1) / visible.length) * 100}%`, backgroundColor: colors.teal, borderRadius: 4}} /></View>
      <Card>
        <Text style={uiStyles.value}>{question.prompt}</Text>
        {question.helperText ? <Text style={uiStyles.body}>{question.helperText}</Text> : null}
        <QuestionInput question={question} value={value} onChange={next => setAnswers(current => ({...current, [question.id]: next}))} />
      </Card>
      <View style={uiStyles.row}>
        {index > 0 ? <View style={{flex: 1}}><SecondaryButton label="Back" onPress={() => setIndex(value => value - 1)} /></View> : null}
        <View style={{flex: 1}}><PrimaryButton label={index === visible.length - 1 ? 'Submit securely' : 'Continue'} disabled={!valid} onPress={index === visible.length - 1 ? finish : () => setIndex(value => value + 1)} /></View>
      </View>
      <Text style={[uiStyles.label, {textAlign: 'center'}]}>Responses are stored locally when offline and retain an export-ready Component D schema.</Text>
    </Screen>
  );
}

function QuestionInput({question, value, onChange}: {question: QuestionnaireQuestion; value: unknown; onChange: (value: string | number | string[]) => void}) {
  if (question.type === 'text') return <Field label="Your response" multiline value={typeof value === 'string' ? value : ''} onChangeText={onChange} />;
  if (question.type === 'single') return <View style={uiStyles.wrap}>{question.options?.map(option => <ChoiceChip key={option} label={option} selected={value === option} onPress={() => onChange(option)} />)}</View>;
  const min = question.min ?? 1;
  const max = question.max ?? 7;
  if (question.type === 'likert' || question.type === 'slider' || question.type === 'numeric') return <View style={uiStyles.wrap}>{Array.from({length: max - min + 1}, (_, i) => i + min).map(option => <ChoiceChip key={option} label={String(option)} selected={value === option} onPress={() => onChange(option)} />)}</View>;
  return <Text style={uiStyles.body}>This response type will be available in a future study template.</Text>;
}

function branchVisible(question: QuestionnaireQuestion, answers: Record<string, unknown>) {
  if (!question.branch) return true;
  const source = answers[question.branch.whenQuestionId];
  if (question.branch.equals !== undefined) return source === question.branch.equals;
  if (question.branch.includes !== undefined) return typeof source === 'string' && ['Moderate', 'Severe'].includes(source);
  return true;
}
