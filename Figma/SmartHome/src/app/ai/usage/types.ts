// AI Usage Tracking Types

export interface UsageRecord {
  id: string;
  timestamp: number;
  provider: 'openai' | 'anthropic' | 'google';
  model: string;
  feature: string; // 'chat', 'smart_home', 'automation', 'analysis', etc.
  userId?: string;
  sessionId: string;
  requestType: 'completion' | 'tool_call' | 'streaming';
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  toolCallsUsed: string[]; // Array of tool names
  latencyMs: number;
  success: boolean;
  errorMessage?: string;
  estimatedCost: number; // USD
  actualCost?: number; // USD from provider API if available
  budgetCategory: string; // 'user_chat', 'background_task', 'automation', etc.
  costTier: 'cheap' | 'balanced' | 'premium';
  metadata?: Record<string, any>;
}

export interface BudgetConfig {
  // Time-based budgets (USD)
  daily?: number;
  weekly?: number;
  monthly?: number;
  
  // Provider-specific budgets (USD)
  perProvider?: {
    openai?: number;
    anthropic?: number;
    google?: number;
  };
  
  // Feature-specific budgets (USD)
  perFeature?: {
    [feature: string]: number;
  };
  
  // User-specific budgets (USD)
  perUser?: {
    [userId: string]: number;
  };
  
  // Warning thresholds (percentage of budget)
  warningThreshold: number; // e.g., 0.8 for 80%
  criticalThreshold: number; // e.g., 0.95 for 95%
  
  // Hard limits
  hardLimitEnabled: boolean;
  
  // Auto-downgrade settings
  autoDowngrade: boolean;
  downgradeThreshold: number; // e.g., 0.9 for 90%
}

export interface BudgetStatus {
  period: 'daily' | 'weekly' | 'monthly';
  limit: number;
  spent: number;
  remaining: number;
  percentUsed: number;
  status: 'ok' | 'warning' | 'critical' | 'exceeded';
  projectedMonthlySpend?: number;
}

export interface UsageSummary {
  totalRequests: number;
  successfulRequests: number;
  failedRequests: number;
  totalTokens: number;
  totalCost: number;
  averageLatency: number;
  byProvider: {
    [provider: string]: {
      requests: number;
      tokens: number;
      cost: number;
    };
  };
  byModel: {
    [model: string]: {
      requests: number;
      tokens: number;
      cost: number;
    };
  };
  byFeature: {
    [feature: string]: {
      requests: number;
      tokens: number;
      cost: number;
    };
  };
  byCostTier: {
    cheap: { requests: number; cost: number };
    balanced: { requests: number; cost: number };
    premium: { requests: number; cost: number };
  };
}

export interface CostAlert {
  id: string;
  timestamp: number;
  severity: 'warning' | 'critical' | 'exceeded';
  type: 'daily' | 'weekly' | 'monthly' | 'provider' | 'feature' | 'user';
  message: string;
  currentSpend: number;
  limit: number;
  percentUsed: number;
  recommendation?: string;
}

export interface ModelPricing {
  provider: 'openai' | 'anthropic' | 'google';
  modelId: string;
  costPer1MInputTokens: number;
  costPer1MOutputTokens: number;
  lastUpdated: string;
}
