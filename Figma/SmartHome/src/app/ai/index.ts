// ATLAS AI - Main exports

export { AtlasAI } from './AtlasAI';
export { AICore } from './AICore';
export { SmartHomeAgent } from './SmartHomeAgent';
export { MemoryManager } from './MemoryManager';
export { ProviderConfigManager } from './ProviderConfig';
export { useAtlasAI } from './useAtlasAI';

// Provider exports
export { ClaudeProvider } from './providers/ClaudeProvider';
export { GeminiProvider } from './providers/GeminiProvider';
export { MockAIProvider } from './providers/MockAIProvider';
export { 
  ProviderFactory, 
  getProviderFactory, 
  setProviderFactory,
  saveProviderMode,
  saveProviderCredentials
} from './providers/ProviderFactory';
export type { ProviderMode, ProviderCredentials } from './providers/ProviderFactory';

export type {
  AIProvider,
  AIMessage,
  AIResponse,
  StructuredResponse,
  SuggestedAction,
  AITool,
  AIMemory,
  AIContext,
  TaskRoute,
  ProviderConfig,
  ProviderStatus
} from './types';

export type { AtlasAIResponse } from './AtlasAI';