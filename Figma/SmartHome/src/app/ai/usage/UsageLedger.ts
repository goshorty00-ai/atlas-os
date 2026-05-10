// Usage Ledger - Track all AI requests

import type { UsageRecord, UsageSummary } from './types';
import { CostEstimator } from './CostEstimator';

export class UsageLedger {
  private records: UsageRecord[] = [];
  private costEstimator: CostEstimator;
  private storageKey = 'atlas_ai_usage_ledger';
  private maxRecords = 10000; // Keep last 10k records in memory

  constructor() {
    this.costEstimator = new CostEstimator();
    this.loadFromStorage();
  }

  // Log a new usage record
  logUsage(record: Omit<UsageRecord, 'id' | 'timestamp' | 'estimatedCost'>): UsageRecord {
    const fullRecord: UsageRecord = {
      ...record,
      id: `usage_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`,
      timestamp: Date.now(),
      estimatedCost: this.costEstimator.estimateCost(record.model, record.inputTokens, record.outputTokens)
    };

    this.records.push(fullRecord);

    // Trim old records if exceeding max
    if (this.records.length > this.maxRecords) {
      this.records = this.records.slice(-this.maxRecords);
    }

    this.saveToStorage();

    console.log(`[UsageLedger] Logged: ${record.provider}/${record.model} - ${record.totalTokens} tokens - $${fullRecord.estimatedCost.toFixed(4)}`);

    return fullRecord;
  }

  // Get records for a time period
  getRecords(startTime?: number, endTime?: number): UsageRecord[] {
    let filtered = this.records;

    if (startTime) {
      filtered = filtered.filter(r => r.timestamp >= startTime);
    }

    if (endTime) {
      filtered = filtered.filter(r => r.timestamp <= endTime);
    }

    return filtered;
  }

  // Get usage summary for a time period
  getSummary(startTime?: number, endTime?: number): UsageSummary {
    const records = this.getRecords(startTime, endTime);

    const summary: UsageSummary = {
      totalRequests: records.length,
      successfulRequests: records.filter(r => r.success).length,
      failedRequests: records.filter(r => !r.success).length,
      totalTokens: records.reduce((sum, r) => sum + r.totalTokens, 0),
      totalCost: records.reduce((sum, r) => sum + r.estimatedCost, 0),
      averageLatency: records.length > 0 ? records.reduce((sum, r) => sum + r.latencyMs, 0) / records.length : 0,
      byProvider: {},
      byModel: {},
      byFeature: {},
      byCostTier: {
        cheap: { requests: 0, cost: 0 },
        balanced: { requests: 0, cost: 0 },
        premium: { requests: 0, cost: 0 }
      }
    };

    // Aggregate by provider
    records.forEach(r => {
      if (!summary.byProvider[r.provider]) {
        summary.byProvider[r.provider] = { requests: 0, tokens: 0, cost: 0 };
      }
      summary.byProvider[r.provider].requests++;
      summary.byProvider[r.provider].tokens += r.totalTokens;
      summary.byProvider[r.provider].cost += r.estimatedCost;
    });

    // Aggregate by model
    records.forEach(r => {
      if (!summary.byModel[r.model]) {
        summary.byModel[r.model] = { requests: 0, tokens: 0, cost: 0 };
      }
      summary.byModel[r.model].requests++;
      summary.byModel[r.model].tokens += r.totalTokens;
      summary.byModel[r.model].cost += r.estimatedCost;
    });

    // Aggregate by feature
    records.forEach(r => {
      if (!summary.byFeature[r.feature]) {
        summary.byFeature[r.feature] = { requests: 0, tokens: 0, cost: 0 };
      }
      summary.byFeature[r.feature].requests++;
      summary.byFeature[r.feature].tokens += r.totalTokens;
      summary.byFeature[r.feature].cost += r.estimatedCost;
    });

    // Aggregate by cost tier
    records.forEach(r => {
      summary.byCostTier[r.costTier].requests++;
      summary.byCostTier[r.costTier].cost += r.estimatedCost;
    });

    return summary;
  }

  // Get most expensive tasks
  getMostExpensive(limit: number = 10, startTime?: number, endTime?: number): UsageRecord[] {
    const records = this.getRecords(startTime, endTime);
    return records
      .sort((a, b) => b.estimatedCost - a.estimatedCost)
      .slice(0, limit);
  }

  // Get total spend for period
  getTotalSpend(startTime?: number, endTime?: number): number {
    const records = this.getRecords(startTime, endTime);
    return records.reduce((sum, r) => sum + r.estimatedCost, 0);
  }

  // Get spend by provider
  getSpendByProvider(provider: string, startTime?: number, endTime?: number): number {
    const records = this.getRecords(startTime, endTime).filter(r => r.provider === provider);
    return records.reduce((sum, r) => sum + r.estimatedCost, 0);
  }

  // Get spend by feature
  getSpendByFeature(feature: string, startTime?: number, endTime?: number): number {
    const records = this.getRecords(startTime, endTime).filter(r => r.feature === feature);
    return records.reduce((sum, r) => sum + r.estimatedCost, 0);
  }

  // Get spend by user
  getSpendByUser(userId: string, startTime?: number, endTime?: number): number {
    const records = this.getRecords(startTime, endTime).filter(r => r.userId === userId);
    return records.reduce((sum, r) => sum + r.estimatedCost, 0);
  }

  // Clear old records
  clearOldRecords(olderThan: number) {
    const before = this.records.length;
    this.records = this.records.filter(r => r.timestamp >= olderThan);
    const after = this.records.length;
    
    if (before !== after) {
      this.saveToStorage();
      console.log(`[UsageLedger] Cleared ${before - after} old records`);
    }
  }

  // Export records as JSON
  exportRecords(startTime?: number, endTime?: number): string {
    const records = this.getRecords(startTime, endTime);
    return JSON.stringify(records, null, 2);
  }

  // Save to localStorage
  private saveToStorage() {
    try {
      localStorage.setItem(this.storageKey, JSON.stringify(this.records));
    } catch (error) {
      console.error('[UsageLedger] Failed to save to storage:', error);
    }
  }

  // Load from localStorage
  private loadFromStorage() {
    try {
      const stored = localStorage.getItem(this.storageKey);
      if (stored) {
        this.records = JSON.parse(stored);
        console.log(`[UsageLedger] Loaded ${this.records.length} records from storage`);
      }
    } catch (error) {
      console.error('[UsageLedger] Failed to load from storage:', error);
      this.records = [];
    }
  }

  // Get cost estimator
  getCostEstimator(): CostEstimator {
    return this.costEstimator;
  }

  // Get most recent request
  getLastRequest(): UsageRecord | null {
    if (this.records.length === 0) return null;
    return this.records[this.records.length - 1];
  }

  // Get usage stats for quick display
  getUsageStats(): {
    spendToday: number;
    spendThisMonth: number;
    requestsToday: number;
    requestsThisMonth: number;
    lastRequest: UsageRecord | null;
  } {
    const dayStart = this.getDayStart();
    const monthStart = this.getMonthStart();
    
    const todayRecords = this.getRecords(dayStart);
    const monthRecords = this.getRecords(monthStart);
    
    return {
      spendToday: todayRecords.reduce((sum, r) => sum + r.estimatedCost, 0),
      spendThisMonth: monthRecords.reduce((sum, r) => sum + r.estimatedCost, 0),
      requestsToday: todayRecords.length,
      requestsThisMonth: monthRecords.length,
      lastRequest: this.getLastRequest()
    };
  }

  // Time helpers (made public for external use)
  private getDayStart(): number {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
  }

  private getMonthStart(): number {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), 1).getTime();
  }
}
