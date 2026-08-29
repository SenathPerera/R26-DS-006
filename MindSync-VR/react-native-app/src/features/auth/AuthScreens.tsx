import React from 'react';
import {Controller, useForm} from 'react-hook-form';
import {zodResolver} from '@hookform/resolvers/zod';
import {Text, View} from 'react-native';
import {LogIn, UserPlus} from 'lucide-react-native';
import {z} from 'zod';
import {BreathingVisual, Card, Field, Header, PrimaryButton, Screen, SecondaryButton, StatusPill, uiStyles} from '../../components/ui';
import {colors, spacing, typography} from '../../theme/theme';
import {useMindSyncStore} from '../../store/useMindSyncStore';

const loginSchema = z.object({email: z.string().email('Enter a valid email'), password: z.string().min(6, 'Use at least 6 characters')});
const signUpSchema = loginSchema.extend({name: z.string().min(2, 'Tell us what to call you')});

export function WelcomeScreen({navigation}: any) {
  return (
    <Screen style={{justifyContent: 'space-between'}}>
      <View style={{alignItems: 'center', paddingTop: spacing.xl, gap: spacing.md}}>
        <BreathingVisual size={170} />
        <StatusPill label="Adaptive wellness system" />
        <Text style={{fontSize: typography.display, color: colors.text, fontWeight: '900'}}>MindSync VR</Text>
        <Text style={[uiStyles.body, {textAlign: 'center', maxWidth: 330}]}>A calm control hub for your wearable, adaptive VR environment, and post-session research validation.</Text>
      </View>
      <Card>
        <Text style={uiStyles.value}>Research-grade wellness control</Text>
        <Text style={uiStyles.body}>Prepare your devices, begin a supported session, and return for a private reflection.</Text>
        <PrimaryButton label="Log in" icon={LogIn} onPress={() => navigation.navigate('Login')} />
        <SecondaryButton label="Create account" icon={UserPlus} onPress={() => navigation.navigate('SignUp')} />
      </Card>
    </Screen>
  );
}

export function LoginScreen({navigation}: any) {
  const login = useMindSyncStore(state => state.loginDemo);
  const {control, handleSubmit, formState: {errors}} = useForm<z.infer<typeof loginSchema>>({resolver: zodResolver(loginSchema), defaultValues: {email: 'ari@mindsync.study', password: 'mindsync'}});
  const submit = handleSubmit(values => { login(values.email); navigation.reset({index: 0, routes: [{name: 'MainTabs'}]}); });
  return (
    <Screen>
      <Header title="Welcome back" subtitle="Continue to your private MindSync workspace." onBack={navigation.goBack} />
      <Card>
        <Controller control={control} name="email" render={({field: {onChange, value}}) => <Field label="Email" autoCapitalize="none" keyboardType="email-address" value={value} onChangeText={onChange} error={errors.email?.message} />} />
        <Controller control={control} name="password" render={({field: {onChange, value}}) => <Field label="Password" secureTextEntry value={value} onChangeText={onChange} error={errors.password?.message} />} />
        <PrimaryButton label="Log in" icon={LogIn} onPress={submit} />
      </Card>
      <Text style={[uiStyles.label, {textAlign: 'center'}]}>Demo credentials are prefilled. Real authentication plugs into services/api without changing screen state.</Text>
    </Screen>
  );
}

export function SignUpScreen({navigation}: any) {
  const signUp = useMindSyncStore(state => state.signUp);
  const {control, handleSubmit, formState: {errors}} = useForm<z.infer<typeof signUpSchema>>({resolver: zodResolver(signUpSchema), defaultValues: {name: '', email: '', password: ''}});
  const submit = handleSubmit(values => { signUp(values.name, values.email); navigation.replace('Onboarding'); });
  return (
    <Screen>
      <Header title="Create your account" subtitle="Your preferences remain editable and your optional answers can be skipped." onBack={navigation.goBack} />
      <Card>
        <Controller control={control} name="name" render={({field: {onChange, value}}) => <Field label="Preferred name" value={value} onChangeText={onChange} error={errors.name?.message} />} />
        <Controller control={control} name="email" render={({field: {onChange, value}}) => <Field label="Email" autoCapitalize="none" keyboardType="email-address" value={value} onChangeText={onChange} error={errors.email?.message} />} />
        <Controller control={control} name="password" render={({field: {onChange, value}}) => <Field label="Password" secureTextEntry value={value} onChangeText={onChange} error={errors.password?.message} />} />
        <PrimaryButton label="Continue" onPress={submit} />
      </Card>
    </Screen>
  );
}
