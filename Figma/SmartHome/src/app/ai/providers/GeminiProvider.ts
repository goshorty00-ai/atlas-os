// Real Gemini API Provider

import type { AIMessage, AIResponse, AITool } from '../types';
import type { AIProviderInterface } from '../AICore';

interface GeminiContent {
  role: 'user' | 'model';
  parts: Array<{ text?: string; functionCall?: any; functionResponse?: any }>;
}

interface GeminiTool {
  functionDeclarations: Array<{
    name: string;
    description: string;
    parameters: {
      type: 'object';
      properties: Record<string, any>;
      required: string[];
    };
  }>;
}

interface GeminiResponse {
  candidates: Array<{
    content: {
      parts: Array<{
        text?: string;
        functionCall?: {
          name: string;
          args: any;
        };
      }>;
      role: string;
    };
    finishReason: string;
  }>;
  usageMetadata?: {
    promptTokenCount: number;
    candidatesTokenCount: number;
    totalTokenCount: number;
  };
}

export class GeminiProvider implements AIProviderInterface {
  private apiKey: string;
  private model: string;
  private timeout: number;
  private maxRetries: number;

  constructor(apiKey: string, model?: string, timeout: number = 30000, maxRetries: number = 2) {
    this.apiKey = apiKey;
    this.model = model || 'gemini-1.5-flash-latest'; // Default to fast
    this.timeout = timeout;
    this.maxRetries = maxRetries;
  }

  // Allow model to be changed dynamically
  setModel(model: string) {
    this.model = model;
    console.log(`[GeminiProvider] Model changed to ${model}`);
  }

  getModel(): string {
    return this.model;
  }

  private getApiUrl(): string {
    return `https://generativelanguage.googleapis.com/v1beta/models/${this.model}:generateContent`;
  }

  async complete(messages: AIMessage[], tools: AITool[]): Promise<AIResponse> {
    console.log(`[GeminiProvider] Starting request with ${messages.length} messages, ${tools.length} tools`);
    
    let lastError: Error | null = null;
    
    for (let attempt = 0; attempt <= this.maxRetries; attempt++) {
      if (attempt > 0) {
        console.log(`[GeminiProvider] Retry attempt ${attempt}/${this.maxRetries}`);
        await this.delay(1000 * attempt);
      }
      
      try {
        const response = await this.makeRequest(messages, tools);
        return response;
      } catch (error: any) {
        lastError = error;
        console.error(`[GeminiProvider] Attempt ${attempt + 1} failed:`, error.message);
        
        // Don't retry on auth errors
        if (error.message.includes('401') || error.message.includes('403') || error.message.includes('API_KEY')) {
          throw new Error(`Gemini API authentication failed: ${error.message}`);
        }
        
        if (attempt === this.maxRetries) {
          throw new Error(`Gemini API failed after ${this.maxRetries + 1} attempts: ${error.message}`);
        }
      }
    }
    
    throw lastError || new Error('Gemini API request failed');
  }

  private async makeRequest(messages: AIMessage[], tools: AITool[]): Promise<AIResponse> {
    // Extract system instruction
    const systemMessage = messages.find(m => m.role === 'system');
    const conversationMessages = messages.filter(m => m.role !== 'system');
    
    // Convert messages to Gemini format
    const geminiContents = this.convertMessages(conversationMessages);
    
    // Convert tools to Gemini format
    const geminiTools = tools.length > 0 ? this.convertTools(tools) : undefined;
    
    // Build request body
    const requestBody: any = {
      contents: geminiContents
    };
    
    if (systemMessage) {
      requestBody.systemInstruction = {
        parts: [{ text: systemMessage.content }]
      };
    }
    
    if (geminiTools) {
      requestBody.tools = [geminiTools];
    }
    
    // Add generation config
    requestBody.generationConfig = {
      temperature: 0.7,
      maxOutputTokens: 4096
    };
    
    console.log(`[GeminiProvider] Request: ${this.model}, ${geminiContents.length} messages, ${geminiTools?.functionDeclarations?.length || 0} tools`);
    
    // Make API request with timeout
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), this.timeout);
    
    try {
      const url = `${this.getApiUrl()}?key=${this.apiKey}`;
      
      const response = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(requestBody),
        signal: controller.signal
      });
      
      clearTimeout(timeoutId);
      
      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Gemini API error ${response.status}: ${errorText}`);
      }
      
      const data: GeminiResponse = await response.json();
      
      if (!data.candidates || data.candidates.length === 0) {
        throw new Error('Gemini API returned no candidates');
      }
      
      console.log(`[GeminiProvider] Response received: ${data.usageMetadata?.totalTokenCount || 'unknown'} tokens`);
      
      return this.convertResponse(data);
      
    } catch (error: any) {
      clearTimeout(timeoutId);
      
      if (error.name === 'AbortError') {
        throw new Error(`Gemini API request timed out after ${this.timeout}ms`);
      }
      
      throw error;
    }
  }

  private convertMessages(messages: AIMessage[]): GeminiContent[] {
    const geminiContents: GeminiContent[] = [];
    
    for (const msg of messages) {
      if (msg.role === 'system') continue; // Handled separately
      
      if (msg.role === 'tool') {
        // Tool result - add as function response
        const toolCallId = msg.toolCallId || 'unknown';
        let functionName = 'unknown';
        
        // Try to extract function name from previous assistant message
        // This is a limitation of the conversion - ideally we'd track this
        
        geminiContents.push({
          role: 'user',
          parts: [{
            functionResponse: {
              name: functionName,
              response: JSON.parse(msg.content)
            }
          }]
        });
      } else if (msg.role === 'assistant' && msg.toolCalls?.length) {
        // Assistant message with tool calls
        const parts: any[] = [];
        
        // Add text if present
        if (msg.content) {
          parts.push({ text: msg.content });
        }
        
        // Add function calls
        for (const toolCall of msg.toolCalls) {
          parts.push({
            functionCall: {
              name: toolCall.function.name,
              args: JSON.parse(toolCall.function.arguments)
            }
          });
        }
        
        geminiContents.push({
          role: 'model',
          parts
        });
      } else {
        // Regular message
        geminiContents.push({
          role: msg.role === 'user' ? 'user' : 'model',
          parts: [{ text: msg.content }]
        });
      }
    }
    
    return geminiContents;
  }

  private convertTools(tools: AITool[]): GeminiTool {
    return {
      functionDeclarations: tools.map(tool => ({
        name: tool.name,
        description: tool.description,
        parameters: {
          type: 'object',
          properties: tool.parameters.properties,
          required: tool.parameters.required
        }
      }))
    };
  }

  private convertResponse(data: GeminiResponse): AIResponse {
    const candidate = data.candidates[0];
    let textContent = '';
    const toolCalls: any[] = [];
    
    // Extract text and function calls from parts
    for (const part of candidate.content.parts) {
      if (part.text) {
        textContent += part.text;
      } else if (part.functionCall) {
        toolCalls.push({
          id: `gemini_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`,
          type: 'function',
          function: {
            name: part.functionCall.name,
            arguments: JSON.stringify(part.functionCall.args)
          }
        });
      }
    }
    
    return {
      content: textContent || 'Processing...',
      toolCalls: toolCalls.length > 0 ? toolCalls : undefined,
      provider: 'gemini',
      confidence: 0.85,
      requiresAction: toolCalls.length > 0
    };
  }

  private delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }
}