// ATLAS AI - Main orchestration service for smart home AI

import { AICore } from './AICore';
import { SmartHomeAgent } from './SmartHomeAgent';
import { MemoryManager } from './MemoryManager';
import { getProviderFactory } from './providers/ProviderFactory';
import type { SmartHomeSnapshot } from '../useSmartHome';
import type { StructuredResponse } from './types';

export interface AtlasAIResponse {
  response: string;
  structured?: StructuredResponse;
  actions?: any[];
  confidence: number;
  provider: string;
  processingTime: number;
  usage?: {
    inputTokens: number;
    outputTokens: number;
    totalTokens: number;
    model: string;
    estimatedCost: number;
    estimated: boolean;
  };
}

export class AtlasAI {
  private aiCore: AICore;
  private smartHomeAgent: SmartHomeAgent;
  private memoryManager: MemoryManager;
  private sessionId: string;

  constructor(
    executeAction: Function,
    isDeviceOn: Function,
    userId?: string
  ) {
    // Generate session ID
    this.sessionId = `session_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
    
    // Initialize memory
    this.memoryManager = new MemoryManager(this.sessionId, userId);
    
    // Load cost mode from localStorage
    const storedCostMode = localStorage.getItem('atlas_ai_cost_mode') as 'cheapest' | 'balanced' | 'best_quality' | null;
    const costMode = storedCostMode || 'balanced';
    
    // Initialize AI core with cost mode
    this.aiCore = new AICore(this.memoryManager.getMemory(), costMode);
    
    console.log(`[AtlasAI] Cost mode: ${costMode}`);
    
    // Get provider factory
    const factory = getProviderFactory();
    const mode = factory.getMode();
    const canCreate = factory.canCreateRealProviders();
    
    console.log(`[AtlasAI] Provider mode: ${mode}`);
    console.log(`[AtlasAI] Real providers available:`, canCreate);
    
    // Register AI providers using factory
    // GPT is disabled (no credits)
    this.aiCore.registerProvider('gpt', factory.createGPTProvider());
    
    // Claude is primary (real or mock based on mode)
    this.aiCore.registerProvider('claude', factory.createClaudeProvider());
    
    // Gemini is fallback (real or mock based on mode)
    this.aiCore.registerProvider('gemini', factory.createGeminiProvider());
    
    // Log provider configuration
    const providerConfig = this.aiCore.getProviderConfig();
    console.log('[AtlasAI] Provider configuration:', providerConfig.getSummary());
    
    // Initialize smart home agent
    this.smartHomeAgent = new SmartHomeAgent(
      this.aiCore,
      executeAction,
      isDeviceOn
    );
  }

  // Update smart home state
  updateState(state: SmartHomeSnapshot | null) {
    this.smartHomeAgent.updateState(state);
  }

  // Process user query
  async query(input: string): Promise<AtlasAIResponse> {
    const startTime = Date.now();
    
    try {
      // Track query in session memory
      this.memoryManager.setSessionMemory('lastQuery', input);
      this.memoryManager.setSessionMemory('lastQueryTime', startTime);
      
      // Process through smart home agent
      const result = await this.smartHomeAgent.processQuery(input);
      
      const processingTime = Date.now() - startTime;
      
      // Track successful interaction
      this.trackInteraction(input, result.response, processingTime);
      
      return {
        response: result.response,
        structured: result.structured,
        actions: result.actions,
        confidence: 0.8,
        provider: 'smart_home_agent',
        processingTime,
        usage: result.usage ? {
          inputTokens: result.usage.inputTokens,
          outputTokens: result.usage.outputTokens,
          totalTokens: result.usage.totalTokens,
          model: result.usage.model,
          estimatedCost: result.usage.estimatedCost,
          estimated: result.usage.estimated
        } : undefined
      };
      
    } catch (error) {
      console.error('ATLAS AI Error:', error);
      
      return {
        response: "I'm experiencing technical difficulties. Please try again in a moment.",
        confidence: 0.1,
        provider: 'error_handler',
        processingTime: Date.now() - startTime
      };
    }
  }
  
  // Get usage tracking services
  getUsageLedger() {
    return this.aiCore.getUsageLedger();
  }
  
  getBudgetManager() {
    return this.aiCore.getBudgetManager();
  }
  
  getCostEstimator() {
    return this.aiCore.getCostEstimator();
  }

  // Get current AI status for UI display
  getAIStatus(): {
    provider: string;
    model: string;
    costMode: string;
    spendToday: number;
    spendThisMonth: number;
    remainingBudget: number;
    lastRequest: {
      provider: string;
      model: string;
      inputTokens: number;
      outputTokens: number;
      totalTokens: number;
      estimatedCost: number;
      timestamp: number;
      success: boolean;
    } | null;
  } {
    const ledger = this.aiCore.getUsageLedger();
    const budgetManager = this.aiCore.getBudgetManager();
    const providerConfig = this.aiCore.getProviderConfig();
    
    const stats = ledger.getUsageStats();
    const monthlyStatus = budgetManager.getMonthlyStatus();
    const costMode = this.aiCore.getCostMode();
    
    // Get current provider (first enabled provider)
    const enabledProviders = providerConfig.getEnabledProviders();
    const currentProvider = enabledProviders[0] || 'none';
    
    // Map provider to display name
    const providerDisplayName = currentProvider === 'claude' ? 'Claude (Anthropic)' :
                                 currentProvider === 'gemini' ? 'Gemini (Google)' :
                                 currentProvider === 'gpt' ? 'GPT (OpenAI)' : 'None';
    
    // Get current model (would need to track this in AICore, for now use default)
    const currentModel = currentProvider === 'claude' ? 'claude-haiku-4' :
                         currentProvider === 'gemini' ? 'gemini-2.0-flash-exp' :
                         currentProvider === 'gpt' ? 'gpt-4o-mini' : 'unknown';
    
    return {
      provider: providerDisplayName,
      model: currentModel,
      costMode: costMode.replace('_', ' '),
      spendToday: stats.spendToday,
      spendThisMonth: stats.spendThisMonth,
      remainingBudget: monthlyStatus.remaining,
      lastRequest: stats.lastRequest ? {
        provider: stats.lastRequest.provider,
        model: stats.lastRequest.model,
        inputTokens: stats.lastRequest.inputTokens,
        outputTokens: stats.lastRequest.outputTokens,
        totalTokens: stats.lastRequest.totalTokens,
        estimatedCost: stats.lastRequest.estimatedCost,
        timestamp: stats.lastRequest.timestamp,
        success: stats.lastRequest.success
      } : null
    };
  }

  // Execute suggested action
  async executeAction(actionId: string, payload: any): Promise<boolean> {
    try {
      // Track action execution
      this.memoryManager.setSessionMemory('lastAction', {
        actionId,
        payload,
        timestamp: Date.now()
      });
      
      // TODO: Implement action execution based on type
      console.log('Executing action:', actionId, payload);
      
      return true;
    } catch (error) {
      console.error('Action execution failed:', error);
      return false;
    }
  }

  // Get conversation context
  getContext(): any {
    return {
      sessionId: this.sessionId,
      lastQuery: this.memoryManager.getSessionMemory('lastQuery'),
      frequentDevices: this.memoryManager.getFrequentlyUsedDevices(),
      responseStyle: this.memoryManager.getResponseStyle(),
      interactionCount: this.memoryManager.getSessionMemory('interactionCount') || 0
    };
  }

  // Set user preferences
  setPreference(key: string, value: any) {
    this.memoryManager.setLongTermMemory(key, value);
  }

  // Get user preferences
  getPreference(key: string): any {
    return this.memoryManager.getLongTermMemory(key);
  }

  // Remember device interaction
  rememberDeviceInteraction(deviceName: string, action: string) {
    this.memoryManager.trackDeviceUsage(deviceName);
    this.memoryManager.rememberDevicePreference(deviceName, {
      lastAction: action,
      lastInteraction: Date.now()
    });
  }

  // Get device suggestions based on usage patterns
  getDeviceSuggestions(): string[] {
    const timeOfDay = this.getTimeOfDay();
    const frequentDevices = this.memoryManager.getFrequentlyUsedDevices();
    
    // TODO: Implement time-based and usage-based suggestions
    return frequentDevices;
  }

  // Clear session
  clearSession() {
    this.memoryManager.clearSession();
    this.sessionId = `session_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
  }

  // Get provider configuration
  getProviderConfig() {
    return this.aiCore.getProviderConfig();
  }

  // Get memory stats
  getMemoryStats(): any {
    const memory = this.memoryManager.getMemory();
    return {
      sessionItems: Object.keys(memory.shortTerm).length,
      longTermItems: Object.keys(memory.longTerm).length,
      domainItems: Object.keys(memory.domain).length,
      devicePreferences: Object.keys(memory.domain.devicePreferences || {}).length,
      userRoutines: Object.keys(memory.domain.userRoutines || {}).length
    };
  }

  private trackInteraction(query: string, response: string, processingTime: number) {
    const count = this.memoryManager.getSessionMemory('interactionCount') || 0;
    this.memoryManager.setSessionMemory('interactionCount', count + 1);
    
    // Track performance
    const avgTime = this.memoryManager.getSessionMemory('avgProcessingTime') || 0;
    const newAvg = count === 0 ? processingTime : (avgTime * count + processingTime) / (count + 1);
    this.memoryManager.setSessionMemory('avgProcessingTime', newAvg);
    
    // Track query patterns
    const queryTypes = this.memoryManager.getSessionMemory('queryTypes') || {};
    const queryType = this.classifyQuery(query);
    queryTypes[queryType] = (queryTypes[queryType] || 0) + 1;
    this.memoryManager.setSessionMemory('queryTypes', queryTypes);
  }

  private classifyQuery(query: string): string {
    const queryLower = query.toLowerCase();
    
    if (queryLower.includes('turn on') || queryLower.includes('turn off')) return 'device_control';
    if (queryLower.includes('status') || queryLower.includes('overview')) return 'status_check';
    if (queryLower.includes('which') || queryLower.includes('list')) return 'device_query';
    if (queryLower.includes('scene') || queryLower.includes('routine')) return 'scene_control';
    if (queryLower.includes('offline') || queryLower.includes('problem')) return 'troubleshooting';
    
    return 'general';
  }

  private getTimeOfDay(): 'morning' | 'afternoon' | 'evening' | 'night' {
    const hour = new Date().getHours();
    if (hour < 6) return 'night';
    if (hour < 12) return 'morning';
    if (hour < 18) return 'afternoon';
    if (hour < 22) return 'evening';
    return 'night';
  }
}