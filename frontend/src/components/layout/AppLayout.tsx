import { Outlet } from 'react-router-dom';
import { Navbar } from './Navbar';
import { Sidebar } from './Sidebar';
import { NotificationToast } from '../common/NotificationToast';

export function AppLayout() {
  return (
    <div className="h-full flex flex-col">
      <Navbar />
      <div className="flex-1 flex overflow-hidden">
        <Sidebar />
        <main className="flex-1 overflow-auto bg-gray-50 p-6">
          <Outlet />
        </main>
      </div>
      <NotificationToast />
    </div>
  );
}
