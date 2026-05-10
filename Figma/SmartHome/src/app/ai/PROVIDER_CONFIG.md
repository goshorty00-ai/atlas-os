# ATLAS AI Provider Configuration

## Current Configuration (GPT Disabled)

### Provider Status

| Provider | Enabled | Priority | Role |
|----------|---------|----------|------|
| **Claude** | ✅ Yes | 1 (Primary) | Reasoning, orchestration, analysis, device control |
| **Gemini** | ✅ Yes | 2 (Fallback) | Multimodal tasks, fallback for reasoning |
| **GPT** | ❌ No | - | Disabled (no credits available) |

## Routing Policy (GPT Disabled)

### Claude (Primary)
**Used for:**
- Device control actions ("turn on kitchen lights")
- Status queries ("show me all devices")
- Analysis and summaries ("which devices are offline?")
- Room-based queries ("what's in the kitchen?")
- Reasoning and orchestration
- Long-context tasks (>50 devices)

**Priority:** 1 (highest)

### Gemini (Fallback)
**Used for:**
- Multimodal queries (images, videos, files)
- Device type queries ("show me all lights")
- Fallback if Claude fails
- General queries

**Priority:** 2

### GPT (Disabled)
**Status:** Disabled due to insufficient credits
**Will be used for:** Reasoning, planning, tool orchestration (when re-enabled)

## Fallback Chain

### Current Chain (GPT Disabled)
```
Query -> Claude (primary) -> Gemini (fallback) -> Error
```

### Future Chain (GPT Enabled)
```
Query -> GPT/Claude/Gemini (based on task) -> Fallbacks -> Error
```

## Configuration Structure

```typescript
interface ProviderConfig {
  enabled: boolean;           // Whether provider is active
  apiKeyPresent: boolean;     // Whether API key is configured
  modelName?: string;         // Model to use (e.g., 'claude-3-sonnet')
  timeout?: number;           // Request timeout in ms
  priority: number;           // Lower = higher priority
  maxRetries?: number;        // Retry attempts on failure
}
```

## Provider Configuration Manager

### Location
`Figma/SmartHome/src/app/ai/ProviderConfig.ts`

### Key Methods

```typescript
// Check if provider is available
isAvailable(provider: AIProvider): boolean

// Get enabled providers sorted by priority
getEnabledProviders(): AIProvider[]

// Get fallback chain for a provider
getFallbackChain(preferredProvider: AIProvider): AIProvider[]

// Enable/disable providers
enableProvider(provider: AIProvider, apiKey?: string)
disableProvider(provider: AIProvider, reason?: string)

// Record failures
recordFailure(provider: AIProvider, error: string)

// Get configuration summary
getSummary(): Record<AIProvider, { enabled, available, priority, reason }>
```

## Graceful Degradation

### Failure Handling

1. **Provider Unavailable**
   - Log: `[AICore] Skipping {provider}: {reason}`
   - Action: Try next provider in fallback chain

2. **Provider Error**
   - Log: `[AICore] Provider {provider} failed: {error}`
   - Action: Record failure, try next provider

3. **All Providers Failed**
   - Error: `All available AI providers failed. Available: {list}`
   - Action: Return error to user

### Example Logs

```
[AtlasAI] Provider configuration: {
  gpt: { enabled: false, available: false, priority: 1, reason: 'Provider disabled - no credits available' },
  claude: { enabled: true, available: true, priority: 1, reason: 'Primary provider' },
  gemini: { enabled: true, available: true, priority: 2, reason: 'Fallback provider' }
}

[AICore] Routing query to claude. Reason: Complex reasoning and action orchestration required (Claude primary while GPT disabled)
[AICore] Fallback chain: claude -> gemini
[AICore] Attempting claude...
[MockAIProvider:claude] Processing query: "turn on kitchen lights..."
[AICore] claude requested 1 tool calls
[AICore] claude completed successfully with tools
```

## Enabling GPT When Credits Available

### Step 1: Update Configuration
```typescript
const providerConfig = atlasAI.getProviderConfig();
providerConfig.enableProvider('gpt', 'your-api-key-here');
```

### Step 2: Verify Status
```typescript
const summary = providerConfig.getSummary();
console.log('GPT status:', summary.gpt);
// { enabled: true, available: true, priority: 1, reason: 'Provider enabled' }
```

### Step 3: Routing Will Auto-Update
- GPT will be used for reasoning and action tasks
- Claude will be used for analysis and long-context
- Gemini will be used for multimodal tasks

## Environment-Based Configuration

### Future Enhancement
```typescript
// Load from environment variables
const config = {
  gpt: {
    enabled: process.env.GPT_ENABLED === 'true',
    apiKey: process.env.OPENAI_API_KEY,
    modelName: process.env.GPT_MODEL || 'gpt-4'
  },
  claude: {
    enabled: process.env.CLAUDE_ENABLED === 'true',
    apiKey: process.env.ANTHROPIC_API_KEY,
    modelName: process.env.CLAUDE_MODEL || 'claude-3-sonnet'
  },
  gemini: {
    enabled: process.env.GEMINI_ENABLED === 'true',
    apiKey: process.env.GOOGLE_API_KEY,
    modelName: process.env.GEMINI_MODEL || 'gemini-pro'
  }
};
```

## Smart Home Grounding (Unchanged)

Provider configuration does NOT affect smart home grounding:
- Device state remains grounded in real data
- Tool execution uses actual device APIs
- No hallucination of device information
- All actions mutate real device state

## Testing Provider Configuration

### Test 1: Verify GPT is Disabled
```typescript
const config = atlasAI.getProviderConfig();
const gptStatus = config.getStatus('gpt');
console.assert(gptStatus.available === false);
console.assert(gptStatus.reason.includes('disabled'));
```

### Test 2: Verify Claude is Primary
```typescript
const enabled = config.getEnabledProviders();
console.assert(enabled[0] === 'claude');
console.assert(enabled.includes('gemini'));
console.assert(!enabled.includes('gpt'));
```

### Test 3: Verify Fallback Chain
```typescript
const chain = config.getFallbackChain('claude');
console.assert(chain[0] === 'claude');
console.assert(chain[1] === 'gemini');
console.assert(chain.length === 2); // GPT not in chain
```

## Production Readiness

### Current Status
- ✅ Provider configuration system implemented
- ✅ GPT disabled with clear reason
- ✅ Claude-first routing active
- ✅ Graceful degradation working
- ✅ Failure logging implemented
- ❌ Real API integrations (still using mocks)
- ❌ Environment-based configuration
- ❌ API key management system

### Next Steps
1. Implement real Claude API integration
2. Implement real Gemini API integration
3. Add secure API key storage
4. Add environment variable support
5. Implement rate limiting per provider
6. Add provider health monitoring