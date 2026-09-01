export type Json =
  | string
  | number
  | boolean
  | null
  | {[key: string]: Json | undefined}
  | Json[];

export type Database = {
  public: {
    Tables: {
      profiles: {
        Row: {
          id: string;
          email: string;
          display_name: string;
          role: 'participant' | 'clinician' | 'researcher';
          onboarding_complete: boolean;
          preferred_language: string;
          created_at: string;
          updated_at: string;
        };
        Insert: {
          id: string;
          email: string;
          display_name: string;
          role?: 'participant' | 'clinician' | 'researcher';
          onboarding_complete?: boolean;
          preferred_language?: string;
          created_at?: string;
          updated_at?: string;
        };
        Update: Partial<Database['public']['Tables']['profiles']['Insert']>;
        Relationships: [];
      };
      onboarding_profiles: {
        Row: {
          user_id: string;
          age_range: string;
          meditation_experience: string;
          preferred_duration: number;
          goals: string[];
          meditation_style: string;
          audio_preferences: string[];
          environment_preferences: string[];
          sensitivities: string[];
          consent_accepted: boolean;
          research_consent: boolean;
          privacy_notice_version: string;
          consented_at: string | null;
          updated_at: string;
        };
        Insert: {
          user_id: string;
          age_range?: string;
          meditation_experience?: string;
          preferred_duration?: number;
          goals?: string[];
          meditation_style?: string;
          audio_preferences?: string[];
          environment_preferences?: string[];
          sensitivities?: string[];
          consent_accepted?: boolean;
          research_consent?: boolean;
          privacy_notice_version?: string;
          consented_at?: string | null;
          updated_at?: string;
        };
        Update: Partial<Database['public']['Tables']['onboarding_profiles']['Insert']>;
        Relationships: [];
      };
      participant_consents: {
        Row: {
          id: string;
          user_id: string;
          consent_type: 'privacy_notice' | 'research_participation';
          document_version: string;
          granted: boolean;
          recorded_at: string;
        };
        Insert: {
          id?: string;
          user_id: string;
          consent_type: 'privacy_notice' | 'research_participation';
          document_version: string;
          granted: boolean;
          recorded_at?: string;
        };
        Update: never;
        Relationships: [];
      };
      meditation_sessions: {
        Row: {
          id: string;
          user_id: string;
          title: string;
          session_date: string;
          duration_minutes: number;
          environment: string;
          audio_profile: string;
          completion_rate: number;
          mood_before: number;
          mood_after: number;
          validation_complete: boolean;
          status: string;
          created_at: string;
          updated_at: string;
        };
        Insert: {
          id: string;
          user_id: string;
          title: string;
          session_date: string;
          duration_minutes: number;
          environment: string;
          audio_profile: string;
          completion_rate?: number;
          mood_before?: number;
          mood_after?: number;
          validation_complete?: boolean;
          status?: string;
          created_at?: string;
          updated_at?: string;
        };
        Update: Partial<Database['public']['Tables']['meditation_sessions']['Insert']>;
        Relationships: [];
      };
      questionnaire_submissions: {
        Row: {
          id: string;
          user_id: string;
          template_id: string;
          session_id: string | null;
          submitted_at: string;
          export_shape_version: string;
          answers: Json;
          created_at: string;
          updated_at: string;
        };
        Insert: {
          id: string;
          user_id: string;
          template_id: string;
          session_id?: string | null;
          submitted_at: string;
          export_shape_version: string;
          answers: Json;
          created_at?: string;
          updated_at?: string;
        };
        Update: Partial<Database['public']['Tables']['questionnaire_submissions']['Insert']>;
        Relationships: [];
      };
      wearable_devices: {
        Row: {
          id: string;
          user_id: string;
          device_identifier: string;
          display_name: string;
          firmware: string | null;
          last_connected_at: string;
          created_at: string;
          updated_at: string;
        };
        Insert: {
          id?: string;
          user_id: string;
          device_identifier: string;
          display_name: string;
          firmware?: string | null;
          last_connected_at?: string;
          created_at?: string;
          updated_at?: string;
        };
        Update: Partial<Database['public']['Tables']['wearable_devices']['Insert']>;
        Relationships: [];
      };
      complete_session_records: {
        Row: {
          record_id: string;
          user_id: string;
          session_id: string;
          schema_version: string;
          started_at: string;
          completed_at: string;
          record: Json;
          created_at: string;
          updated_at: string;
        };
        Insert: {
          record_id: string;
          user_id: string;
          session_id: string;
          schema_version: string;
          started_at: string;
          completed_at: string;
          record: Json;
          created_at?: string;
          updated_at?: string;
        };
        Update: Partial<Database['public']['Tables']['complete_session_records']['Insert']>;
        Relationships: [];
      };
    };
    Views: Record<string, never>;
    Functions: Record<string, never>;
    Enums: Record<string, never>;
    CompositeTypes: Record<string, never>;
  };
};
