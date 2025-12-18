import { create } from 'zustand';

export type NotificationType = 'success' | 'error' | 'warning' | 'info';

export interface Notification {
  id: string;
  type: NotificationType;
  title: string;
  message?: string;
  duration?: number;
}

interface NotificationState {
  notifications: Notification[];
  addNotification: (notification: Omit<Notification, 'id'>) => void;
  removeNotification: (id: string) => void;
  clearNotifications: () => void;
}

let notificationId = 0;

export const useNotificationStore = create<NotificationState>((set) => ({
  notifications: [],

  addNotification: (notification) => {
    const id = `notification-${++notificationId}`;
    const duration = notification.duration ?? 5000;
    const newNotification: Notification = {
      ...notification,
      id,
      duration,
    };

    set((state) => ({
      notifications: [...state.notifications, newNotification],
    }));

    // Auto-remove after duration
    if (duration > 0) {
      setTimeout(() => {
        set((state) => ({
          notifications: state.notifications.filter((n) => n.id !== id),
        }));
      }, duration);
    }
  },

  removeNotification: (id) => {
    set((state) => ({
      notifications: state.notifications.filter((n) => n.id !== id),
    }));
  },

  clearNotifications: () => {
    set({ notifications: [] });
  },
}));

// Helper functions for easy access
export const notify = {
  success: (title: string, message?: string) =>
    useNotificationStore.getState().addNotification({ type: 'success', title, message }),
  error: (title: string, message?: string) =>
    useNotificationStore.getState().addNotification({ type: 'error', title, message }),
  warning: (title: string, message?: string) =>
    useNotificationStore.getState().addNotification({ type: 'warning', title, message }),
  info: (title: string, message?: string) =>
    useNotificationStore.getState().addNotification({ type: 'info', title, message }),
};
