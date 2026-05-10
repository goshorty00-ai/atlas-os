// AI Core Types for ATLAS Smart Home

export type AIProvider = 'gpt' | 'claude' | 'gemini';

export interface ProviderConfig {
  enabled: boolean;
  apiKeyPresent: boolean;
  modelName?: string;
  timeout?: number;
  priority: number; // Lower number = higher priority
  maxRetries?: number;
}

export interface ProviderStatus {
  provider: AIProvider;
  available: boolean;
  reason?: string;
  lastError?: string;
  lastErrorTime?: number;
}

export interface AIMessage {
  role: 'system' | 'user' | 'assistant' | 'tool';
  content: string;
  toolCalls?: ToolCall[];
  toolCallId?: string;
}

export interface ToolCall {
  id: string;
  type: 'function';
  function: {
    name: string;
    arguments: string;
  };
}

export interface AIResponse {
  content: string;
  toolCalls?: ToolCall[];
  provider: AIProvider;
  confidence: number;
  requiresAction: boolean;
  structured?: StructuredResponse;
  usage?: UsageMetadata; // NEW: Token usage and cost tracking
}

export interface UsageMetadata {
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  model: string;
  provider: AIProvider;
  latencyMs: number;
  estimatedCost: number;
  estimated: boolean; // true if token counts are estimated, false if from provider
  finishReason?: string;
}

export interface StructuredResponse {
  type: 'device_list' | 'device_action' | 'scene_suggestion' | 'error' | 'confirmation';
  data: any;
  actions?: SuggestedAction[];
}

export interface SuggestedAction {
  id: string;
  label: string;
  type: 'device_control' | 'scene_apply' | 'provider_config';
  payload: any;
  confirmationRequired?: boolean;
}

export interface AITool {
  name: string;
  description: string;
  parameters: {
    type: 'object';
    properties: Record<string, any>;
    required: string[];
  };
  handler: (args: any) => Promise<any>;
}

export interface AIMemory {
  sessionId: string;
  userId?: string;
  shortTerm: Record<string, any>; // Current session context
  longTerm: Record<string, any>;  // Persistent preferences
  domain: Record<string, any>;    // Smart home specific memory
}

export interface AIContext {
  devices: any[];
  providers: any[];
  recentEvents: any[];
  userPreferences: any;
  currentRoom?: string;
  timeOfDay: 'morning' | 'afternoon' | 'evening' | 'night';
}

export interface TaskRoute {
  provider: AIProvider;
  reason: string;
  fallbacks: AIProvider[];
  availableProviders: AIProvider[]; // Providers that are actually enabled
}