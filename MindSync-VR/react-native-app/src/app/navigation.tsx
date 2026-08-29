import React from 'react';
import {NavigationContainer, DarkTheme} from '@react-navigation/native';
import {createNativeStackNavigator} from '@react-navigation/native-stack';
import {createBottomTabNavigator} from '@react-navigation/bottom-tabs';
import {BarChart3, ClipboardCheck, Home, Settings} from 'lucide-react-native';
import {WelcomeScreen, LoginScreen, SignUpScreen} from '../features/auth/AuthScreens';
import {OnboardingScreen} from '../features/onboarding/OnboardingScreen';
import {HomeScreen} from '../features/dashboard/HomeScreen';
import {WearableDetailScreen, WearableScreen} from '../features/wearable/WearableScreens';
import {VrScreen} from '../features/vr/VrScreen';
import {LiveSessionScreen, PreSessionScreen, SessionCompleteScreen} from '../features/session/SessionScreens';
import {QuestionnaireFormScreen, QuestionnaireHomeScreen} from '../features/questionnaire/QuestionnaireScreens';
import {AnalyticsScreen} from '../features/analytics/AnalyticsScreen';
import {SettingsScreen} from '../features/settings/SettingsScreen';
import {VoiceCheckInScreen} from '../features/voice/VoiceCheckInScreen';
import {colors} from '../theme/theme';

export type RootStackParamList = {
  Welcome: undefined;
  Login: undefined;
  SignUp: undefined;
  Onboarding: undefined;
  MainTabs: {screen?: keyof MainTabParamList} | undefined;
  Wearable: undefined;
  WearableDetail: undefined;
  VR: undefined;
  PreSession: undefined;
  LiveSession: undefined;
  SessionComplete: undefined;
  VoiceCheckIn: undefined;
  QuestionnaireForm: {templateId: string};
};

export type MainTabParamList = {
  Home: undefined;
  Validate: undefined;
  Trends: undefined;
  Settings: undefined;
};

const Stack = createNativeStackNavigator<RootStackParamList>();
const Tabs = createBottomTabNavigator<MainTabParamList>();

const navigationTheme = {
  ...DarkTheme,
  colors: {...DarkTheme.colors, background: colors.midnight, card: colors.deep, border: colors.borderSoft, text: colors.text, primary: colors.teal},
};

function MainTabs() {
  return (
    <Tabs.Navigator
      screenOptions={({route}) => ({
        headerShown: false,
        tabBarActiveTintColor: colors.teal,
        tabBarInactiveTintColor: colors.faint,
        tabBarStyle: {position: 'absolute', height: 76, paddingTop: 8, paddingBottom: 10, backgroundColor: '#081425F4', borderTopColor: colors.borderSoft},
        tabBarLabelStyle: {fontSize: 11, fontWeight: '700'},
        tabBarIcon: ({color, size}) => {
          const Icon = route.name === 'Home' ? Home : route.name === 'Validate' ? ClipboardCheck : route.name === 'Trends' ? BarChart3 : Settings;
          return <Icon color={color} size={size} />;
        },
      })}>
      <Tabs.Screen name="Home" component={HomeScreen} />
      <Tabs.Screen name="Validate" component={QuestionnaireHomeScreen} />
      <Tabs.Screen name="Trends" component={AnalyticsScreen} />
      <Tabs.Screen name="Settings" component={SettingsScreen} />
    </Tabs.Navigator>
  );
}

export function AppNavigation() {
  return (
    <NavigationContainer theme={navigationTheme}>
      <Stack.Navigator initialRouteName="Welcome" screenOptions={{headerShown: false, animation: 'fade_from_bottom', contentStyle: {backgroundColor: colors.midnight}}}>
        <Stack.Screen name="Welcome" component={WelcomeScreen} />
        <Stack.Screen name="Login" component={LoginScreen} />
        <Stack.Screen name="SignUp" component={SignUpScreen} />
        <Stack.Screen name="Onboarding" component={OnboardingScreen} />
        <Stack.Screen name="MainTabs" component={MainTabs} options={{animation: 'fade'}} />
        <Stack.Screen name="Wearable" component={WearableScreen} />
        <Stack.Screen name="WearableDetail" component={WearableDetailScreen} />
        <Stack.Screen name="VR" component={VrScreen} />
        <Stack.Screen name="PreSession" component={PreSessionScreen} />
        <Stack.Screen name="LiveSession" component={LiveSessionScreen} options={{gestureEnabled: false}} />
        <Stack.Screen name="SessionComplete" component={SessionCompleteScreen} />
        <Stack.Screen name="VoiceCheckIn" component={VoiceCheckInScreen} />
        <Stack.Screen name="QuestionnaireForm" component={QuestionnaireFormScreen} />
      </Stack.Navigator>
    </NavigationContainer>
  );
}
