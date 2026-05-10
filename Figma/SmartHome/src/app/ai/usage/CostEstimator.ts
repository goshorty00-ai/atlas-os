// Cost Estimator - Calculate costs from token usage

import type { ModelPricing } from './types';

export class CostEstimator {
  private pricingTable: Map<string, ModelPricing> = new Map();

  constructor() {
    this.initializePricingTable();
  }

  // Initialize pricing table with current model prices
  private initializePricingTable() {
    const pricing: ModelPricing[] = [
      // Anthropic Claude
      { provider: 'anthropic', modelId: 'claude-3-5-sonnet-20241022', costPer1MInputTokens: 3.00, costPer1MOutputTokens: 15.00, lastUpdated: '2024-01-15' },
      { provider: 'anthropic', modelId: 'claude-3-5-sonnet-20240620', costPer1MInputTokens: 3.00, costPer1MOutputTokens: 15.00, lastUpdated: '2024-01-15' },
      { provider: 'anthropic', modelId: 'claude-3-opus-20240229', costPer1MInputTokens: 15.00, costPer1MOutputTokens: 75.00, lastUpdated: '2024-01-15' },
      { provider: 'anthropic', modelId: 'claude-3-sonnet-20240229', costPer1MInputTokens: 3.00, costPer1MOutputTokens: 15.00, lastUpdated: '2024-01-15' },
      { provider: 'anthropic', modelId: 'claude-3-5-haiku-20241022', costPer1MInputTokens: 1.00, costPer1MOutputTokens: 5.00, lastUpdated: '2024-01-15' },
      { provider: 'anthropic', modelId: 'claude-3-haiku-20240307', costPer1MInputTokens: 0.25, costPer1MOutputTokens: 1.25, lastUpdated: '2024-01-15' },
      
      // Google Gemini
      { provider: 'google', modelId: 'gemini-2.0-flash-exp', costPer1MInputTokens: 0.00, costPer1MOutputTokens: 0.00, lastUpdated: '2024-01-15' },
      { provider: 'google', modelId: 'gemini-1.5-pro-latest', costPer1MInputTokens: 1.25, costPer1MOutputTokens: 5.00, lastUpdated: '2024-01-15' },
      { provider: 'google', modelId: 'gemini-1.5-pro-002', costPer1MInputTokens: 1.25, costPer1MOutputTokens: 5.00, lastUpdated: '2024-01-15' },
      { provider: 'google', modelId: 'gemini-1.5-flash-latest', costPer1MInputTokens: 0.075, costPer1MOutputTokens: 0.30, lastUpdated: '2024-01-15' },
      { provider: 'google', modelId: 'gemini-1.5-flash-002', costPer1MInputTokens: 0.075, costPer1MOutputTokens: 0.30, lastUpdated: '2024-01-15' },
      { provider: 'google', modelId: 'gemini-1.5-flash-8b-latest', costPer1MInputTokens: 0.0375, costPer1MOutputTokens: 0.15, lastUpdated: '2024-01-15' },
      { provider: 'google', modelId: 'gemini-1.0-pro', costPer1MInputTokens: 0.50, costPer1MOutputTokens: 1.50, lastUpdated: '2024-01-15' },
      
      // OpenAI GPT
      { provider: 'openai', modelId: 'gpt-4o', costPer1MInputTokens: 2.50, costPer1MOutputTokens: 10.00, lastUpdated: '2024-01-15' },
      { provider: 'openai', modelId: 'gpt-4o-2024-11-20', costPer1MInputTokens: 2.50, costPer1MOutputTokens: 10.00, lastUpdated: '2024-01-15' },
      { provider: 'openai', modelId: 'gpt-4o-mini', costPer1MInputTokens: 0.15, costPer1MOutputTokens: 0.60, lastUpdated: '2024-01-15' },
      { provider: 'openai', modelId: 'gpt-4-turbo', costPer1MInputTokens: 10.00, costPer1MOutputTokens: 30.00, lastUpdated: '2024-01-15' },
      { provider: 'openai', modelId: 'gpt-3.5-turbo', costPer1MInputTokens: 0.50, costPer1MOutputTokens: 1.50, lastUpdated: '2024-01-15' },
    ];

    pricing.forEach(p => {
      this.pricingTable.set(p.modelId, p);
    });
  }

  // Estimate cost for a request
  estimateCost(modelId: string, inputTokens: number, outputTokens: number): number {
    const pricing = this.pricingTable.get(modelId);
    
    if (!pricing) {
      console.warn(`[CostEstimator] No pricing found for model: ${modelId}`);
      return 0;
    }

    const inputCost = (inputTokens / 1000000) * pricing.costPer1MInputTokens;
    const outputCost = (outputTokens / 1000000) * pricing.costPer1MOutputTokens;
    
    return inputCost + outputCost;
  }

  // Get pricing for a model
  getPricing(modelId: string): ModelPricing | undefined {
    return this.pricingTable.get(modelId);
  }

  // Get all pricing
  getAllPricing(): ModelPricing[] {
    return Array.from(this.pricingTable.values());
  }

  // Update pricing for a model
  updatePricing(pricing: ModelPricing) {
    this.pricingTable.set(pricing.modelId, pricing);
    console.log(`[CostEstimator] Updated pricing for ${pricing.modelId}`);
  }

  // Calculate cost per 1K tokens (for display)
  costPer1KTokens(modelId: string, type: 'input' | 'output'): number {
    const pricing = this.pricingTable.get(modelId);
    if (!pricing) return 0;
    
    const costPer1M = type === 'input' ? pricing.costPer1MInputTokens : pricing.costPer1MOutputTokens;
    return costPer1M / 1000;
  }

  // Estimate cost for different token counts (for planning)
  estimateScenarios(modelId: string): {
    small: { tokens: number; cost: number };
    medium: { tokens: number; cost: number };
    large: { tokens: number; cost: number };
  } {
    return {
      small: { tokens: 1000, cost: this.estimateCost(modelId, 500, 500) },
      medium: { tokens: 10000, cost: this.estimateCost(modelId, 5000, 5000) },
      large: { tokens: 100000, cost: this.estimateCost(modelId, 50000, 50000) }
    };
  }
}
