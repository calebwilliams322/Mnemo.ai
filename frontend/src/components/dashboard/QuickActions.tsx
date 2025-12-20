import { Link } from 'react-router-dom';
import {
  DocumentMagnifyingGlassIcon,
  ShieldExclamationIcon,
  DocumentTextIcon,
  ScaleIcon,
} from '@heroicons/react/24/outline';
import { clsx } from 'clsx';

interface QuickAction {
  id: string;
  title: string;
  description: string;
  icon: React.ComponentType<{ className?: string }>;
  href?: string;
  disabled?: boolean;
  badge?: string;
  iconBgColor: string;
  iconColor: string;
}

const actions: QuickAction[] = [
  {
    id: 'policy-summary',
    title: 'Policy Summary',
    description: 'Upload a policy and get an AI-powered summary with key details',
    icon: DocumentMagnifyingGlassIcon,
    href: '/policy-summary',
    iconBgColor: 'bg-primary-100',
    iconColor: 'text-primary-600',
  },
  {
    id: 'coverage-gap',
    title: 'Coverage Gap Analysis',
    description: 'Compare policies against requirements to identify gaps',
    icon: ShieldExclamationIcon,
    disabled: true,
    badge: 'Coming Soon',
    iconBgColor: 'bg-amber-100',
    iconColor: 'text-amber-600',
  },
  {
    id: 'proposal-gen',
    title: 'Proposal Generation',
    description: 'Generate professional proposals from policy data',
    icon: DocumentTextIcon,
    disabled: true,
    badge: 'Coming Soon',
    iconBgColor: 'bg-emerald-100',
    iconColor: 'text-emerald-600',
  },
  {
    id: 'quote-comparison',
    title: 'Quote Comparison',
    description: 'Compare quotes from multiple carriers side-by-side',
    icon: ScaleIcon,
    href: '/quote-comparison',
    iconBgColor: 'bg-violet-100',
    iconColor: 'text-violet-600',
  },
];

export function QuickActions() {
  return (
    <div className="mb-8">
      <h2 className="text-lg font-semibold text-gray-900 mb-4">Quick Actions</h2>
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {actions.map((action) => {
          const Icon = action.icon;
          const content = (
            <div
              className={clsx(
                'relative bg-white rounded-lg border border-gray-200 p-6 transition-all',
                action.disabled
                  ? 'opacity-60 cursor-not-allowed'
                  : 'hover:border-primary-300 hover:shadow-md cursor-pointer'
              )}
            >
              {action.badge && (
                <span className="absolute top-3 right-3 inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-600">
                  {action.badge}
                </span>
              )}
              <div className={clsx('inline-flex p-3 rounded-lg', action.iconBgColor)}>
                <Icon className={clsx('h-6 w-6', action.iconColor)} />
              </div>
              <h3 className="mt-4 text-base font-semibold text-gray-900">
                {action.title}
              </h3>
              <p className="mt-2 text-sm text-gray-500">{action.description}</p>
            </div>
          );

          if (action.disabled || !action.href) {
            return <div key={action.id}>{content}</div>;
          }

          return (
            <Link key={action.id} to={action.href} className="block">
              {content}
            </Link>
          );
        })}
      </div>
    </div>
  );
}
