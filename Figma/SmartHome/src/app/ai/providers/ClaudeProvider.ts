// Real Claude API Provider

import type { AIMessage, AIResponse, AITool } from '../types';
import type { AIProviderInterface } from '../AICore';

interface ClaudeMessage {
  role: 'user' | 'assistant';
  content: string | Array<{ type: string; text?: string; tool_use_id?: string; content?: string }>;
}

interface ClaudeTool {
  name: string;
  description: string;
  input_schema: {
    type: 'object';
    properties: Record<string, any>;
    required: string[];
  };
}

interface ClaudeResponse {
  id: string;
  type: 'message';
  role: 'assistant';
  content: Array<{
    type: 'text' | 'tool_use';
    text?: string;
    id?: string;
    name?: string;
    input?: any;
  }>;
  model: string;
  stop_reason: string;
  usage: {
    input_tokens: number;
    output_tokens: number;
  };
}

export class ClaudeProvider implements AIProviderInterface {
  private apiKey: string;
  private model: string;
  private apiUrl: string = 'https://api.anthropic.com/v1/messages';
  private timeout: number;
  private maxRetries: number;

  constructor(apiKey: string, model?: string, timeout: number = 30000, maxRetries: number = 2) {
    this.apiKey = apiKey;
    this.model = model || 'claude-3-5-sonnet-20241022'; // Default to flagship
    this.timeout = timeout;
    this.maxRetries = maxRetries;
  }

  // Allow model to be changed dynamically
  setModel(model: string) {
    this.model = model;
    console.log(`[ClaudeProvider] Model changed to ${model}`);
  }

  getModel(): string {
    return this.model;
  }

  async complete(messages: AIMessage[], tools: AITool[]): Promise<AIResponse> {
    console.log(`[ClaudeProvider] Starting request with ${messages.length} messages, ${tools.length} tools`);
    
    let lastError: Error | null = null;
    
    for (let attempt = 0; attempt <= this.maxRetries; attempt++) {
      if (attempt > 0) {
        console.log(`[ClaudeProvider] Retry attempt ${attempt}/${this.maxRetries}`);
        await this.delay(1000 * attempt); // Exponential backoff
      }
      
      try {
        const response = await this.makeRequest(messages, tools);
        return response;
      } catch (error: any) {
        lastError = error;
        console.error(`[ClaudeProvider] Attempt ${attempt + 1} failed:`, error.message);
        
        // Don't retry on certain errors
        if (error.message.includes('401') || error.message.includes('invalid_api_key')) {
          throw new Error(`Claude API authentication failed: ${error.message}`);
        }
        
        if (attempt === this.maxRetries) {
          throw new Error(`Claude API failed after ${this.maxRetries + 1} attempts: ${error.message}`);
        }
      }
    }
    
    throw lastError || new Error('Claude API request failed');
  }

  private async makeRequest(messages: AIMessage[], tools: AITool[]): Promise<AIResponse> {
    // Extract system message
    const systemMessage = messages.find(m => m.role === 'system');
    const conversationMessages = messages.filter(m => m.role !== 'system');
    
    // Convert messages to Claude format
    const claudeMessages = this.convertMessages(conversationMessages);
    
    // Convert tools to Claude format
    const claudeTools = tools.length > 0 ? this.convertTools(tools) : undefined;
    
    // Build request body
    const requestBody: any = {
      model: this.model,
      max_tokens: 4096,
      messages: claudeMessages
    };
    
    if (systemMessage) {
      requestBody.system = systemMessage.content;
    }
    
    if (claudeTools) {
      requestBody.tools = claudeTools;
    }
    
    console.log(`[ClaudeProvider] Request: ${this.model}, ${claudeMessages.length} messages, ${claudeTools?.length || 0} tools`);
    
    // Make API request with timeout
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), this.timeout);
    
    try {
      const response = await fetch(this.apiUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'x-api-key': this.apiKey,
          'anthropic-version': '2023-06-01'
        },
        body: JSON.stringify(requestBody),
        signal: controller.signal
      });
      
      clearTimeout(timeoutId);
      
      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Claude API error ${response.status}: ${errorText}`);
      }
      
      const data: ClaudeResponse = await response.json();
      
      console.log(`[ClaudeProvider] Response received: ${data.usage.input_tokens} in, ${data.usage.output_tokens} out`);
      
      return this.convertResponse(data);
      
    } catch (error: any) {
      clearTimeout(timeoutId);
      
      if (error.name === 'AbortError') {
        throw new Error(`Claude API request timed out after ${this.timeout}ms`);
      }
      
      throw error;
    }
  }

  private convertMessages(messages: AIMessage[]): ClaudeMessage[] {
    const claudeMessages: ClaudeMessage[] = [];
    
    for (const msg of messages) {
      if (msg.role === 'system') continue; // Handled separately
      
      if (msg.role === 'tool') {
        // Tool result - add as user message with tool_result content
        claudeMessages.push({
          role: 'user',
          content: [{
            type: 'tool_result',
            tool_use_id: msg.toolCallId || 'unknown',
            content: msg.content
          }]
        });
      } else if (msg.role === 'assistant' && msg.toolCalls?.length) {
        // Assistant message with tool calls
        const content: any[] = [];
        
        // Add text content if present
        if (msg.content) {
          content.push({ type: 'text', text: msg.content });
        }
        
        // Add tool use blocks
        for (const toolCall of msg.toolCalls) {
          content.push({
            type: 'tool_use',
            id: toolCall.id,
            name: toolCall.function.name,
            input: JSON.parse(toolCall.function.arguments)
          });
        }
        
        claudeMessages.push({
          role: 'assistant',
          content
        });
      } else {
        // Regular message
        claudeMessages.push({
          role: msg.role === 'user' ? 'user' : 'assistant',
          content: msg.content
        });
      }
    }
    
    return claudeMessages;
  }

  private convertTools(tools: AITool[]): ClaudeTool[] {
    return tools.map(tool => ({
      name: tool.name,
      description: tool.description,
      input_schema: {
        type: 'object',
        properties: tool.parameters.properties,
        required: tool.parameters.required
      }
    }));
  }

  private convertResponse(data: ClaudeResponse): AIResponse {
    let textContent = '';
    const toolCalls: any[] = [];
    
    // Extract text and tool calls from content blocks
    for (const block of data.content) {
      if (block.type === 'text' && block.text) {
        textContent += block.text;
      } else if (block.type === 'tool_use') {
        toolCalls.push({
          id: block.id || `tool_${Date.now()}`,
          type: 'function',
          function: {
            name: block.name || 'unknown',
            arguments: JSON.stringify(block.input || {})
          }
        });
      }
    }
    
    return {
      content: textContent || 'Processing...',
      toolCalls: toolCalls.length > 0 ? toolCalls : undefined,
      provider: 'claude',
      confidence: 0.9,
      requiresAction: toolCalls.length > 0
    };
  }

  private delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }
}