// Provider Configuration Manager

import type { AIProvider, ProviderConfig, ProviderStatus } from './types';

export class ProviderConfigManager {
  private configs: Map<AIProvider, ProviderConfig> = new Map();
  private statuses: Map<AIProvider, ProviderStatus> = new Map();

  constructor() {
    this.initializeDefaultConfigs();
  }

  private initializeDefaultConfigs() {
    // GPT - Disabled (no credits available)
    this.configs.set('gpt', {
      enabled: false,
      apiKeyPresent: false,
      modelName: 'gpt-4',
      timeout: 30000,
      priority: 1, // Would be highest priority if enabled
      maxRetries: 2
    });

    // Claude - Primary provider (enabled)
    this.configs.set('claude', {
      enabled: true,
      apiKeyPresent: true, // Mock for now
      modelName: 'claude-3-sonnet',
      timeout: 30000,
      priority: 1, // Highest priority while GPT disabled
      maxRetries: 2
    });

    // Gemini - Fallback provider (enabled)
    this.configs.set('gemini', {
      enabled: true,
      apiKeyPresent: true, // Mock for now
      modelName: 'gemini-pro',
      timeout: 30000,
      priority: 2, // Second priority
      maxRetries: 2
    });

    // Initialize statuses
    this.statuses.set('gpt', {
      provider: 'gpt',
      available: false,
      reason: 'Provider disabled - no credits available'
    });

    this.statuses.set('claude', {
      provider: 'claude',
      available: true,
      reason: 'Primary provider'
    });

    this.statuses.set('gemini', {
      provider: 'gemini',
      available: true,
      reason: 'Fallback provider'
    });
  }

  // Get provider configuration
  getConfig(provider: AIProvider): ProviderConfig | undefined {
    return this.configs.get(provider);
  }

  // Update provider configuration
  updateConfig(provider: AIProvider, config: Partial<ProviderConfig>) {
    const existing = this.configs.get(provider);
    if (existing) {
      this.configs.set(provider, { ...existing, ...config });
      
      // Update status based on config
      this.updateStatus(provider, {
        available: config.enabled ?? existing.enabled,
        reason: config.enabled ? 'Provider enabled' : 'Provider disabled'
      });
    }
  }

  // Get provider status
  getStatus(provider: AIProvider): ProviderStatus | undefined {
    return this.statuses.get(provider);
  }

  // Update provider status
  updateStatus(provider: AIProvider, update: Partial<ProviderStatus>) {
    const existing = this.statuses.get(provider);
    if (existing) {
      this.statuses.set(provider, { ...existing, ...update });
    }
  }

  // Record provider failure
  recordFailure(provider: AIProvider, error: string) {
    const status = this.statuses.get(provider);
    if (status) {
      this.statuses.set(provider, {
        ...status,
        lastError: error,
        lastErrorTime: Date.now()
      });
    }
    
    console.warn(`[ProviderConfig] ${provider} failed:`, error);
  }

  // Check if provider is available
  isAvailable(provider: AIProvider): boolean {
    const config = this.configs.get(provider);
    const status = this.statuses.get(provider);
    
    return !!(config?.enabled && config?.apiKeyPresent && status?.available);
  }

  // Get all enabled providers sorted by priority
  getEnabledProviders(): AIProvider[] {
    const enabled: Array<{ provider: AIProvider; priority: number }> = [];
    
    for (const [provider, config] of this.configs.entries()) {
      if (this.isAvailable(provider)) {
        enabled.push({ provider, priority: config.priority });
      }
    }
    
    // Sort by priority (lower number = higher priority)
    enabled.sort((a, b) => a.priority - b.priority);
    
    return enabled.map(e => e.provider);
  }

  // Get provider fallback chain
  getFallbackChain(preferredProvider: AIProvider): AIProvider[] {
    const enabled = this.getEnabledProviders();
    
    // If preferred provider is available, put it first
    if (enabled.includes(preferredProvider)) {
      return [
        preferredProvider,
        ...enabled.filter(p => p !== preferredProvider)
      ];
    }
    
    // Otherwise return all enabled providers in priority order
    return enabled;
  }

  // Get configuration summary
  getSummary(): Record<AIProvider, { enabled: boolean; available: boolean; priority: number; reason?: string }> {
    const summary: any = {};
    
    for (const provider of ['gpt', 'claude', 'gemini'] as AIProvider[]) {
      const config = this.configs.get(provider);
      const status = this.statuses.get(provider);
      
      summary[provider] = {
        enabled: config?.enabled ?? false,
        available: this.isAvailable(provider),
        priority: config?.priority ?? 999,
        reason: status?.reason
      };
    }
    
    return summary;
  }

  // Enable provider
  enableProvider(provider: AIProvider, apiKey?: string) {
    this.updateConfig(provider, {
      enabled: true,
      apiKeyPresent: !!apiKey
    });
    
    console.log(`[ProviderConfig] Enabled ${provider}`);
  }

  // Disable provider
  disableProvider(provider: AIProvider, reason?: string) {
    this.updateConfig(provider, { enabled: false });
    this.updateStatus(provider, {
      available: false,
      reason: reason || 'Provider disabled'
    });
    
    console.log(`[ProviderConfig] Disabled ${provider}: ${reason || 'manual'}`);
  }
}