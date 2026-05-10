// AI Usage Dashboard - Track spend, usage, and budget

import { motion } from "motion/react";
import { DollarSign, TrendingUp, AlertTriangle, Activity, Clock, Zap } from "lucide-react";
import { useEffect, useState } from "react";
import type { UsageLedger } from "../ai/usage/UsageLedger";
import type { BudgetManager } from "../ai/usage/BudgetManager";

interface UsageDashboardProps {
  usageLedger: UsageLedger;
  budgetManager: BudgetManager;
  className?: string;
}

interface UsageStats {
  spendToday: number;
  spendWeek: number;
  spendMonth: number;
  requestsToday: number;
  requestsWeek: number;
  requestsMonth: number;
  byProvider: Record<string, { requests: number; cost: number; tokens: number }>;
  byModel: Record<string, { requests: number; cost: number; tokens: number }>;
  byFeature: Record<string, { requests: number; cost: number; tokens: number }>;
  recentRequests: Array<{
    timestamp: number;
    provider: string;
    model: string;
    feature: string;
    tokens: number;
    cost: number;
    success: boolean;
  }>;
}

export function UsageDashboard({ usageLedger, budgetManager, className = "" }: UsageDashboardProps) {
  const [stats, setStats] = useState<UsageStats | null>(null);
  const [budgetStatus, setBudgetStatus] = useState<any>(null);
  const [alerts, setAlerts] = useState<string[]>([]);

  useEffect(() => {
    const updateStats = () => {
      // Get usage stats
      const now = Date.now();
      const oneDayAgo = now - 24 * 60 * 60 * 1000;
      const oneWeekAgo = now - 7 * 24 * 60 * 60 * 1000;
      const oneMonthAgo = now - 30 * 24 * 60 * 60 * 1000;

      const spendToday = usageLedger.getTotalSpend(oneDayAgo, now);
      const spendWeek = usageLedger.getTotalSpend(oneWeekAgo, now);
      const spendMonth = usageLedger.getTotalSpend(oneMonthAgo, now);

      const requestsToday = usageLedger.getRequestCount(oneDayAgo, now);
      const requestsWeek = usageLedger.getRequestCount(oneWeekAgo, now);
      const requestsMonth = usageLedger.getRequestCount(oneMonthAgo, now);

      const byProvider = usageLedger.getUsageByProvider(oneMonthAgo, now);
      const byModel = usageLedger.getUsageByModel(oneMonthAgo, now);
      const byFeature = usageLedger.getUsageByFeature(oneMonthAgo, now);
      const recentRequests = usageLedger.getRecentRequests(10);

      setStats({
        spendToday,
        spendWeek,
        spendMonth,
        requestsToday,
        requestsWeek,
        requestsMonth,
        byProvider,
        byModel,
        byFeature,
        recentRequests
      });

      // Get budget status
      const status = budgetManager.getBudgetStatus();
      setBudgetStatus(status);

      // Get alerts
      const newAlerts: string[] = [];
      if (status.daily.percentUsed > 80) {
        newAlerts.push(`Daily budget ${status.daily.percentUsed.toFixed(0)}% used`);
      }
      if (status.monthly.percentUsed > 80) {
        newAlerts.push(`Monthly budget ${status.monthly.percentUsed.toFixed(0)}% used`);
      }
      if (budgetManager.shouldDowngrade()) {
        newAlerts.push('Budget threshold reached - consider downgrading to cheaper models');
      }
      setAlerts(newAlerts);
    };

    updateStats();
    const interval = setInterval(updateStats, 5000); // Update every 5 seconds

    return () => clearInterval(interval);
  }, [usageLedger, budgetManager]);

  if (!stats || !budgetStatus) {
    return (
      <div className={`p-6 ${className}`}>
        <div className="text-center text-white/60">Loading usage data...</div>
      </div>
    );
  }

  return (
    <div className={`p-6 space-y-6 ${className}`}>
      {/* Header */}
      <div>
        <h2 className="text-2xl font-bold text-white mb-2">AI Usage & Budget</h2>
        <p className="text-sm text-white/60">Track your AI spending and usage patterns</p>
      </div>

      {/* Alerts */}
      {alerts.length > 0 && (
        <motion.div
          initial={{ opacity: 0, y: -10 }}
          animate={{ opacity: 1, y: 0 }}
          className="p-4 rounded-xl border border-yellow-500/30 bg-yellow-500/10"
        >
          <div className="flex items-start gap-3">
            <AlertTriangle className="w-5 h-5 text-yellow-500 flex-shrink-0 mt-0.5" />
            <div className="flex-1">
              <h3 className="text-sm font-semibold text-yellow-500 mb-1">Budget Alerts</h3>
              <ul className="text-xs text-yellow-500/80 space-y-1">
                {alerts.map((alert, idx) => (
                  <li key={idx}>• {alert}</li>
                ))}
              </ul>
            </div>
          </div>
        </motion.div>
      )}

      {/* Spend Overview */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <StatCard
          icon={<DollarSign className="w-5 h-5" />}
          label="Today"
          value={`$${stats.spendToday.toFixed(4)}`}
          subtitle={`${stats.requestsToday} requests`}
          color="cyan"
        />
        <StatCard
          icon={<TrendingUp className="w-5 h-5" />}
          label="This Week"
          value={`$${stats.spendWeek.toFixed(4)}`}
          subtitle={`${stats.requestsWeek} requests`}
          color="blue"
        />
        <StatCard
          icon={<Activity className="w-5 h-5" />}
          label="This Month"
          value={`$${stats.spendMonth.toFixed(4)}`}
          subtitle={`${stats.requestsMonth} requests`}
          color="purple"
        />
      </div>

      {/* Budget Status */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <BudgetCard
          label="Daily Budget"
          spent={budgetStatus.daily.spent}
          limit={budgetStatus.daily.limit}
          percentUsed={budgetStatus.daily.percentUsed}
        />
        <BudgetCard
          label="Monthly Budget"
          spent={budgetStatus.monthly.spent}
          limit={budgetStatus.monthly.limit}
          percentUsed={budgetStatus.monthly.percentUsed}
        />
      </div>

      {/* Usage by Provider */}
      <div className="rounded-xl border border-white/10 bg-white/5 p-4">
        <h3 className="text-sm font-semibold text-white mb-3 flex items-center gap-2">
          <Zap className="w-4 h-4 text-cyan-400" />
          Usage by Provider
        </h3>
        <div className="space-y-2">
          {Object.entries(stats.byProvider).map(([provider, data]) => (
            <UsageBar
              key={provider}
              label={provider}
              requests={data.requests}
              cost={data.cost}
              tokens={data.tokens}
              total={stats.requestsMonth}
            />
          ))}
        </div>
      </div>

      {/* Usage by Model */}
      <div className="rounded-xl border border-white/10 bg-white/5 p-4">
        <h3 className="text-sm font-semibold text-white mb-3 flex items-center gap-2">
          <Activity className="w-4 h-4 text-cyan-400" />
          Usage by Model
        </h3>
        <div className="space-y-2">
          {Object.entries(stats.byModel)
            .sort((a, b) => b[1].cost - a[1].cost)
            .slice(0, 5)
            .map(([model, data]) => (
              <UsageBar
                key={model}
                label={model}
                requests={data.requests}
                cost={data.cost}
                tokens={data.tokens}
                total={stats.requestsMonth}
              />
            ))}
        </div>
      </div>

      {/* Usage by Feature */}
      <div className="rounded-xl border border-white/10 bg-white/5 p-4">
        <h3 className="text-sm font-semibold text-white mb-3 flex items-center gap-2">
          <TrendingUp className="w-4 h-4 text-cyan-400" />
          Usage by Feature
        </h3>
        <div className="space-y-2">
          {Object.entries(stats.byFeature).map(([feature, data]) => (
            <UsageBar
              key={feature}
              label={feature}
              requests={data.requests}
              cost={data.cost}
              tokens={data.tokens}
              total={stats.requestsMonth}
            />
          ))}
        </div>
      </div>

      {/* Recent Requests */}
      <div className="rounded-xl border border-white/10 bg-white/5 p-4">
        <h3 className="text-sm font-semibold text-white mb-3 flex items-center gap-2">
          <Clock className="w-4 h-4 text-cyan-400" />
          Recent Requests
        </h3>
        <div className="space-y-2">
          {stats.recentRequests.map((req, idx) => (
            <div
              key={idx}
              className="flex items-center justify-between p-2 rounded-lg bg-white/5 text-xs"
            >
              <div className="flex items-center gap-3 flex-1">
                <div className={`w-2 h-2 rounded-full ${req.success ? 'bg-green-500' : 'bg-red-500'}`} />
                <span className="text-white/80">{req.model}</span>
                <span className="text-white/40">•</span>
                <span className="text-white/60">{req.feature}</span>
              </div>
              <div className="flex items-center gap-4 text-white/60">
                <span>{req.tokens} tokens</span>
                <span className="text-cyan-400">${req.cost.toFixed(6)}</span>
                <span>{new Date(req.timestamp).toLocaleTimeString()}</span>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function StatCard({ icon, label, value, subtitle, color }: any) {
  const colorMap: Record<string, string> = {
    cyan: 'from-cyan-500/20 to-cyan-600/20 border-cyan-500/30',
    blue: 'from-blue-500/20 to-blue-600/20 border-blue-500/30',
    purple: 'from-purple-500/20 to-purple-600/20 border-purple-500/30'
  };

  return (
    <div className={`rounded-xl border bg-gradient-to-br p-4 ${colorMap[color]}`}>
      <div className="flex items-center gap-3 mb-2">
        <div className="text-white/80">{icon}</div>
        <span className="text-sm text-white/60">{label}</span>
      </div>
      <div className="text-2xl font-bold text-white mb-1">{value}</div>
      <div className="text-xs text-white/40">{subtitle}</div>
    </div>
  );
}

function BudgetCard({ label, spent, limit, percentUsed }: any) {
  const getColor = (percent: number) => {
    if (percent >= 90) return 'red';
    if (percent >= 75) return 'yellow';
    return 'green';
  };

  const color = getColor(percentUsed);
  const colorMap: Record<string, { bar: string; text: string }> = {
    green: { bar: 'bg-green-500', text: 'text-green-500' },
    yellow: { bar: 'bg-yellow-500', text: 'text-yellow-500' },
    red: { bar: 'bg-red-500', text: 'text-red-500' }
  };

  return (
    <div className="rounded-xl border border-white/10 bg-white/5 p-4">
      <div className="flex items-center justify-between mb-3">
        <span className="text-sm font-semibold text-white">{label}</span>
        <span className={`text-sm font-bold ${colorMap[color].text}`}>
          {percentUsed.toFixed(1)}%
        </span>
      </div>
      <div className="w-full h-2 rounded-full bg-white/10 overflow-hidden mb-2">
        <motion.div
          className={`h-full ${colorMap[color].bar}`}
          initial={{ width: 0 }}
          animate={{ width: `${Math.min(percentUsed, 100)}%` }}
          transition={{ duration: 0.5 }}
        />
      </div>
      <div className="flex items-center justify-between text-xs text-white/60">
        <span>${spent.toFixed(4)} spent</span>
        <span>${limit.toFixed(2)} limit</span>
      </div>
    </div>
  );
}

function UsageBar({ label, requests, cost, tokens, total }: any) {
  const percent = total > 0 ? (requests / total) * 100 : 0;

  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between text-xs">
        <span className="text-white/80">{label}</span>
        <div className="flex items-center gap-3 text-white/60">
          <span>{requests} req</span>
          <span>{(tokens / 1000).toFixed(1)}k tokens</span>
          <span className="text-cyan-400">${cost.toFixed(6)}</span>
        </div>
      </div>
      <div className="w-full h-1.5 rounded-full bg-white/10 overflow-hidden">
        <motion.div
          className="h-full bg-gradient-to-r from-cyan-500 to-blue-500"
          initial={{ width: 0 }}
          animate={{ width: `${percent}%` }}
          transition={{ duration: 0.3 }}
        />
      </div>
    </div>
  );
}
