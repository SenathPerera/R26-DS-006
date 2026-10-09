import React, {useState} from 'react';
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
const resetSchema = z.object({email: z.string().email('Enter a valid email')});
const updatePasswordSchema = z.object({
  password: z.string().min(6, 'Use at least 6 characters'),
  confirmPassword: z.string().min(6, 'Confirm your new password'),
}).refine(values => values.password === values.confirmPassword, {
  message: 'Passwords do not match',
  path: ['confirmPassword'],
});

export function WelcomeScreen({navigation}: any) {
  return (
    <Screen style={{justifyContent: 'space-between'}}>
      <View style={{alignItems: 'center', paddingTop: spacing.xl, gap: spacing.md}}>
        <BreathingVisual size={170} />
        <Text style={{fontSize: typography.display, color: colors.text, fontWeight: '900'}}>Laminar VR</Text>
      </View>
      <Card>
        <PrimaryButton label="Log in" icon={LogIn} onPress={() => navigation.navigate('Login')} />
        <SecondaryButton label="Create account" icon={UserPlus} onPress={() => navigation.navigate('SignUp')} />
      </Card>
    </Screen>
  );
}

export function LoginScreen({navigation}: any) {
  const login = useMindSyncStore(state => state.login);
  const configured = useMindSyncStore(state => state.supabaseConfigured);
  const authError = useMindSyncStore(state => state.authError);
  const authStatus = useMindSyncStore(state => state.authStatus);
  const {control, handleSubmit, setError, formState: {errors}} = useForm<z.infer<typeof loginSchema>>({resolver: zodResolver(loginSchema), defaultValues: {email: configured ? '' : 'ari@mindsync.study', password: configured ? '' : 'mindsync'}});
  const submit = handleSubmit(async values => {
    try {
      await login(values.email, values.password);
    } catch (error) {
      setError('root', {message: error instanceof Error ? error.message : 'Unable to sign in'});
    }
  });
  const busy = authStatus === 'authenticating';
  return (
    <Screen>
      <Header stacked title="Welcome back" subtitle="Continue to your private Laminar VR workspace." onBack={navigation.goBack} />
      <Card>
        <Controller control={control} name="email" render={({field: {onChange, value}}) => <Field label="Email" autoCapitalize="none" keyboardType="email-address" value={value} onChangeText={onChange} error={errors.email?.message} />} />
        <Controller control={control} name="password" render={({field: {onChange, value}}) => <Field label="Password" secureTextEntry autoCapitalize="none" autoCorrect={false} spellCheck={false} autoComplete="current-password" textContentType="password" value={value} onChangeText={onChange} error={errors.password?.message} />} />
        {errors.root?.message || authError ? <Text style={[uiStyles.label, {color: colors.rose}]}>{errors.root?.message ?? authError}</Text> : null}
        <PrimaryButton label={busy ? 'Signing in...' : 'Log in'} icon={LogIn} disabled={busy} onPress={submit} />
        <SecondaryButton label="Forgot password" onPress={() => navigation.navigate('ForgotPassword')} />
      </Card>
      {!configured ? <Text style={[uiStyles.label, {textAlign: 'center'}]}>Supabase is not configured in this build. Demo credentials are enabled.</Text> : null}
    </Screen>
  );
}

export function SignUpScreen({navigation}: any) {
  const signUp = useMindSyncStore(state => state.signUp);
  const authStatus = useMindSyncStore(state => state.authStatus);
  const {control, handleSubmit, setError, formState: {errors}} = useForm<z.infer<typeof signUpSchema>>({resolver: zodResolver(signUpSchema), defaultValues: {name: '', email: '', password: ''}});
  const submit = handleSubmit(async values => {
    try {
      const result = await signUp(values.name, values.email, values.password);
      if (result.emailConfirmationRequired) navigation.replace('Login');
    } catch (error) {
      setError('root', {message: error instanceof Error ? error.message : 'Unable to create the account'});
    }
  });
  const busy = authStatus === 'authenticating';
  return (
    <Screen>
      <Header
        stacked
        title={<>Welcome to <Text style={{color: colors.cyan}}>Laminar VR</Text></>}
        subtitle="Create an account to get started"
        onBack={navigation.goBack}
      />
      <Card>
        <Controller control={control} name="name" render={({field: {onChange, value}}) => <Field label="Username" value={value} onChangeText={onChange} error={errors.name?.message} />} />
        <Controller control={control} name="email" render={({field: {onChange, value}}) => <Field label="Email" autoCapitalize="none" keyboardType="email-address" value={value} onChangeText={onChange} error={errors.email?.message} />} />
        <Controller control={control} name="password" render={({field: {onChange, value}}) => <Field label="Password" secureTextEntry autoCapitalize="none" autoCorrect={false} spellCheck={false} autoComplete="new-password" textContentType="newPassword" value={value} onChangeText={onChange} error={errors.password?.message} />} />
        {errors.root?.message ? <Text style={[uiStyles.label, {color: colors.rose}]}>{errors.root.message}</Text> : null}
        <PrimaryButton label={busy ? 'Creating account...' : 'Continue'} disabled={busy} onPress={submit} />
      </Card>
    </Screen>
  );
}

export function ForgotPasswordScreen({navigation}: any) {
  const [sent, setSent] = useState(false);
  const sendPasswordReset = useMindSyncStore(state => state.sendPasswordReset);
  const configured = useMindSyncStore(state => state.supabaseConfigured);
  const {control, handleSubmit, setError, formState: {errors, isSubmitting}} = useForm<z.infer<typeof resetSchema>>({resolver: zodResolver(resetSchema), defaultValues: {email: ''}});
  const submit = handleSubmit(async values => {
    try {
      await sendPasswordReset(values.email);
      setSent(true);
    } catch (error) {
      setError('root', {message: error instanceof Error ? error.message : 'Unable to send the reset email'});
    }
  });
  return (
    <Screen>
      <Header title="Reset password" subtitle="We will send account recovery instructions to your email." onBack={navigation.goBack} />
      <Card>
        <Controller control={control} name="email" render={({field: {onChange, value}}) => <Field label="Email" autoCapitalize="none" keyboardType="email-address" value={value} onChangeText={onChange} error={errors.email?.message} />} />
        {sent ? <StatusPill label="Recovery email requested" tone="good" /> : null}
        {errors.root?.message ? <Text style={[uiStyles.label, {color: colors.rose}]}>{errors.root.message}</Text> : null}
        <PrimaryButton label={isSubmitting ? 'Sending...' : 'Send recovery email'} disabled={isSubmitting || !configured} onPress={submit} />
      </Card>
      {!configured ? <Text style={[uiStyles.label, {textAlign: 'center'}]}>Password recovery becomes available when Supabase is configured.</Text> : null}
    </Screen>
  );
}

export function UpdatePasswordScreen() {
  const recoveryStatus = useMindSyncStore(state => state.passwordRecoveryStatus);
  const recoveryError = useMindSyncStore(state => state.passwordRecoveryError);
  const updateRecoveredPassword = useMindSyncStore(state => state.updateRecoveredPassword);
  const clearPasswordRecovery = useMindSyncStore(state => state.clearPasswordRecovery);
  const {control, handleSubmit, setError, formState: {errors}} = useForm<z.infer<typeof updatePasswordSchema>>({
    resolver: zodResolver(updatePasswordSchema),
    defaultValues: {password: '', confirmPassword: ''},
  });
  const submit = handleSubmit(async values => {
    try {
      await updateRecoveredPassword(values.password);
    } catch (error) {
      setError('root', {message: error instanceof Error ? error.message : 'Unable to update your password'});
    }
  });

  if (recoveryStatus === 'opening') {
    return (
      <Screen>
        <Header title="Reset password" subtitle="Securely opening your recovery link." />
        <Card><StatusPill label="Verifying recovery link..." tone="neutral" /></Card>
      </Screen>
    );
  }

  if (recoveryStatus === 'error') {
    return (
      <Screen>
        <Header title="Recovery link unavailable" subtitle="This link could not be used to reset your password." />
        <Card>
          <StatusPill label="Recovery link failed" tone="danger" />
          <Text style={[uiStyles.body, {color: colors.rose}]}>{recoveryError}</Text>
          <SecondaryButton label="Back to login" onPress={clearPasswordRecovery} />
        </Card>
      </Screen>
    );
  }

  const busy = recoveryStatus === 'updating';
  return (
    <Screen>
      <Header title="Choose a new password" subtitle="Enter the new password you want to use for MindSync VR." />
      <Card>
        <Controller control={control} name="password" render={({field: {onChange, value}}) => <Field label="New password" secureTextEntry autoCapitalize="none" autoCorrect={false} spellCheck={false} autoComplete="new-password" textContentType="newPassword" value={value} onChangeText={onChange} error={errors.password?.message} />} />
        <Controller control={control} name="confirmPassword" render={({field: {onChange, value}}) => <Field label="Confirm new password" secureTextEntry autoCapitalize="none" autoCorrect={false} spellCheck={false} autoComplete="new-password" textContentType="newPassword" value={value} onChangeText={onChange} error={errors.confirmPassword?.message} />} />
        {errors.root?.message || recoveryError ? <Text style={[uiStyles.label, {color: colors.rose}]}>{errors.root?.message ?? recoveryError}</Text> : null}
        <PrimaryButton label={busy ? 'Updating password...' : 'Update password'} disabled={busy} onPress={submit} />
      </Card>
    </Screen>
  );
}
