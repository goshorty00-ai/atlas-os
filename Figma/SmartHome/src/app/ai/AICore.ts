// AI Core - Provider-agnostic orchestration layer

import type { 
  AIProvider, 
  AIMessage, 
  AIResponse, 
  TaskRoute, 
  AITool,
  AIMemory,
  AIContext 
} from './types';
import { ProviderConfigManager } from './ProviderConfig';
import { UsageLedger } from './usage/UsageLedger';
import { BudgetManager } from './usage/BudgetManager';
import { CostEstimator } from './usage/CostEstimator';

type CostTier = 'cheap' | 'balanced' | 'premium';

// Inline model info to avoid file system issues
interface ModelInfo {
  modelId: string;
  provider: string;
  tier: string;
  costTier: CostTier;
}

const SIMPLE_MODELS: ModelInfo[] = [
  { modelId: 'claude-haiku-4', provider: 'anthropic', tier: 'fast', costTier: 'cheap' },
  { modelId: 'claude-sonnet-4', provider: 'anthropic', tier: 'flagship', costTier: 'balanced' },
  { modelId: 'claude-opus-4', provider: 'anthropic', tier: 'flagship', costTier: 'premium' },
  { modelId: 'gemini-2.0-flash-exp', provider: 'google', tier: 'fast', costTier: 'cheap' },
  { modelId: 'gemini-1.5-flash', provider: 'google', tier: 'fast', costTier: 'cheap' },
  { modelId: 'gemini-1.5-pro', provider: 'google', tier: 'flagship', costTier: 'balanced' },
  { modelId: 'gpt-4o-mini', provider: 'openai', tier: 'fast', costTier: 'cheap' },
  { modelId: 'gpt-4o', provider: 'openai', tier: 'flagship', costTier: 'balanced' },
  { modelId: 'gpt-4-turbo', provider: 'openai', tier: 'flagship', costTier: 'premium' }
];

function getModelById(modelId: string): ModelInfo | undefined {
  return SIMPLE_MODELS.find(m => m.modelId === modelId);
}

function getModelsByCostTier(costTier: CostTier): ModelInfo[] {
  return SIMPLE_MODELS.filter(m => m.costTier === costTier);
}

export type CostMode = 'cheapest' | 'balanced' | 'best_quality';

export class AICore {
  private providers: Map<AIProvider, AIProviderInterface> = new Map();
  private tools: Map<string, AITool> = new Map();
  private memory: AIMemory;
  private providerConfig: ProviderConfigManager;
  private costMode: CostMode = 'balanced';
  private usageLedger: UsageLedger;
  private budgetManager: BudgetManager;
  private costEstimator: CostEstimator;

  constructor(memory: AIMemory, costMode: CostMode = 'balanced') {
    this.memory = memory;
    this.providerConfig = new ProviderConfigManager();
    this.costMode = costMode;
    
    // Initialize usage tracking
    this.usageLedger = new UsageLedger();
    this.budgetManager = new BudgetManager(this.usageLedger);
    this.costEstimator = this.usageLedger.getCostEstimator();
    
    console.log('[AICore] Usage tracking initialized');
  }

  // Get usage tracking services
  getUsageLedger(): UsageLedger {
    return this.usageLedger;
  }

  getBudgetManager(): BudgetManager {
    return this.budgetManager;
  }

  getCostEstimator(): CostEstimator {
    return this.costEstimator;
  }

  // Get/set cost mode
  getCostMode(): CostMode {
    return this.costMode;
  }

  setCostMode(mode: CostMode) {
    this.costMode = mode;
    console.log(`[AICore] Cost mode set to: ${mode}`);
  }

  // Get provider configuration manager
  getProviderConfig(): ProviderConfigManager {
    return this.providerConfig;
  }

  // Register AI providers
  registerProvider(provider: AIProvider, implementation: AIProviderInterface) {
    this.providers.set(provider, implementation);
  }

  // Register tools
  registerTool(tool: AITool) {
    this.tools.set(tool.name, tool);
  }

  // Route task to best provider and model with cost-aware selection
  routeTask(query: string, context: AIContext): TaskRoute & { modelId?: string; modelTier?: string; costTier?: CostTier } {
    const queryLower = query.toLowerCase();
    const enabledProviders = this.providerConfig.getEnabledProviders();
    
    if (enabledProviders.length === 0) {
      throw new Error('No AI providers are currently available');
    }
    
    // Classify task complexity and determine cost tier
    const taskClassification = this.classifyTaskWithCostTier(queryLower);
    const { taskType, costTier } = taskClassification;
    
    console.log(`[AICore] Task classified as: ${taskType}, Cost tier: ${costTier}, Mode: ${this.costMode}`);
    
    // Select model based on task type, cost tier, and cost mode
    const modelSelection = this.selectModelForTask(taskType, costTier, enabledProviders);
    
    if (!modelSelection) {
      throw new Error(`No suitable model found for task type: ${taskType}`);
    }
    
    const { provider, modelId, reason } = modelSelection;
    
    // Get fallback chain based on selected provider
    const fallbackChain = this.providerConfig.getFallbackChain(provider);
    const primaryProvider = fallbackChain[0];
    const fallbacks = fallbackChain.slice(1);
    
    // Get model info for tier display
    const modelInfo = modelId ? getModelById(modelId) : undefined;
    
    return {
      provider: primaryProvider,
      modelId: primaryProvider === provider ? modelId : undefined,
      modelTier: modelInfo?.tier,
      costTier: modelInfo?.costTier,
      reason: primaryProvider !== provider 
        ? `${reason} - Fallback to ${primaryProvider} (${provider} unavailable)`
        : reason,
      fallbacks,
      availableProviders: enabledProviders
    };
  }
  
  // Classify task and determine appropriate cost tier
  private classifyTaskWithCostTier(query: string): { taskType: string; costTier: CostTier } {
    // CHEAP TIER - Simple, low-risk tasks
    const cheapPatterns = [
      /^(turn on|turn off|switch|toggle|set|dim|brighten)/i,
      /^(show|list|display) (me )?(the |all )?/i,
      /offline|online|status/i,
      /^which (devices|lights|sensors)/i,
      /^is .+ (on|off|online|offline)/i
    ];
    
    if (cheapPatterns.some(p => p.test(query))) {
      return { taskType: 'simple_device_control', costTier: 'cheap' };
    }
    
    // Check for intent classification keywords
    if (query.includes('classify') || query.includes('categorize') || query.includes('filter')) {
      return { taskType: 'classification', costTier: 'cheap' };
    }
    
    // Check for formatting/rewriting
    if (query.includes('format') || query.includes('rewrite') || query.includes('rephrase')) {
      return { taskType: 'formatting', costTier: 'cheap' };
    }
    
    // PREMIUM TIER - Complex, high-value tasks
    const premiumPatterns = [
      /why .+ (fail|not work|broken|issue)/i,
      /debug|troubleshoot|diagnose/i,
      /plan|strategy|recommend|suggest|optimize/i,
      /analyze .+ (pattern|trend|behavior|usage)/i,
      /multi-step|complex|advanced/i,
      /explain .+ (in detail|thoroughly|comprehensively)/i
    ];
    
    if (premiumPatterns.some(p => p.test(query))) {
      // Override with cost mode
      if (this.costMode === 'cheapest') {
        return { taskType: 'complex_reasoning', costTier: 'balanced' };
      } else if (this.costMode === 'best_quality') {
        return { taskType: 'complex_reasoning', costTier: 'premium' };
      }
      return { taskType: 'complex_reasoning', costTier: 'balanced' };
    }
    
    // Multimodal detection
    if (this.needsMultimodal(query)) {
      return { taskType: 'multimodal', costTier: 'cheap' }; // Gemini Flash is cheap and has vision
    }
    
    // BALANCED TIER - Normal assistant queries
    const balancedPatterns = [
      /what (is|are|was|were)/i,
      /how (do|does|can|should)/i,
      /tell me about/i,
      /summary|summarize|overview/i
    ];
    
    if (balancedPatterns.some(p => p.test(query))) {
      // Apply cost mode
      if (this.costMode === 'cheapest') {
        return { taskType: 'normal_query', costTier: 'cheap' };
      }
      return { taskType: 'normal_query', costTier: 'balanced' };
    }
    
    // Default to cheap for unclassified queries
    return { taskType: 'general', costTier: 'cheap' };
  }
  
  // Select best model for task based on cost tier and mode
  private selectModelForTask(
    taskType: string, 
    costTier: CostTier, 
    enabledProviders: AIProvider[]
  ): { provider: AIProvider; modelId: string; reason: string } | null {
    
    // Get models by cost tier
    const tierModels = getModelsByCostTier(costTier);
    
    // Filter by enabled providers
    const availableModels = tierModels.filter(m => {
      if (m.provider === 'anthropic') return enabledProviders.includes('claude');
      if (m.provider === 'google') return enabledProviders.includes('gemini');
      if (m.provider === 'openai') return enabledProviders.includes('gpt');
      return false;
    });
    
    if (availableModels.length === 0) {
      console.warn(`[AICore] No models available for cost tier: ${costTier}`);
      return null;
    }
    
    // Select model based on task type and cost mode
    let selectedModel: ModelInfo | undefined;
    
    switch (costTier) {
      case 'cheap':
        // For cheap tier, always use the cheapest available model
        selectedModel = availableModels.sort((a, b) => 
          (a.costPer1MInputTokens || 0) - (b.costPer1MInputTokens || 0)
        )[0];
        break;
        
      case 'balanced':
        // For balanced tier, prefer Claude Sonnet or Gemini Pro
        if (this.costMode === 'cheapest') {
          selectedModel = availableModels.sort((a, b) => 
            (a.costPer1MInputTokens || 0) - (b.costPer1MInputTokens || 0)
          )[0];
        } else {
          selectedModel = availableModels.find(m => 
            m.modelId.includes('sonnet') || m.modelId.includes('pro')
          ) || availableModels[0];
        }
        break;
        
      case 'premium':
        // For premium tier, use Opus if available, otherwise best balanced
        selectedModel = availableModels.find(m => m.modelId.includes('opus')) 
          || availableModels.find(m => m.tier === 'flagship')
          || availableModels[0];
        break;
    }
    
    if (!selectedModel) {
      return null;
    }
    
    const provider = selectedModel.provider === 'anthropic' ? 'claude' :
                     selectedModel.provider === 'google' ? 'gemini' : 'gpt';
    
    const reason = `${taskType} task → ${costTier} tier → ${selectedModel.displayName} ($${selectedModel.costPer1MInputTokens}/M)`;
    
    return {
      provider,
      modelId: selectedModel.modelId,
      reason
    };
  }
  
  // Legacy task classification (kept for compatibility)
  private classifyTask(query: string): 'simple_query' | 'classification' | 'formatting' | 'multimodal' | 'complex_reasoning' | 'analysis' | 'coding' {
    // Multimodal detection
    if (this.needsMultimodal(query)) {
      return 'multimodal';
    }
    
    // Simple queries
    const simplePatterns = [
      /^(what|which|who|when|where) (is|are|was|were)/i,
      /^(turn on|turn off|switch|toggle|set)/i,
      /^(show|list|display) (me )?the/i,
      /offline|online|status/i
    ];
    if (simplePatterns.some(p => p.test(query))) {
      return 'simple_query';
    }
    
    // Classification tasks
    if (query.includes('classify') || query.includes('categorize') || query.includes('identify')) {
      return 'classification';
    }
    
    // Formatting tasks
    if (query.includes('format') || query.includes('convert') || query.includes('transform')) {
      return 'formatting';
    }
    
    // Complex reasoning
    if (this.needsReasoning(query)) {
      return 'complex_reasoning';
    }
    
    // Analysis
    if (this.needsAnalysis(query)) {
      return 'analysis';
    }
    
    // Coding (if we add code generation later)
    if (query.includes('code') || query.includes('script') || query.includes('automation')) {
      return 'coding';
    }
    
    // Default to simple
    return 'simple_query';
  }

  // Execute query with automatic provider routing, budget checks, and usage tracking
  async execute(
    query: string, 
    context: AIContext, 
    forceProvider?: AIProvider,
    forceModel?: string,
    feature: string = 'chat'
  ): Promise<AIResponse> {
    const startTime = Date.now();
    
    // 1. Route to appropriate provider and model
    const route = forceProvider ? 
      { 
        provider: forceProvider, 
        modelId: forceModel,
        costTier: 'balanced' as CostTier,
        reason: 'Forced', 
        fallbacks: [],
        availableProviders: this.providerConfig.getEnabledProviders()
      } :
      this.routeTask(query, context);

    const providers = [route.provider, ...route.fallbacks];
    
    console.log(`[AICore] Routing query to ${route.provider}${route.modelId ? ` (${route.modelId})` : ''}. Reason: ${route.reason}`);
    console.log(`[AICore] Fallback chain: ${providers.join(' -> ')}`);
    
    // 2. Estimate cost before execution
    const estimatedTokens = this.estimateTokens(query, context);
    const estimatedCost = route.modelId ? 
      this.costEstimator.estimateCost(route.modelId, estimatedTokens.input, estimatedTokens.output) : 
      0.001; // Default estimate if no model specified
    
    console.log(`[AICore] Estimated: ${estimatedTokens.total} tokens, $${estimatedCost.toFixed(6)}`);
    
    // 3. Check budget before execution
    const affordability = this.budgetManager.canAfford(
      estimatedCost,
      route.provider === 'claude' ? 'anthropic' : route.provider === 'gemini' ? 'google' : 'openai',
      feature,
      this.memory.userId
    );
    
    if (!affordability.allowed) {
      console.error(`[AICore] Budget check failed: ${affordability.reason}`);
      
      // Log failed request
      this.usageLedger.logUsage({
        provider: route.provider === 'claude' ? 'anthropic' : route.provider === 'gemini' ? 'google' : 'openai',
        model: route.modelId || 'unknown',
        feature,
        userId: this.memory.userId,
        sessionId: this.memory.sessionId,
        requestType: 'completion',
        inputTokens: 0,
        outputTokens: 0,
        totalTokens: 0,
        toolCallsUsed: [],
        latencyMs: Date.now() - startTime,
        success: false,
        errorMessage: affordability.reason,
        budgetCategory: feature,
        costTier: route.costTier || 'balanced'
      });
      
      throw new Error(`Budget exceeded: ${affordability.reason}`);
    }
    
    // 4. Check if should auto-downgrade
    if (this.budgetManager.shouldDowngrade() && this.costMode !== 'cheapest') {
      console.warn('[AICore] Budget threshold reached - auto-downgrading to cheapest mode');
      this.setCostMode('cheapest');
      // Re-route with cheaper model
      return this.execute(query, context, undefined, undefined, feature);
    }
    
    let lastError: Error | null = null;
    let attemptedProviders: string[] = [];
    
    // 5. Try providers in fallback chain
    for (const provider of providers) {
      // Check if provider is available
      if (!this.providerConfig.isAvailable(provider)) {
        const status = this.providerConfig.getStatus(provider);
        console.warn(`[AICore] Skipping ${provider}: ${status?.reason || 'unavailable'}`);
        attemptedProviders.push(`${provider}(unavailable)`);
        continue;
      }

      try {
        const impl = this.providers.get(provider);
        if (!impl) {
          console.warn(`[AICore] Provider ${provider} not registered, skipping`);
          attemptedProviders.push(`${provider}(not_registered)`);
          continue;
        }

        // Set model if specified and provider supports it
        const modelToUse = route.modelId && provider === route.provider ? route.modelId : undefined;
        if (modelToUse && 'setModel' in impl && typeof impl.setModel === 'function') {
          (impl as any).setModel(modelToUse);
        }

        console.log(`[AICore] Attempting ${provider}${modelToUse ? ` with ${modelToUse}` : ''}...`);
        attemptedProviders.push(provider);
        
        const requestStartTime = Date.now();
        const messages = this.buildMessages(query, context);
        const tools = Array.from(this.tools.values());
        
        const response = await impl.complete(messages, tools);
        const latency = Date.now() - requestStartTime;
        
        // 6. Extract usage metadata from response or estimate
        const usage = response.usage || this.estimateUsageFromResponse(response, modelToUse, latency);
        
        // 7. Handle tool calls
        if (response.toolCalls?.length) {
          console.log(`[AICore] ${provider} requested ${response.toolCalls.length} tool calls`);
          const toolResults = await this.executeTools(response.toolCalls);
          
          // Continue conversation with tool results
          const followupMessages = [
            ...messages,
            { role: 'assistant' as const, content: response.content, toolCalls: response.toolCalls },
            ...toolResults.map(result => ({
              role: 'tool' as const,
              content: JSON.stringify(result.result),
              toolCallId: result.toolCallId
            }))
          ];
          
          const finalResponse = await impl.complete(followupMessages, []);
          const finalLatency = Date.now() - requestStartTime;
          const finalUsage = finalResponse.usage || this.estimateUsageFromResponse(finalResponse, modelToUse, finalLatency);
          
          // 8. Log usage to ledger
          this.usageLedger.logUsage({
            provider: provider === 'claude' ? 'anthropic' : provider === 'gemini' ? 'google' : 'openai',
            model: modelToUse || usage.model || 'unknown',
            feature,
            userId: this.memory.userId,
            sessionId: this.memory.sessionId,
            requestType: 'completion',
            inputTokens: usage.inputTokens + finalUsage.inputTokens,
            outputTokens: usage.outputTokens + finalUsage.outputTokens,
            totalTokens: usage.totalTokens + finalUsage.totalTokens,
            toolCallsUsed: response.toolCalls.map(tc => tc.function.name),
            latencyMs: finalLatency,
            success: true,
            budgetCategory: feature,
            costTier: route.costTier || 'balanced'
          });
          
          console.log(`[AICore] ${provider} completed successfully with tools`);
          
          return {
            ...finalResponse,
            provider,
            confidence: this.calculateConfidence(finalResponse, provider === route.provider),
            usage: {
              ...finalUsage,
              inputTokens: usage.inputTokens + finalUsage.inputTokens,
              outputTokens: usage.outputTokens + finalUsage.outputTokens,
              totalTokens: usage.totalTokens + finalUsage.totalTokens,
              latencyMs: finalLatency
            }
          };
        }

        // 9. Log successful usage
        this.usageLedger.logUsage({
          provider: provider === 'claude' ? 'anthropic' : provider === 'gemini' ? 'google' : 'openai',
          model: modelToUse || usage.model || 'unknown',
          feature,
          userId: this.memory.userId,
          sessionId: this.memory.sessionId,
          requestType: 'completion',
          inputTokens: usage.inputTokens,
          outputTokens: usage.outputTokens,
          totalTokens: usage.totalTokens,
          toolCallsUsed: [],
          latencyMs: latency,
          success: true,
          budgetCategory: feature,
          costTier: route.costTier || 'balanced'
        });

        console.log(`[AICore] ${provider} completed successfully`);
        
        return {
          ...response,
          provider,
          confidence: this.calculateConfidence(response, provider === route.provider),
          usage
        };

      } catch (error: any) {
        lastError = error;
        const errorMsg = error?.message || String(error) || 'Unknown error';
        console.error(`[AICore] Provider ${provider} failed:`, errorMsg);
        
        // Log failed attempt
        this.usageLedger.logUsage({
          provider: provider === 'claude' ? 'anthropic' : provider === 'gemini' ? 'google' : 'openai',
          model: route.modelId || 'unknown',
          feature,
          userId: this.memory.userId,
          sessionId: this.memory.sessionId,
          requestType: 'completion',
          inputTokens: 0,
          outputTokens: 0,
          totalTokens: 0,
          toolCallsUsed: [],
          latencyMs: Date.now() - startTime,
          success: false,
          errorMessage: errorMsg,
          budgetCategory: feature,
          costTier: route.costTier || 'balanced'
        });
        
        // Record failure in provider config
        this.providerConfig.recordFailure(provider, errorMsg);
        
        // Continue to next provider
        continue;
      }
    }

    // All providers failed
    const availableProviders = route.availableProviders.join(', ') || 'none';
    const attemptedList = attemptedProviders.join(' -> ');
    throw new Error(`All available AI providers failed. Attempted: ${attemptedList}. Available: ${availableProviders}`);
  }
  
  // Estimate token count from query and context
  private estimateTokens(query: string, context: AIContext): { input: number; output: number; total: number } {
    // Rough estimation: ~4 characters per token
    const systemPrompt = this.buildSystemPrompt(context);
    const contextPrompt = this.buildContextPrompt(context);
    
    const inputChars = systemPrompt.length + contextPrompt.length + query.length;
    const inputTokens = Math.ceil(inputChars / 4);
    
    // Estimate output based on query complexity
    const outputTokens = query.length > 100 ? 200 : 100;
    
    return {
      input: inputTokens,
      output: outputTokens,
      total: inputTokens + outputTokens
    };
  }
  
  // Estimate usage from response when provider doesn't return it
  private estimateUsageFromResponse(response: AIResponse, model: string | undefined, latency: number): {
    inputTokens: number;
    outputTokens: number;
    totalTokens: number;
    model: string;
    provider: AIProvider;
    latencyMs: number;
    estimatedCost: number;
    estimated: boolean;
    finishReason?: string;
  } {
    // Estimate output tokens from response length
    const outputTokens = Math.ceil(response.content.length / 4);
    const inputTokens = 150; // Default estimate
    const totalTokens = inputTokens + outputTokens;
    
    const estimatedCost = model ? 
      this.costEstimator.estimateCost(model, inputTokens, outputTokens) : 
      0.0001;
    
    return {
      inputTokens,
      outputTokens,
      totalTokens,
      model: model || 'unknown',
      provider: response.provider,
      latencyMs: latency,
      estimatedCost,
      estimated: true
    };
  }

  private buildMessages(query: string, context: AIContext): AIMessage[] {
    const systemPrompt = this.buildSystemPrompt(context);
    const contextPrompt = this.buildContextPrompt(context);
    
    return [
      { role: 'system', content: systemPrompt },
      { role: 'user', content: `${contextPrompt}\n\nUser Query: ${query}` }
    ];
  }

  private buildSystemPrompt(context: AIContext): string {
    return `You are ATLAS, a premium smart home AI assistant. You are NOT a basic chatbot.

CORE BEHAVIOR:
- Answer naturally, clearly, and confidently only when justified
- NEVER invent facts, device states, or capabilities
- Use tools to get real data before answering
- Choose appropriate actions based on user intent
- Explain results concisely and professionally
- Maintain context across the conversation

AVAILABLE TOOLS:
${Array.from(this.tools.values()).map(tool => 
  `- ${tool.name}: ${tool.description}`
).join('\n')}

RESPONSE RULES:
- If you don't know current device state, use get_device_state tool
- If user wants to control devices, use device_control tool
- If user asks about rooms/groups, use get_room_devices tool
- For device issues, use get_device_events tool
- Always ground answers in real data
- Return structured responses when appropriate

TIME CONTEXT: ${context.timeOfDay}
${context.currentRoom ? `CURRENT ROOM: ${context.currentRoom}` : ''}`;
  }

  private buildContextPrompt(context: AIContext): string {
    const deviceSummary = `DEVICES (${context.devices.length} total):
${context.devices.slice(0, 10).map(d => 
  `- ${d.name} (${d.deviceType}, ${d.isOnline ? 'online' : 'offline'})`
).join('\n')}${context.devices.length > 10 ? `\n... and ${context.devices.length - 10} more` : ''}

PROVIDERS: ${context.providers.map(p => p.displayName).join(', ')}`;

    return deviceSummary;
  }

  private async executeTools(toolCalls: any[]): Promise<any[]> {
    const results = [];
    
    for (const call of toolCalls) {
      try {
        const tool = this.tools.get(call.function.name);
        if (!tool) {
          results.push({
            toolCallId: call.id,
            result: { error: `Tool ${call.function.name} not found` }
          });
          continue;
        }

        const args = JSON.parse(call.function.arguments);
        const result = await tool.handler(args);
        
        results.push({
          toolCallId: call.id,
          result
        });
      } catch (error: any) {
        results.push({
          toolCallId: call.id,
          result: { error: error?.message || String(error) }
        });
      }
    }
    
    return results;
  }

  private calculateConfidence(response: AIResponse, isPreferredProvider: boolean): number {
    let confidence = 0.7; // Base confidence
    
    if (isPreferredProvider) confidence += 0.1;
    if (response.toolCalls?.length) confidence += 0.1; // Used tools = more grounded
    if (response.structured) confidence += 0.1; // Structured response = more useful
    
    return Math.min(confidence, 1.0);
  }

  private needsReasoning(query: string): boolean {
    const reasoningKeywords = [
      'why', 'how', 'what if', 'should i', 'recommend', 'suggest',
      'turn off everything', 'all lights', 'scene', 'automation'
    ];
    return reasoningKeywords.some(keyword => query.includes(keyword));
  }

  private needsActions(query: string): boolean {
    const actionKeywords = [
      'turn on', 'turn off', 'set', 'change', 'adjust', 'dim',
      'brighten', 'switch', 'activate', 'deactivate'
    ];
    return actionKeywords.some(keyword => query.includes(keyword));
  }

  private needsAnalysis(query: string): boolean {
    const analysisKeywords = [
      'analyze', 'summary', 'report', 'status', 'overview',
      'which devices', 'list all', 'show me'
    ];
    return analysisKeywords.some(keyword => query.includes(keyword));
  }

  private needsMultimodal(query: string): boolean {
    const multimodalKeywords = [
      'image', 'picture', 'photo', 'video', 'file',
      'look at', 'see', 'visual', 'camera'
    ];
    return multimodalKeywords.some(keyword => query.includes(keyword));
  }
}

// Provider interface that each AI service must implement
export interface AIProviderInterface {
  complete(messages: AIMessage[], tools: AITool[]): Promise<AIResponse>;
}