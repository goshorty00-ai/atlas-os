// Budget Manager - Track and enforce budget limits

import type { BudgetConfig, BudgetStatus, CostAlert } from './types';
import { UsageLedger } from './UsageLedger';

export class BudgetManager {
  private config: BudgetConfig;
  private ledger: UsageLedger;
  private alerts: CostAlert[] = [];
  private storageKey = 'atlas_ai_budget_config';

  constructor(ledger: UsageLedger, config?: BudgetConfig) {
    this.ledger = ledger;
    this.config = config || this.getDefaultConfig();
    this.loadConfig();
  }

  // Get default budget configuration
  private getDefaultConfig(): BudgetConfig {
    return {
      daily: 10.00,
      weekly: 50.00,
      monthly: 150.00,
      perProvider: {
        anthropic: 100.00,
        google: 30.00,
        openai: 20.00
      },
      perFeature: {
        chat: 50.00,
        smart_home: 30.00,
        automation: 20.00,
        analysis: 30.00,
        background_task: 20.00
      },
      warningThreshold: 0.80,
      criticalThreshold: 0.95,
      hardLimitEnabled: true,
      autoDowngrade: true,
      downgradeThreshold: 0.90
    };
  }

  // Check if request is within budget
  canAfford(estimatedCost: number, provider?: string, feature?: string, userId?: string): {
    allowed: boolean;
    reason?: string;
    alert?: CostAlert;
  } {
    // Check daily budget
    const dailyStatus = this.getDailyStatus();
    if (dailyStatus.remaining < estimatedCost && this.config.hardLimitEnabled) {
      return {
        allowed: false,
        reason: `Daily budget exceeded. Remaining: $${dailyStatus.remaining.toFixed(4)}`,
        alert: this.createAlert('daily', dailyStatus)
      };
    }

    // Check monthly budget
    const monthlyStatus = this.getMonthlyStatus();
    if (monthlyStatus.remaining < estimatedCost && this.config.hardLimitEnabled) {
      return {
        allowed: false,
        reason: `Monthly budget exceeded. Remaining: $${monthlyStatus.remaining.toFixed(4)}`,
        alert: this.createAlert('monthly', monthlyStatus)
      };
    }

    // Check provider budget
    if (provider && this.config.perProvider?.[provider]) {
      const providerSpend = this.ledger.getSpendByProvider(provider, this.getMonthStart());
      const providerLimit = this.config.perProvider[provider];
      if (providerSpend + estimatedCost > providerLimit && this.config.hardLimitEnabled) {
        return {
          allowed: false,
          reason: `Provider budget exceeded for ${provider}. Limit: $${providerLimit}`,
          alert: this.createProviderAlert(provider, providerSpend, providerLimit)
        };
      }
    }

    // Check feature budget
    if (feature && this.config.perFeature?.[feature]) {
      const featureSpend = this.ledger.getSpendByFeature(feature, this.getMonthStart());
      const featureLimit = this.config.perFeature[feature];
      if (featureSpend + estimatedCost > featureLimit && this.config.hardLimitEnabled) {
        return {
          allowed: false,
          reason: `Feature budget exceeded for ${feature}. Limit: $${featureLimit}`,
          alert: this.createFeatureAlert(feature, featureSpend, featureLimit)
        };
      }
    }

    // Check user budget
    if (userId && this.config.perUser?.[userId]) {
      const userSpend = this.ledger.getSpendByUser(userId, this.getMonthStart());
      const userLimit = this.config.perUser[userId];
      if (userSpend + estimatedCost > userLimit && this.config.hardLimitEnabled) {
        return {
          allowed: false,
          reason: `User budget exceeded. Limit: $${userLimit}`,
          alert: this.createUserAlert(userId, userSpend, userLimit)
        };
      }
    }

    return { allowed: true };
  }

  // Check if should downgrade to cheaper model
  shouldDowngrade(): boolean {
    if (!this.config.autoDowngrade) return false;

    const monthlyStatus = this.getMonthlyStatus();
    return monthlyStatus.percentUsed >= this.config.downgradeThreshold;
  }

  // Get daily budget status
  getDailyStatus(): BudgetStatus {
    const limit = this.config.daily || 0;
    const spent = this.ledger.getTotalSpend(this.getDayStart());
    const remaining = Math.max(0, limit - spent);
    const percentUsed = limit > 0 ? (spent / limit) * 100 : 0;

    return {
      period: 'daily',
      limit,
      spent,
      remaining,
      percentUsed,
      status: this.getStatus(percentUsed)
    };
  }

  // Get weekly budget status
  getWeeklyStatus(): BudgetStatus {
    const limit = this.config.weekly || 0;
    const spent = this.ledger.getTotalSpend(this.getWeekStart());
    const remaining = Math.max(0, limit - spent);
    const percentUsed = limit > 0 ? (spent / limit) * 100 : 0;

    return {
      period: 'weekly',
      limit,
      spent,
      remaining,
      percentUsed,
      status: this.getStatus(percentUsed)
    };
  }

  // Get monthly budget status
  getMonthlyStatus(): BudgetStatus {
    const limit = this.config.monthly || 0;
    const spent = this.ledger.getTotalSpend(this.getMonthStart());
    const remaining = Math.max(0, limit - spent);
    const percentUsed = limit > 0 ? (spent / limit) * 100 : 0;

    // Project monthly spend
    const daysInMonth = new Date(new Date().getFullYear(), new Date().getMonth() + 1, 0).getDate();
    const dayOfMonth = new Date().getDate();
    const projectedMonthlySpend = dayOfMonth > 0 ? (spent / dayOfMonth) * daysInMonth : 0;

    return {
      period: 'monthly',
      limit,
      spent,
      remaining,
      percentUsed,
      status: this.getStatus(percentUsed),
      projectedMonthlySpend
    };
  }

  // Get status based on percent used
  private getStatus(percentUsed: number): 'ok' | 'warning' | 'critical' | 'exceeded' {
    if (percentUsed >= 100) return 'exceeded';
    if (percentUsed >= this.config.criticalThreshold * 100) return 'critical';
    if (percentUsed >= this.config.warningThreshold * 100) return 'warning';
    return 'ok';
  }

  // Get all active alerts
  getAlerts(): CostAlert[] {
    const alerts: CostAlert[] = [];

    // Check daily budget
    const dailyStatus = this.getDailyStatus();
    if (dailyStatus.status !== 'ok') {
      alerts.push(this.createAlert('daily', dailyStatus));
    }

    // Check monthly budget
    const monthlyStatus = this.getMonthlyStatus();
    if (monthlyStatus.status !== 'ok') {
      alerts.push(this.createAlert('monthly', monthlyStatus));
    }

    return alerts;
  }

  // Create alert
  private createAlert(type: 'daily' | 'weekly' | 'monthly', status: BudgetStatus): CostAlert {
    const severity = status.status === 'exceeded' ? 'exceeded' : 
                     status.status === 'critical' ? 'critical' : 'warning';

    let message = `${type.charAt(0).toUpperCase() + type.slice(1)} budget at ${status.percentUsed.toFixed(1)}%`;
    let recommendation = '';

    if (status.status === 'exceeded') {
      message += ` - EXCEEDED by $${(status.spent - status.limit).toFixed(2)}`;
      recommendation = 'Consider upgrading budget or reducing usage';
    } else if (status.status === 'critical') {
      message += ` - Only $${status.remaining.toFixed(2)} remaining`;
      recommendation = 'Switch to cheapest cost mode or pause non-essential tasks';
    } else {
      message += ` - $${status.remaining.toFixed(2)} remaining`;
      recommendation = 'Monitor usage closely';
    }

    return {
      id: `alert_${type}_${Date.now()}`,
      timestamp: Date.now(),
      severity,
      type,
      message,
      currentSpend: status.spent,
      limit: status.limit,
      percentUsed: status.percentUsed,
      recommendation
    };
  }

  // Create provider alert
  private createProviderAlert(provider: string, spent: number, limit: number): CostAlert {
    const percentUsed = (spent / limit) * 100;
    return {
      id: `alert_provider_${provider}_${Date.now()}`,
      timestamp: Date.now(),
      severity: percentUsed >= 100 ? 'exceeded' : 'critical',
      type: 'provider',
      message: `${provider} budget at ${percentUsed.toFixed(1)}%`,
      currentSpend: spent,
      limit,
      percentUsed,
      recommendation: `Switch to alternative provider or increase ${provider} budget`
    };
  }

  // Create feature alert
  private createFeatureAlert(feature: string, spent: number, limit: number): CostAlert {
    const percentUsed = (spent / limit) * 100;
    return {
      id: `alert_feature_${feature}_${Date.now()}`,
      timestamp: Date.now(),
      severity: percentUsed >= 100 ? 'exceeded' : 'critical',
      type: 'feature',
      message: `${feature} feature budget at ${percentUsed.toFixed(1)}%`,
      currentSpend: spent,
      limit,
      percentUsed,
      recommendation: `Reduce ${feature} usage or increase feature budget`
    };
  }

  // Create user alert
  private createUserAlert(userId: string, spent: number, limit: number): CostAlert {
    const percentUsed = (spent / limit) * 100;
    return {
      id: `alert_user_${userId}_${Date.now()}`,
      timestamp: Date.now(),
      severity: percentUsed >= 100 ? 'exceeded' : 'critical',
      type: 'user',
      message: `User ${userId} budget at ${percentUsed.toFixed(1)}%`,
      currentSpend: spent,
      limit,
      percentUsed,
      recommendation: `Contact user or increase user budget`
    };
  }

  // Time helpers
  private getDayStart(): number {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
  }

  private getWeekStart(): number {
    const now = new Date();
    const day = now.getDay();
    const diff = now.getDate() - day;
    return new Date(now.getFullYear(), now.getMonth(), diff).getTime();
  }

  private getMonthStart(): number {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), 1).getTime();
  }

  // Update configuration
  updateConfig(config: Partial<BudgetConfig>) {
    this.config = { ...this.config, ...config };
    this.saveConfig();
    console.log('[BudgetManager] Configuration updated');
  }

  // Get configuration
  getConfig(): BudgetConfig {
    return { ...this.config };
  }

  // Save configuration
  private saveConfig() {
    try {
      localStorage.setItem(this.storageKey, JSON.stringify(this.config));
    } catch (error) {
      console.error('[BudgetManager] Failed to save config:', error);
    }
  }

  // Load configuration
  private loadConfig() {
    try {
      const stored = localStorage.getItem(this.storageKey);
      if (stored) {
        this.config = { ...this.config, ...JSON.parse(stored) };
        console.log('[BudgetManager] Configuration loaded');
      }
    } catch (error) {
      console.error('[BudgetManager] Failed to load config:', error);
    }
  }
}
