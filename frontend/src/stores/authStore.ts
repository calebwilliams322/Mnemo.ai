import { create } from 'zustand';
import type { Session } from '@supabase/supabase-js';
import { supabase, onAuthStateChange } from '../lib/supabase';
import { getProfile } from '../api/auth';
import type { UserProfile } from '../api/types';
import { connectSignalR, disconnectSignalR } from '../lib/signalr';

interface AuthState {
  user: UserProfile | null;
  session: Session | null;
  isLoading: boolean;
  isAuthenticated: boolean;
  initialize: () => Promise<void>;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  refreshProfile: () => Promise<void>;
}

export const useAuthStore = create<AuthState>((set, get) => ({
  user: null,
  session: null,
  isLoading: true,
  isAuthenticated: false,

  initialize: async () => {
    set({ isLoading: true });

    try {
      const { data: { session } } = await supabase.auth.getSession();

      if (session) {
        try {
          const profile = await getProfile();
          await connectSignalR();
          set({ session, user: profile, isAuthenticated: true, isLoading: false });
        } catch (error) {
          console.error('Failed to get profile:', error);
          await supabase.auth.signOut();
          set({ session: null, user: null, isAuthenticated: false, isLoading: false });
        }
      } else {
        set({ isLoading: false });
      }

      // Listen for auth state changes
      onAuthStateChange(async (newSession) => {
        const currentState = get();

        if (newSession && !currentState.isAuthenticated) {
          try {
            const profile = await getProfile();
            await connectSignalR();
            set({ session: newSession, user: profile, isAuthenticated: true });
          } catch (error) {
            console.error('Failed to get profile on auth change:', error);
            set({ session: null, user: null, isAuthenticated: false });
          }
        } else if (!newSession && currentState.isAuthenticated) {
          await disconnectSignalR();
          set({ session: null, user: null, isAuthenticated: false });
        }
      });
    } catch (error) {
      console.error('Failed to initialize auth:', error);
      set({ isLoading: false });
    }
  },

  login: async (email: string, password: string) => {
    const { data, error } = await supabase.auth.signInWithPassword({ email, password });
    if (error) throw error;

    const profile = await getProfile();
    // SignalR is optional - don't fail login if it doesn't connect
    try {
      await connectSignalR();
    } catch (e) {
      console.warn('SignalR connection failed, continuing without real-time updates:', e);
    }
    set({ session: data.session, user: profile, isAuthenticated: true });
  },

  logout: async () => {
    await disconnectSignalR();
    await supabase.auth.signOut();
    set({ session: null, user: null, isAuthenticated: false });
  },

  refreshProfile: async () => {
    const profile = await getProfile();
    set({ user: profile });
  },
}));
