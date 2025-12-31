import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useAuthStore } from '../stores/authStore';
import { updatePassword } from '../lib/supabase';
import { apiClient } from '../api/client';
import { Button, Card } from '../components/common';
import { notify } from '../stores/notificationStore';
import { UserCircleIcon, KeyIcon } from '@heroicons/react/24/outline';

const passwordSchema = z.object({
  newPassword: z.string().min(12, 'Password must be at least 12 characters'),
  confirmPassword: z.string(),
}).refine((data) => data.newPassword === data.confirmPassword, {
  message: "Passwords don't match",
  path: ['confirmPassword'],
});

const profileSchema = z.object({
  name: z.string().min(1, 'Name is required'),
});

type PasswordFormData = z.infer<typeof passwordSchema>;
type ProfileFormData = z.infer<typeof profileSchema>;

export function SettingsPage() {
  const { user, refreshProfile } = useAuthStore();
  const [passwordSuccess, setPasswordSuccess] = useState(false);

  const passwordForm = useForm<PasswordFormData>({
    resolver: zodResolver(passwordSchema),
  });

  const profileForm = useForm<ProfileFormData>({
    resolver: zodResolver(profileSchema),
    defaultValues: {
      name: user?.name || '',
    },
  });

  const onPasswordSubmit = async (data: PasswordFormData) => {
    try {
      await updatePassword(data.newPassword);
      setPasswordSuccess(true);
      passwordForm.reset();
      notify.success('Password updated', 'Your password has been changed successfully');
      setTimeout(() => setPasswordSuccess(false), 3000);
    } catch (err) {
      notify.error('Error', err instanceof Error ? err.message : 'Failed to update password');
    }
  };

  const onProfileSubmit = async (data: ProfileFormData) => {
    try {
      await apiClient.patch('/me', { name: data.name });
      await refreshProfile();
      notify.success('Profile updated', 'Your name has been updated');
    } catch (err) {
      notify.error('Error', err instanceof Error ? err.message : 'Failed to update profile');
    }
  };

  return (
    <div className="max-w-2xl mx-auto space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Settings</h1>
        <p className="text-gray-600">Manage your account settings</p>
      </div>

      {/* Profile Section */}
      <Card>
        <div className="flex items-center gap-3 mb-6">
          <UserCircleIcon className="h-6 w-6 text-gray-400" />
          <h2 className="text-lg font-semibold text-gray-900">Profile</h2>
        </div>

        <div className="mb-6 p-4 bg-gray-50 rounded-lg">
          <div className="grid grid-cols-2 gap-4 text-sm">
            <div>
              <span className="text-gray-500">Email</span>
              <p className="font-medium text-gray-900">{user?.email}</p>
            </div>
            <div>
              <span className="text-gray-500">Role</span>
              <p className="font-medium text-gray-900">{user?.role}</p>
            </div>
            <div>
              <span className="text-gray-500">Organization</span>
              <p className="font-medium text-gray-900">{user?.tenantName}</p>
            </div>
          </div>
        </div>

        <form onSubmit={profileForm.handleSubmit(onProfileSubmit)} className="space-y-4">
          <div>
            <label htmlFor="name" className="block text-sm font-medium text-gray-700">
              Display Name
            </label>
            <input
              {...profileForm.register('name')}
              id="name"
              type="text"
              className="mt-1 block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm placeholder-gray-400 focus:outline-none focus:ring-primary-500 focus:border-primary-500 sm:text-sm"
            />
            {profileForm.formState.errors.name && (
              <p className="mt-1 text-sm text-red-600">{profileForm.formState.errors.name.message}</p>
            )}
          </div>

          <div className="flex justify-end">
            <Button type="submit" isLoading={profileForm.formState.isSubmitting}>
              Update Profile
            </Button>
          </div>
        </form>
      </Card>

      {/* Password Section */}
      <Card>
        <div className="flex items-center gap-3 mb-6">
          <KeyIcon className="h-6 w-6 text-gray-400" />
          <h2 className="text-lg font-semibold text-gray-900">Change Password</h2>
        </div>

        {passwordSuccess && (
          <div className="mb-4 bg-green-50 border border-green-200 text-green-700 px-4 py-3 rounded-md text-sm">
            Password updated successfully!
          </div>
        )}

        <form onSubmit={passwordForm.handleSubmit(onPasswordSubmit)} className="space-y-4">
          <div>
            <label htmlFor="newPassword" className="block text-sm font-medium text-gray-700">
              New Password
            </label>
            <input
              {...passwordForm.register('newPassword')}
              id="newPassword"
              type="password"
              autoComplete="new-password"
              className="mt-1 block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm placeholder-gray-400 focus:outline-none focus:ring-primary-500 focus:border-primary-500 sm:text-sm"
              placeholder="Minimum 12 characters"
            />
            {passwordForm.formState.errors.newPassword && (
              <p className="mt-1 text-sm text-red-600">{passwordForm.formState.errors.newPassword.message}</p>
            )}
          </div>

          <div>
            <label htmlFor="confirmPassword" className="block text-sm font-medium text-gray-700">
              Confirm New Password
            </label>
            <input
              {...passwordForm.register('confirmPassword')}
              id="confirmPassword"
              type="password"
              autoComplete="new-password"
              className="mt-1 block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm placeholder-gray-400 focus:outline-none focus:ring-primary-500 focus:border-primary-500 sm:text-sm"
              placeholder="Re-enter your new password"
            />
            {passwordForm.formState.errors.confirmPassword && (
              <p className="mt-1 text-sm text-red-600">{passwordForm.formState.errors.confirmPassword.message}</p>
            )}
          </div>

          <div className="flex justify-end">
            <Button type="submit" isLoading={passwordForm.formState.isSubmitting}>
              Change Password
            </Button>
          </div>
        </form>
      </Card>
    </div>
  );
}
