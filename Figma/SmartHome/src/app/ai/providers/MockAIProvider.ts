// Mock AI Provider for development and testing

import type { AIMessage, AIResponse, AITool } from '../types';
import type { AIProviderInterface } from '../AICore';

export class MockAIProvider implements AIProviderInterface {
  private provider: 'gpt' | 'claude' | 'gemini';

  constructor(provider: 'gpt' | 'claude' | 'gemini') {
    this.provider = provider;
  }

  async complete(messages: AIMessage[], tools: AITool[]): Promise<AIResponse> {
    const userMessage = messages.find(m => m.role === 'user')?.content || '';
    const queryLower = userMessage.toLowerCase();

    console.log(`[MockAIProvider:${this.provider}] Processing query: "${userMessage.substring(0, 50)}..."`);

    // Simulate different provider behaviors
    if (this.provider === 'claude') {
      return this.handleClaudeQuery(queryLower, tools);
    } else if (this.provider === 'gemini') {
      return this.handleGeminiQuery(queryLower, tools);
    } else {
      // GPT disabled
      throw new Error('GPT provider is currently disabled - no credits available');
    }
  }

  private async handleGPTQuery(query: string, tools: AITool[]): Promise<AIResponse> {
    // GPT is disabled - throw error
    throw new Error('GPT provider is currently disabled due to insufficient credits');
  }

  private async handleClaudeQuery(query: string, tools: AITool[]): Promise<AIResponse> {
    // Claude: Primary for reasoning, orchestration, analysis, and actions (while GPT disabled)
    
    // Device control actions
    if (query.includes('turn on') || query.includes('turn off')) {
      const deviceName = this.extractDeviceName(query);
      const action = query.includes('turn on') ? 'on' : 'off';
      
      return {
        content: `I'll ${action === 'on' ? 'turn on' : 'turn off'} the ${deviceName} for you.`,
        toolCalls: [{
          id: 'call_claude_1',
          type: 'function',
          function: {
            name: 'device_control',
            arguments: JSON.stringify({ deviceName, action })
          }
        }],
        provider: 'claude',
        confidence: 0.9,
        requiresAction: true,
        structured: {
          type: 'device_action',
          data: { deviceName, action },
          actions: [{
            id: 'device_control_1',
            label: `${action === 'on' ? 'Turn On' : 'Turn Off'} ${deviceName}`,
            type: 'device_control',
            payload: { deviceName, action }
          }]
        }
      };
    }

    // Status and analysis queries
    if (query.includes('status') || query.includes('overview')) {
      return {
        content: "I'll provide you with a comprehensive overview of your smart home system.",
        toolCalls: [{
          id: 'call_claude_2',
          type: 'function',
          function: {
            name: 'get_device_state',
            arguments: JSON.stringify({ deviceName: 'all' })
          }
        }],
        provider: 'claude',
        confidence: 0.85,
        requiresAction: false,
        structured: {
          type: 'device_list',
          data: {},
          actions: []
        }
      };
    }

    // Offline device queries
    if (query.includes('offline') || query.includes('unavailable')) {
      return {
        content: "Let me check which devices are currently offline and analyze potential issues.",
        toolCalls: [{
          id: 'call_claude_3',
          type: 'function',
          function: {
            name: 'get_offline_devices',
            arguments: JSON.stringify({})
          }
        }],
        provider: 'claude',
        confidence: 0.9,
        requiresAction: false
      };
    }

    // Device queries by state
    if (query.includes('which') && (query.includes('on') || query.includes('off'))) {
      return {
        content: "Let me check which devices are currently on.",
        toolCalls: [{
          id: 'call_claude_4',
          type: 'function',
          function: {
            name: 'get_device_state',
            arguments: JSON.stringify({ deviceName: 'all' })
          }
        }],
        provider: 'claude',
        confidence: 0.85,
        requiresAction: false
      };
    }

    // Room-based queries
    if (query.includes('kitchen') || query.includes('bedroom') || query.includes('living room')) {
      const room = query.includes('kitchen') ? 'kitchen' : 
                   query.includes('bedroom') ? 'bedroom' : 'living room';
      
      return {
        content: `I'll check the devices in the ${room}.`,
        toolCalls: [{
          id: 'call_claude_5',
          type: 'function',
          function: {
            name: 'get_room_devices',
            arguments: JSON.stringify({ room })
          }
        }],
        provider: 'claude',
        confidence: 0.85,
        requiresAction: false
      };
    }

    // Default response
    return {
      content: "I can help you analyze your smart home system. What specific information would you like me to review?",
      provider: 'claude',
      confidence: 0.7,
      requiresAction: false
    };
  }

  private async handleGeminiQuery(query: string, tools: AITool[]): Promise<AIResponse> {
    // Gemini: Fallback for reasoning, primary for multimodal
    
    // Device type queries
    if (query.includes('lights') || query.includes('light')) {
      return {
        content: "I'll check your lighting devices.",
        toolCalls: [{
          id: 'call_gemini_1',
          type: 'function',
          function: {
            name: 'get_devices_by_type',
            arguments: JSON.stringify({ deviceType: 'light' })
          }
        }],
        provider: 'gemini',
        confidence: 0.8,
        requiresAction: false
      };
    }

    // Camera/visual queries
    if (query.includes('camera') || query.includes('video')) {
      return {
        content: "I'll check your camera devices.",
        toolCalls: [{
          id: 'call_gemini_2',
          type: 'function',
          function: {
            name: 'get_devices_by_type',
            arguments: JSON.stringify({ deviceType: 'camera' })
          }
        }],
        provider: 'gemini',
        confidence: 0.85,
        requiresAction: false
      };
    }

    // Fallback for device control if Claude failed
    if (query.includes('turn on') || query.includes('turn off')) {
      const deviceName = this.extractDeviceName(query);
      const action = query.includes('turn on') ? 'on' : 'off';
      
      return {
        content: `I'll ${action === 'on' ? 'turn on' : 'turn off'} the ${deviceName}.`,
        toolCalls: [{
          id: 'call_gemini_3',
          type: 'function',
          function: {
            name: 'device_control',
            arguments: JSON.stringify({ deviceName, action })
          }
        }],
        provider: 'gemini',
        confidence: 0.8,
        requiresAction: true
      };
    }

    // General fallback
    return {
      content: "I'm here to help with your smart home. What would you like to know or control?",
      provider: 'gemini',
      confidence: 0.6,
      requiresAction: false
    };
  }

  private extractDeviceName(query: string): string {
    // Simple device name extraction
    const words = query.split(' ');
    const turnIndex = words.findIndex(w => w.includes('turn'));
    if (turnIndex >= 0 && turnIndex + 2 < words.length) {
      return words.slice(turnIndex + 2).join(' ');
    }
    return 'device';
  }
}