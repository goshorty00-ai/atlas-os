// Provider Factory - Creates real or mock providers based on environment

import type { AIProviderInterface } from '../AICore';
import { ClaudeProvider } from './ClaudeProvider';
import { GeminiProvider } from './GeminiProvider';
import { MockAIProvider } from './MockAIProvider';

export type ProviderMode = 'mock' | 'real';

export interface ProviderCredentials {
  claudeApiKey?: string;
  geminiApiKey?: string;
  openaiApiKey?: string;
}

export interface ProviderModelConfig {
  claudeModel?: string;
  geminiModel?: string;
}

export class ProviderFactory {
  private mode: ProviderMode;
  private credentials: ProviderCredentials;
  private modelConfig: ProviderModelConfig;

  constructor(
    mode: ProviderMode = 'mock', 
    credentials: ProviderCredentials = {},
    modelConfig: ProviderModelConfig = {}
  ) {
    this.mode = mode;
    this.credentials = credentials;
    this.modelConfig = modelConfig;
    
    console.log(`[ProviderFactory] Initialized in ${mode} mode`);
  }

  createClaudeProvider(modelId?: string): AIProviderInterface {
    if (this.mode === 'real' && this.credentials.claudeApiKey) {
      const model = modelId || this.modelConfig.claudeModel || 'claude-3-5-sonnet-20241022';
      
      console.log(`[ProviderFactory] Creating REAL Claude provider with model: ${model}`);
      return new ClaudeProvider(
        this.credentials.claudeApiKey,
        model,
        30000,
        2
      );
    }
    
    console.log('[ProviderFactory] Creating MOCK Claude provider');
    return new MockAIProvider('claude');
  }

  createGeminiProvider(modelId?: string): AIProviderInterface {
    if (this.mode === 'real' && this.credentials.geminiApiKey) {
      const model = modelId || this.modelConfig.geminiModel || 'gemini-1.5-flash-latest';
      
      console.log(`[ProviderFactory] Creating REAL Gemini provider with model: ${model}`);
      return new GeminiProvider(
        this.credentials.geminiApiKey,
        model,
        30000,
        2
      );
    }
    
    console.log('[ProviderFactory] Creating MOCK Gemini provider');
    return new MockAIProvider('gemini');
  }

  createGPTProvider(): AIProviderInterface {
    // GPT always returns mock (disabled)
    console.log('[ProviderFactory] Creating MOCK GPT provider (disabled)');
    return new MockAIProvider('gpt');
  }

  updateModelConfig(config: ProviderModelConfig) {
    this.modelConfig = { ...this.modelConfig, ...config };
    console.log('[ProviderFactory] Model config updated:', this.modelConfig);
  }

  getMode(): ProviderMode {
    return this.mode;
  }

  setMode(mode: ProviderMode) {
    this.mode = mode;
    console.log(`[ProviderFactory] Mode changed to ${mode}`);
  }

  updateCredentials(credentials: Partial<ProviderCredentials>) {
    this.credentials = { ...this.credentials, ...credentials };
    console.log('[ProviderFactory] Credentials updated');
  }

  // Check if real providers can be created
  canCreateRealProviders(): { claude: boolean; gemini: boolean; gpt: boolean } {
    return {
      claude: !!(this.credentials.claudeApiKey),
      gemini: !!(this.credentials.geminiApiKey),
      gpt: false // GPT disabled
    };
  }
}

// Singleton instance
let factoryInstance: ProviderFactory | null = null;

export function getProviderFactory(): ProviderFactory {
  if (!factoryInstance) {
    // Try to load from environment or localStorage
    const mode = getProviderMode();
    const credentials = getProviderCredentials();
    const modelConfig = getModelConfig();
    
    factoryInstance = new ProviderFactory(mode, credentials, modelConfig);
  }
  
  return factoryInstance;
}

export function setProviderFactory(factory: ProviderFactory) {
  factoryInstance = factory;
}

// Get provider mode from environment/storage
function getProviderMode(): ProviderMode {
  // Check localStorage first
  const stored = localStorage.getItem('atlas_ai_provider_mode');
  if (stored === 'real' || stored === 'mock') {
    return stored;
  }
  
  // Check if we have API keys in localStorage
  const hasClaudeKey = !!localStorage.getItem('atlas_ai_claude_key');
  const hasGeminiKey = !!localStorage.getItem('atlas_ai_gemini_key');
  
  // If we have keys, use real mode
  if (hasClaudeKey || hasGeminiKey) {
    return 'real';
  }
  
  // Default to mock for development
  return 'mock';
}

// Get provider credentials from storage
function getProviderCredentials(): ProviderCredentials {
  return {
    claudeApiKey: localStorage.getItem('atlas_ai_claude_key') || undefined,
    geminiApiKey: localStorage.getItem('atlas_ai_gemini_key') || undefined,
    openaiApiKey: undefined // GPT disabled
  };
}

// Get model configuration from storage
function getModelConfig(): ProviderModelConfig {
  return {
    claudeModel: localStorage.getItem('atlas_ai_claude_model') || undefined,
    geminiModel: localStorage.getItem('atlas_ai_gemini_model') || undefined
  };
}

// Save provider mode
export function saveProviderMode(mode: ProviderMode) {
  localStorage.setItem('atlas_ai_provider_mode', mode);
  console.log(`[ProviderFactory] Saved mode: ${mode}`);
}

// Save API keys
export function saveProviderCredentials(credentials: ProviderCredentials) {
  if (credentials.claudeApiKey) {
    localStorage.setItem('atlas_ai_claude_key', credentials.claudeApiKey);
  }
  if (credentials.geminiApiKey) {
    localStorage.setItem('atlas_ai_gemini_key', credentials.geminiApiKey);
  }
  console.log('[ProviderFactory] Saved credentials');
}