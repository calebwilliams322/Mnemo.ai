import { useEffect, useState } from 'react';
import { Navigate } from 'react-router-dom';
import { getAdminStatus } from '../../api/admin';
import { FullPageLoader } from '../common';

interface AdminRouteProps {
  children: React.ReactNode;
}

export function AdminRoute({ children }: AdminRouteProps) {
  const [isLoading, setIsLoading] = useState(true);
  const [isAdmin, setIsAdmin] = useState(false);

  useEffect(() => {
    checkAdminStatus();
  }, []);

  async function checkAdminStatus() {
    try {
      const status = await getAdminStatus();
      setIsAdmin(status.isSuperAdmin);
    } catch (error) {
      console.error('Failed to check admin status:', error);
      setIsAdmin(false);
    } finally {
      setIsLoading(false);
    }
  }

  if (isLoading) {
    return <FullPageLoader message="Checking permissions..." />;
  }

  if (!isAdmin) {
    return <Navigate to="/dashboard" replace />;
  }

  return <>{children}</>;
}
