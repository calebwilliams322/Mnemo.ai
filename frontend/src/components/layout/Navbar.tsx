import { Fragment, useEffect, useState } from 'react';
import { Menu, MenuButton, MenuItem, MenuItems, Transition } from '@headlessui/react';
import { UserCircleIcon, ArrowRightOnRectangleIcon, Cog6ToothIcon } from '@heroicons/react/24/outline';
import { useAuthStore } from '../../stores/authStore';
import { Link } from 'react-router-dom';
import { getAdminStatus } from '../../api/admin';

export function Navbar() {
  const { user, logout } = useAuthStore();
  const [isAdmin, setIsAdmin] = useState(false);

  useEffect(() => {
    getAdminStatus()
      .then((status) => setIsAdmin(status.isSuperAdmin))
      .catch(() => setIsAdmin(false));
  }, []);

  const handleLogout = async () => {
    try {
      await logout();
    } catch (error) {
      console.error('Logout failed:', error);
    }
  };

  return (
    <nav className="bg-white border-b border-gray-200 px-4 py-3">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Link to="/dashboard" className="text-xl font-bold text-primary-600">
            Mnemo.ai
          </Link>
          <span className="text-sm text-gray-500">{user?.tenantName}</span>
        </div>

        <div className="flex items-center gap-4">
          <Link
            to="/about"
            className="text-sm font-medium text-gray-600 hover:text-primary-600 transition-colors"
          >
            About
          </Link>
          {isAdmin && (
            <Link
              to="/admin/usage"
              className="text-sm font-medium text-gray-600 hover:text-primary-600 transition-colors"
            >
              Admin
            </Link>
          )}
          <Menu as="div" className="relative">
            <MenuButton className="flex items-center gap-2 text-sm text-gray-700 hover:text-gray-900">
              <UserCircleIcon className="h-8 w-8 text-gray-400" />
              <span className="hidden sm:block">{user?.email}</span>
            </MenuButton>

            <Transition
              as={Fragment}
              enter="transition ease-out duration-100"
              enterFrom="transform opacity-0 scale-95"
              enterTo="transform opacity-100 scale-100"
              leave="transition ease-in duration-75"
              leaveFrom="transform opacity-100 scale-100"
              leaveTo="transform opacity-0 scale-95"
            >
              <MenuItems className="absolute right-0 mt-2 w-48 origin-top-right bg-white rounded-md shadow-lg ring-1 ring-black ring-opacity-5 focus:outline-none">
                <div className="py-1">
                  <MenuItem>
                    {({ focus }) => (
                      <Link
                        to="/settings"
                        className={`${
                          focus ? 'bg-gray-100' : ''
                        } flex items-center gap-2 px-4 py-2 text-sm text-gray-700`}
                      >
                        <Cog6ToothIcon className="h-5 w-5" />
                        Settings
                      </Link>
                    )}
                  </MenuItem>
                  <MenuItem>
                    {({ focus }) => (
                      <button
                        onClick={handleLogout}
                        className={`${
                          focus ? 'bg-gray-100' : ''
                        } flex items-center gap-2 w-full px-4 py-2 text-sm text-gray-700`}
                      >
                        <ArrowRightOnRectangleIcon className="h-5 w-5" />
                        Sign out
                      </button>
                    )}
                  </MenuItem>
                </div>
              </MenuItems>
            </Transition>
          </Menu>
        </div>
      </div>
    </nav>
  );
}
