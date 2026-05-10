// React hook for ATLAS AI integration

import { useState, useEffect, useRef, useCallback } from 'react';
import { AtlasAI, type AtlasAIResponse } from './AtlasAI';
import { useSmartHomeContext } from '../SmartHomeContext';

export interface AIMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  timestamp: number;
  structured?: any;
  actions?: any[];
  usage?: {
    inputTokens: number;
    outputTokens: number;
    totalTokens: number;
    model: string;
    estimatedCost: number;
    estimated: boolean;
  };
}

export interface AIState {
  isProcessing: boolean;
  messages: AIMessage[];
  error: string | null;
  context: any;
}

export function useAtlasAI() {
  const { state, executeAction, isDeviceOn } = useSmartHomeContext();
  const [aiState, setAIState] = useState<AIState>({
    isProcessing: false,
    messages: [],
    error: null,
    context: null
  });
  
  const atlasRef = useRef<AtlasAI | null>(null);
  const messageIdRef = useRef(0);

  // Initialize ATLAS AI
  useEffect(() => {
    if (!atlasRef.current) {
      atlasRef.current = new AtlasAI(executeAction, isDeviceOn);
      
      // Update context with provider config
      const providerConfig = atlasRef.current.getProviderConfig();
      setAIState(prev => ({
        ...prev,
        context: {
          ...atlasRef.current?.getContext(),
          providerConfig: providerConfig.getSummary()
        }
      }));
    }
  }, [executeAction, isDeviceOn]);

  // Update AI state when smart home state changes
  useEffect(() => {
    if (atlasRef.current && state) {
      atlasRef.current.updateState(state);
    }
  }, [state]);

  // Send query to AI
  const sendQuery = useCallback(async (query: string): Promise<void> => {
    if (!atlasRef.current || !query.trim()) return;

    const messageId = `msg_${++messageIdRef.current}`;
    const userMessage: AIMessage = {
      id: messageId,
      role: 'user',
      content: query.trim(),
      timestamp: Date.now()
    };

    // Add user message
    setAIState(prev => ({
      ...prev,
      messages: [...prev.messages, userMessage],
      isProcessing: true,
      error: null
    }));

    try {
      const response = await atlasRef.current.query(query);
      
      const assistantMessage: AIMessage = {
        id: `msg_${++messageIdRef.current}`,
        role: 'assistant',
        content: response.response,
        timestamp: Date.now(),
        structured: response.structured,
        actions: response.actions,
        usage: response.usage
      };

      setAIState(prev => ({
        ...prev,
        messages: [...prev.messages, assistantMessage],
        isProcessing: false,
        context: atlasRef.current?.getContext()
      }));

    } catch (error) {
      console.error('AI Query Error:', error);
      
      const errorMessage: AIMessage = {
        id: `msg_${++messageIdRef.current}`,
        role: 'assistant',
        content: "I'm sorry, I encountered an error processing your request. Please try again.",
        timestamp: Date.now()
      };

      setAIState(prev => ({
        ...prev,
        messages: [...prev.messages, errorMessage],
        isProcessing: false,
        error: error.message
      }));
    }
  }, []);

  // Execute suggested action
  const executeAIAction = useCallback(async (actionId: string, payload: any): Promise<boolean> => {
    if (!atlasRef.current) return false;

    try {
      const success = await atlasRef.current.executeAction(actionId, payload);
      
      if (success) {
        // Add confirmation message
        const confirmMessage: AIMessage = {
          id: `msg_${++messageIdRef.current}`,
          role: 'assistant',
          content: `Action completed successfully.`,
          timestamp: Date.now()
        };

        setAIState(prev => ({
          ...prev,
          messages: [...prev.messages, confirmMessage]
        }));
      }

      return success;
    } catch (error) {
      console.error('Action execution error:', error);
      return false;
    }
  }, []);

  // Clear conversation
  const clearConversation = useCallback(() => {
    setAIState(prev => ({
      ...prev,
      messages: [],
      error: null
    }));
    
    if (atlasRef.current) {
      atlasRef.current.clearSession();
    }
  }, []);

  // Set AI preference
  const setPreference = useCallback((key: string, value: any) => {
    if (atlasRef.current) {
      atlasRef.current.setPreference(key, value);
    }
  }, []);

  // Get AI preference
  const getPreference = useCallback((key: string): any => {
    return atlasRef.current?.getPreference(key);
  }, []);

  // Remember device interaction
  const rememberDeviceInteraction = useCallback((deviceName: string, action: string) => {
    if (atlasRef.current) {
      atlasRef.current.rememberDeviceInteraction(deviceName, action);
    }
  }, []);

  // Get device suggestions
  const getDeviceSuggestions = useCallback((): string[] => {
    return atlasRef.current?.getDeviceSuggestions() || [];
  }, []);

  // Get memory stats
  const getMemoryStats = useCallback(() => {
    return atlasRef.current?.getMemoryStats() || {};
  }, []);

  // Get usage tracking services
  const getUsageLedger = useCallback(() => {
    return atlasRef.current?.getUsageLedger();
  }, []);

  const getBudgetManager = useCallback(() => {
    return atlasRef.current?.getBudgetManager();
  }, []);

  const getCostEstimator = useCallback(() => {
    return atlasRef.current?.getCostEstimator();
  }, []);

  // Get AI status for UI display
  const getAIStatus = useCallback(() => {
    return atlasRef.current?.getAIStatus();
  }, []);

  // Quick actions for common queries
  const quickActions = {
    getAllDeviceStatus: () => sendQuery("Show me the status of all my devices"),
    getOfflineDevices: () => sendQuery("Which devices are offline?"),
    turnOffAllLights: () => sendQuery("Turn off all lights"),
    getKitchenDevices: () => sendQuery("What devices are in the kitchen?"),
    getBrightness: () => sendQuery("What's the brightness of my lights?")
  };

  return {
    // State
    ...aiState,
    
    // Actions
    sendQuery,
    executeAIAction,
    clearConversation,
    setPreference,
    getPreference,
    rememberDeviceInteraction,
    getDeviceSuggestions,
    getMemoryStats,
    getUsageLedger,
    getBudgetManager,
    getCostEstimator,
    getAIStatus,
    
    // Quick actions
    quickActions,
    
    // Utilities
    isReady: !!atlasRef.current,
    hasMessages: aiState.messages.length > 0
  };
}