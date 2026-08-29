import {MeditationSession, QuestionnaireTemplate, UserProfile} from '../types/domain';

export const demoUser: UserProfile = {
  id: 'participant-demo', email: 'ari@mindsync.study', name: 'Ari', role: 'participant',
  onboardingComplete: true, preferredLanguage: 'English',
};

export const demoSessions: MeditationSession[] = [
  {id: 's-104', title: 'Ocean Dusk Reset', date: '2026-08-28', durationMinutes: 15, environment: 'Ocean', audioProfile: 'Warm pads', completionRate: 100, moodBefore: 5, moodAfter: 8, validationComplete: false},
  {id: 's-103', title: 'Forest Focus', date: '2026-08-25', durationMinutes: 12, environment: 'Forest', audioProfile: 'Nature heavy', completionRate: 100, moodBefore: 4, moodAfter: 7, validationComplete: true},
  {id: 's-102', title: 'Mountain Stillness', date: '2026-08-22', durationMinutes: 10, environment: 'Mountain', audioProfile: 'Soft drone', completionRate: 92, moodBefore: 6, moodAfter: 8, validationComplete: true},
];

export const questionnaireTemplates: QuestionnaireTemplate[] = [
  {
    id: 'component-d-post-v1', title: 'Post-session validation',
    description: 'A brief reflection linked to your latest meditation session.', component: 'D', version: '1.0.0',
    questions: [
      {id: 'relaxation', prompt: 'How relaxed do you feel now?', type: 'likert', required: true, min: 1, max: 7},
      {id: 'immersion', prompt: 'How immersed did you feel in the VR environment?', type: 'slider', required: true, min: 0, max: 10},
      {id: 'discomfort', prompt: 'Did you experience any discomfort?', type: 'single', required: true, options: ['No', 'Mild', 'Moderate', 'Severe']},
      {id: 'discomfort-note', prompt: 'Please describe what felt uncomfortable.', type: 'text', branch: {whenQuestionId: 'discomfort', includes: 'Moderate'}},
    ],
  },
  {
    id: 'pre-session-v1', title: 'Pre-session check-in', description: 'A private baseline for this session.', component: 'B', version: '1.0.0',
    questions: [
      {id: 'stress', prompt: 'How activated or stressed do you feel?', type: 'slider', required: true, min: 0, max: 10},
      {id: 'mood', prompt: 'How would you describe your mood?', type: 'single', required: true, options: ['Low', 'Uneasy', 'Neutral', 'Steady', 'Positive']},
    ],
  },
];
