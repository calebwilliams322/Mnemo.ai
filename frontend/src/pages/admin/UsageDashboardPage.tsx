import { Fragment, useEffect, useState } from 'react';
import { format, subDays } from 'date-fns';
import {
  ChartBarIcon,
  UserGroupIcon,
  ChatBubbleLeftRightIcon,
  CurrencyDollarIcon,
  ChevronDownIcon,
  ChevronUpIcon,
} from '@heroicons/react/24/outline';
import { Card, CardHeader, CardTitle, CardContent, LoadingSpinner } from '../../components/common';
import {
  getTenantsUsage,
  getTenantUserUsage,
  formatTokenCount,
  formatCost,
  type TenantUsageSummary,
  type UserUsageSummary,
  type UsageTotals,
} from '../../api/admin';
import { clsx } from 'clsx';

type DatePreset = '7d' | '30d' | 'thisMonth' | 'lastMonth' | 'custom';

export function UsageDashboardPage() {
  const [isLoading, setIsLoading] = useState(true);
  const [tenants, setTenants] = useState<TenantUsageSummary[]>([]);
  const [totals, setTotals] = useState<UsageTotals | null>(null);
  const [expandedTenant, setExpandedTenant] = useState<string | null>(null);
  const [tenantUsers, setTenantUsers] = useState<Map<string, UserUsageSummary[]>>(new Map());
  const [loadingUsers, setLoadingUsers] = useState<string | null>(null);
  const [datePreset, setDatePreset] = useState<DatePreset>('30d');
  const [startDate, setStartDate] = useState<string>(format(subDays(new Date(), 30), 'yyyy-MM-dd'));
  const [endDate, setEndDate] = useState<string>(format(new Date(), 'yyyy-MM-dd'));

  useEffect(() => {
    loadUsage();
  }, [startDate, endDate]);

  useEffect(() => {
    // Update dates based on preset
    const now = new Date();
    switch (datePreset) {
      case '7d':
        setStartDate(format(subDays(now, 7), 'yyyy-MM-dd'));
        setEndDate(format(now, 'yyyy-MM-dd'));
        break;
      case '30d':
        setStartDate(format(subDays(now, 30), 'yyyy-MM-dd'));
        setEndDate(format(now, 'yyyy-MM-dd'));
        break;
      case 'thisMonth':
        setStartDate(format(new Date(now.getFullYear(), now.getMonth(), 1), 'yyyy-MM-dd'));
        setEndDate(format(now, 'yyyy-MM-dd'));
        break;
      case 'lastMonth':
        const lastMonth = new Date(now.getFullYear(), now.getMonth() - 1, 1);
        const lastDayOfLastMonth = new Date(now.getFullYear(), now.getMonth(), 0);
        setStartDate(format(lastMonth, 'yyyy-MM-dd'));
        setEndDate(format(lastDayOfLastMonth, 'yyyy-MM-dd'));
        break;
      // 'custom' - don't change dates
    }
  }, [datePreset]);

  async function loadUsage() {
    setIsLoading(true);
    try {
      const data = await getTenantsUsage(startDate, endDate);
      setTenants(data.tenants);
      setTotals(data.totals);
    } catch (error) {
      console.error('Failed to load usage:', error);
    } finally {
      setIsLoading(false);
    }
  }

  async function toggleTenantExpand(tenantId: string) {
    if (expandedTenant === tenantId) {
      setExpandedTenant(null);
      return;
    }

    setExpandedTenant(tenantId);

    // Load users if not already loaded
    if (!tenantUsers.has(tenantId)) {
      setLoadingUsers(tenantId);
      try {
        const data = await getTenantUserUsage(tenantId, startDate, endDate);
        setTenantUsers((prev) => new Map(prev).set(tenantId, data.users));
      } catch (error) {
        console.error('Failed to load tenant users:', error);
      } finally {
        setLoadingUsers(null);
      }
    }
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Usage Dashboard</h1>
          <p className="text-gray-600">Monitor token usage and costs across all tenants</p>
        </div>

        {/* Date Range Selector */}
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2">
            {(['7d', '30d', 'thisMonth', 'lastMonth'] as const).map((preset) => (
              <button
                key={preset}
                onClick={() => setDatePreset(preset)}
                className={clsx(
                  'px-3 py-1.5 text-sm font-medium rounded-lg transition-colors',
                  datePreset === preset
                    ? 'bg-primary-100 text-primary-700'
                    : 'text-gray-600 hover:bg-gray-100'
                )}
              >
                {preset === '7d' && '7 Days'}
                {preset === '30d' && '30 Days'}
                {preset === 'thisMonth' && 'This Month'}
                {preset === 'lastMonth' && 'Last Month'}
              </button>
            ))}
          </div>
          <div className="flex items-center gap-2 text-sm">
            <input
              type="date"
              value={startDate}
              onChange={(e) => {
                setStartDate(e.target.value);
                setDatePreset('custom');
              }}
              className="px-2 py-1 border border-gray-300 rounded-md"
            />
            <span className="text-gray-500">to</span>
            <input
              type="date"
              value={endDate}
              onChange={(e) => {
                setEndDate(e.target.value);
                setDatePreset('custom');
              }}
              className="px-2 py-1 border border-gray-300 rounded-md"
            />
          </div>
        </div>
      </div>

      {isLoading ? (
        <div className="flex justify-center py-12">
          <LoadingSpinner />
        </div>
      ) : (
        <>
          {/* Summary Cards */}
          {totals && (
            <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
              <Card>
                <CardContent className="p-6">
                  <div className="flex items-center gap-4">
                    <div className="p-3 bg-blue-100 rounded-lg">
                      <ChatBubbleLeftRightIcon className="h-6 w-6 text-blue-600" />
                    </div>
                    <div>
                      <p className="text-sm text-gray-500">Total Messages</p>
                      <p className="text-2xl font-bold text-gray-900">
                        {totals.totalMessages.toLocaleString()}
                      </p>
                    </div>
                  </div>
                </CardContent>
              </Card>

              <Card>
                <CardContent className="p-6">
                  <div className="flex items-center gap-4">
                    <div className="p-3 bg-purple-100 rounded-lg">
                      <ChartBarIcon className="h-6 w-6 text-purple-600" />
                    </div>
                    <div>
                      <p className="text-sm text-gray-500">Input Tokens</p>
                      <p className="text-2xl font-bold text-gray-900">
                        {formatTokenCount(totals.totalInputTokens)}
                      </p>
                    </div>
                  </div>
                </CardContent>
              </Card>

              <Card>
                <CardContent className="p-6">
                  <div className="flex items-center gap-4">
                    <div className="p-3 bg-amber-100 rounded-lg">
                      <ChartBarIcon className="h-6 w-6 text-amber-600" />
                    </div>
                    <div>
                      <p className="text-sm text-gray-500">Output Tokens</p>
                      <p className="text-2xl font-bold text-gray-900">
                        {formatTokenCount(totals.totalOutputTokens)}
                      </p>
                    </div>
                  </div>
                </CardContent>
              </Card>

              <Card>
                <CardContent className="p-6">
                  <div className="flex items-center gap-4">
                    <div className="p-3 bg-green-100 rounded-lg">
                      <CurrencyDollarIcon className="h-6 w-6 text-green-600" />
                    </div>
                    <div>
                      <p className="text-sm text-gray-500">Estimated Cost</p>
                      <p className="text-2xl font-bold text-gray-900">
                        {formatCost(totals.totalCost)}
                      </p>
                    </div>
                  </div>
                </CardContent>
              </Card>
            </div>
          )}

          {/* Tenants Table */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <UserGroupIcon className="h-5 w-5" />
                Usage by Tenant
              </CardTitle>
            </CardHeader>
            <CardContent>
              {tenants.length === 0 ? (
                <p className="text-center text-gray-500 py-8">No usage data for this period</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full">
                    <thead>
                      <tr className="border-b border-gray-200">
                        <th className="text-left py-3 px-4 font-medium text-gray-500">Tenant</th>
                        <th className="text-right py-3 px-4 font-medium text-gray-500">Messages</th>
                        <th className="text-right py-3 px-4 font-medium text-gray-500">Input Tokens</th>
                        <th className="text-right py-3 px-4 font-medium text-gray-500">Output Tokens</th>
                        <th className="text-right py-3 px-4 font-medium text-gray-500">Est. Cost</th>
                        <th className="text-right py-3 px-4 font-medium text-gray-500">Users</th>
                        <th className="text-right py-3 px-4 font-medium text-gray-500">Conversations</th>
                        <th className="w-10"></th>
                      </tr>
                    </thead>
                    <tbody>
                      {tenants.map((tenant) => (
                        <Fragment key={tenant.tenantId}>
                          <tr
                            className="border-b border-gray-100 hover:bg-gray-50 cursor-pointer"
                            onClick={() => toggleTenantExpand(tenant.tenantId)}
                          >
                            <td className="py-3 px-4 font-medium text-gray-900">
                              {tenant.tenantName}
                            </td>
                            <td className="py-3 px-4 text-right text-gray-600">
                              {tenant.messageCount.toLocaleString()}
                            </td>
                            <td className="py-3 px-4 text-right text-gray-600">
                              {formatTokenCount(tenant.totalInputTokens)}
                            </td>
                            <td className="py-3 px-4 text-right text-gray-600">
                              {formatTokenCount(tenant.totalOutputTokens)}
                            </td>
                            <td className="py-3 px-4 text-right font-medium text-gray-900">
                              {formatCost(tenant.estimatedCost)}
                            </td>
                            <td className="py-3 px-4 text-right text-gray-600">
                              {tenant.activeUserCount}
                            </td>
                            <td className="py-3 px-4 text-right text-gray-600">
                              {tenant.conversationCount}
                            </td>
                            <td className="py-3 px-4">
                              {expandedTenant === tenant.tenantId ? (
                                <ChevronUpIcon className="h-4 w-4 text-gray-400" />
                              ) : (
                                <ChevronDownIcon className="h-4 w-4 text-gray-400" />
                              )}
                            </td>
                          </tr>
                          {expandedTenant === tenant.tenantId && (
                            <tr>
                              <td colSpan={8} className="bg-gray-50 p-4">
                                {loadingUsers === tenant.tenantId ? (
                                  <div className="flex justify-center py-4">
                                    <LoadingSpinner />
                                  </div>
                                ) : (
                                  <UserUsageTable users={tenantUsers.get(tenant.tenantId) || []} />
                                )}
                              </td>
                            </tr>
                          )}
                        </Fragment>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </CardContent>
          </Card>
        </>
      )}
    </div>
  );
}

function UserUsageTable({ users }: { users: UserUsageSummary[] }) {
  if (users.length === 0) {
    return <p className="text-center text-gray-500 py-4">No user data available</p>;
  }

  return (
    <table className="w-full">
      <thead>
        <tr className="border-b border-gray-200">
          <th className="text-left py-2 px-3 font-medium text-gray-500 text-sm">User</th>
          <th className="text-right py-2 px-3 font-medium text-gray-500 text-sm">Messages</th>
          <th className="text-right py-2 px-3 font-medium text-gray-500 text-sm">Input Tokens</th>
          <th className="text-right py-2 px-3 font-medium text-gray-500 text-sm">Output Tokens</th>
          <th className="text-right py-2 px-3 font-medium text-gray-500 text-sm">Est. Cost</th>
          <th className="text-right py-2 px-3 font-medium text-gray-500 text-sm">Conversations</th>
        </tr>
      </thead>
      <tbody>
        {users.map((user) => (
          <tr key={user.userId} className="border-b border-gray-100">
            <td className="py-2 px-3">
              <div>
                <p className="font-medium text-gray-900 text-sm">{user.userName || 'Unknown'}</p>
                <p className="text-gray-500 text-xs">{user.userEmail}</p>
              </div>
            </td>
            <td className="py-2 px-3 text-right text-gray-600 text-sm">
              {user.messageCount.toLocaleString()}
            </td>
            <td className="py-2 px-3 text-right text-gray-600 text-sm">
              {formatTokenCount(user.totalInputTokens)}
            </td>
            <td className="py-2 px-3 text-right text-gray-600 text-sm">
              {formatTokenCount(user.totalOutputTokens)}
            </td>
            <td className="py-2 px-3 text-right font-medium text-gray-900 text-sm">
              {formatCost(user.estimatedCost)}
            </td>
            <td className="py-2 px-3 text-right text-gray-600 text-sm">
              {user.conversationCount}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
