# Model Catalog & Task-Based Routing Implementation

## Overview

ATLAS AI now includes a comprehensive model catalog with tier classification and intelligent task-based routing. The system automatically selects the most appropriate model based on task complexity, optimizing for cost and performance.

## Model Catalog

### Tier Classification

Models are classified into 7 tiers:

1. **flagship** - Latest high-end models for complex reasoning
2. **fast** - Current fast models for quick responses
3. **cheap** - Cost-optimized models for simple tasks
4. **legacy** - Older but still officially supported models
5. **preview** - Beta/experimental models
6. **deprecated** - Marked for removal, still functional
7. **retired** - No longer available (blocked from selection)

### Supported Models

#### Anthropic Claude
- **Claude 3.5 Sonnet (Oct 2024)** - flagship, $3/$15 per 1M tokens
- **Claude 3.5 Sonnet (June 2024)** - flagship/legacy, $3/$15 per 1M tokens
- **Claude 3 Opus** - flagship, $15/$75 per 1M tokens (highest quality)
- **Claude 3.5 Haiku** - fast, $1/$5 per 1M tokens
- **Claude 3 Haiku** - cheap/legacy, $0.25/$1.25 per 1M tokens
- **Claude 3 Sonnet** - legacy, $3/$15 per 1M tokens

#### Google Gemini
- **Gemini 2.0 Flash (Experimental)** - preview, FREE during preview
- **Gemini 1.5 Pro (Latest)** - flagship, $1.25/$5 per 1M tokens
- **Gemini 1.5 Pro (002)** - flagship, $1.25/$5 per 1M tokens
- **Gemini 1.5 Flash (Latest)** - fast, $0.075/$0.30 per 1M tokens
- **Gemini 1.5 Flash (002)** - fast, $0.075/$0.30 per 1M tokens
- **Gemini 1.5 Flash-8B** - cheap, $0.0375/$0.15 per 1M tokens (cheapest)
- **Gemini 1.0 Pro** - legacy, $0.50/$1.50 per 1M tokens

#### OpenAI GPT (Disabled - No Credits)
- **GPT-4o** - flagship, $2.50/$10 per 1M tokens (DISABLED)
- **GPT-4o (Nov 2024)** - flagship, $2.50/$10 per 1M tokens (DISABLED)
- **GPT-4o Mini** - fast, $0.15/$0.60 per 1M tokens (DISABLED)
- **GPT-4 Turbo** - legacy, $10/$30 per 1M tokens (DISABLED)
- **GPT-3.5 Turbo** - deprecated, $0.50/$1.50 per 1M tokens (DISABLED)

## Task-Based Routing

### Task Classification

The AICore automatically classifies queries into task types:

1. **simple_query** - Basic status checks, device queries
   - Examples: "Which devices are offline?", "Turn on kitchen lights"
   - Model: Cheapest available (Claude 3 Haiku or Gemini Flash-8B)

2. **classification** - Categorization tasks
   - Examples: "Classify these devices", "Identify device types"
   - Model: Cheap/fast models

3. **formatting** - Data transformation
   - Examples: "Format this data", "Convert to JSON"
   - Model: Cheap/fast models

4. **multimodal** - Image/video/audio processing
   - Examples: "Look at this image", "Analyze camera feed"
   - Model: Gemini (vision support)

5. **complex_reasoning** - Multi-step logic, planning
   - Examples: "Why are these devices failing?", "Recommend automation"
   - Model: Flagship (Claude 3.5 Sonnet or Gemini 1.5 Pro)

6. **analysis** - Data analysis, summaries
   - Examples: "Analyze device usage", "Summarize events"
   - Model: Flagship models

7. **coding** - Code generation, automation scripts
   - Examples: "Create automation script", "Generate code"
   - Model: Flagship models

### Routing Logic

```typescript
// AICore.routeTask() flow:
1. Classify query into task type
2. Select ideal model based on task type
3. Check if provider is available
4. Return route with model ID and fallback chain
```

### Cost Optimization

The system automatically uses cheaper models when appropriate:

- **Simple device queries** → Claude 3 Haiku ($0.25/M) or Gemini Flash-8B ($0.0375/M)
- **Complex reasoning** → Claude 3.5 Sonnet ($3/M) or Gemini 1.5 Pro ($1.25/M)
- **Multimodal tasks** → Gemini 1.5 Flash ($0.075/M) with vision support

**Example cost savings:**
- 1000 simple queries with Haiku instead of Sonnet: **$2.75 saved**
- 1000 simple queries with Flash-8B instead of Pro: **$1.21 saved**

## Implementation Files

### Core Files

1. **ModelCatalog.ts** - Model definitions and helper functions
   - `MODEL_CATALOG` - Complete model database
   - `getRecommendedModel()` - Get model by task type
   - `getCheapestModelForTag()` - Find cheapest model for usage tag
   - `getModelById()` - Lookup model by ID

2. **AICore.ts** - Task routing and execution
   - `routeTask()` - Classify task and select model
   - `classifyTask()` - Determine task type from query
   - `execute()` - Execute with automatic model selection

3. **ClaudeProvider.ts** - Claude API integration
   - `setModel()` - Change model dynamically
   - `getModel()` - Get current model
   - Supports all Claude models in catalog

4. **GeminiProvider.ts** - Gemini API integration
   - `setModel()` - Change model dynamically
   - `getModel()` - Get current model
   - Supports all Gemini models in catalog

5. **ProviderFactory.ts** - Provider creation with model config
   - `createClaudeProvider(modelId?)` - Create with specific model
   - `createGeminiProvider(modelId?)` - Create with specific model
   - `updateModelConfig()` - Update default models

### UI Components

1. **ModelCatalogView.tsx** - Display model catalog
   - Shows all models with tier badges
   - Filterable by provider
   - Expandable details (context, cost, capabilities)
   - Model selection interface

2. **AIProviderSettings.tsx** - Provider configuration
   - API key management
   - Default model selection per provider
   - Model catalog viewer
   - Saves preferences to localStorage

## Usage Examples

### Automatic Routing

```typescript
// Simple query - uses cheap model
await atlasAI.ask("Which devices are offline?");
// → Routes to Claude 3 Haiku or Gemini Flash-8B

// Complex query - uses flagship model
await atlasAI.ask("Why are my lights failing and what should I do?");
// → Routes to Claude 3.5 Sonnet or Gemini 1.5 Pro

// Multimodal query - uses Gemini
await atlasAI.ask("Look at this camera image and identify issues");
// → Routes to Gemini 1.5 Flash (vision support)
```

### Manual Model Selection

```typescript
// Force specific model
await atlasAI.ask("Complex query", { 
  forceProvider: 'claude',
  forceModel: 'claude-3-opus-20240229' // Use Opus for highest quality
});
```

### Model Configuration

```typescript
// Set default models
factory.updateModelConfig({
  claudeModel: 'claude-3-5-haiku-20241022', // Use fast model by default
  geminiModel: 'gemini-1.5-flash-8b-latest' // Use cheapest Gemini
});
```

## Routing Decision Tree

```
Query received
    ↓
Classify task type
    ↓
    ├─ Simple/Classification/Formatting
    │   → Use cheapest model (Haiku/Flash-8B)
    │
    ├─ Multimodal
    │   → Use Gemini with vision (Flash/Pro)
    │
    └─ Complex/Analysis/Coding
        → Use flagship model (Sonnet/Pro/Opus)
    ↓
Check provider availability
    ↓
    ├─ Provider available
    │   → Use selected model
    │
    └─ Provider unavailable
        → Fallback to next provider
    ↓
Execute with selected model
```

## Model Selection UI

The AIProviderSettings component now includes:

1. **Default Model Selection** - Dropdown per provider
2. **Model Catalog Viewer** - Browse all available models
3. **Tier Badges** - Visual classification (flagship/fast/cheap/legacy/etc.)
4. **Cost Display** - Show pricing per 1M tokens
5. **Capability Tags** - Show supported features (vision, tool_calling, etc.)
6. **Context Window** - Display token limits
7. **Documentation Links** - Link to official provider docs

## Cost Tracking (Future)

Planned features:
- Track token usage per model
- Calculate actual costs per session
- Display cost breakdown in UI
- Set budget limits per model tier
- Alert when approaching budget limits

## Production Considerations

### Current Status: DEV-ONLY

- API keys stored in localStorage (XSS risk)
- Frontend-direct API calls (key exposure)
- No rate limiting
- No usage tracking
- No cost monitoring

### Required for Production

1. **Backend API Proxy** (CRITICAL)
   - Move API calls to C# backend
   - Secure key storage (Azure Key Vault)
   - Rate limiting per user
   - Usage tracking and billing

2. **Cost Management**
   - Track token usage per user
   - Set budget limits
   - Alert on high usage
   - Cost breakdown reports

3. **Model Governance**
   - Admin controls for model availability
   - Per-user model access policies
   - Automatic downgrade on budget limits
   - Audit logs for model usage

## Verification

### Files Created/Modified

✅ `ModelCatalog.ts` - Complete model database with 20+ models
✅ `AICore.ts` - Task-based routing with model selection
✅ `ClaudeProvider.ts` - Dynamic model support
✅ `GeminiProvider.ts` - Dynamic model support
✅ `ProviderFactory.ts` - Model configuration support
✅ `ModelCatalogView.tsx` - UI component for model catalog
✅ `AIProviderSettings.tsx` - Model selection in settings

### Execution Flow

1. User asks query → AICore.execute()
2. AICore.routeTask() classifies task type
3. Select appropriate model based on task
4. Check provider availability
5. Create provider with selected model
6. Execute query with model
7. Return response with model info

### Cost Optimization Proof

Example: "Which devices are offline?"
- **Without routing**: Claude 3.5 Sonnet ($3/M input)
- **With routing**: Claude 3 Haiku ($0.25/M input)
- **Savings**: 92% cost reduction for simple queries

Example: 10,000 queries/day
- 70% simple queries (7,000) → Use Haiku
- 30% complex queries (3,000) → Use Sonnet
- **Daily cost**: (7000 × $0.25 + 3000 × $3) / 1000 = $10.75
- **Without routing**: 10000 × $3 / 1000 = $30
- **Savings**: $19.25/day = $577.50/month

## Next Steps

1. ✅ Model catalog with tier classification - COMPLETE
2. ✅ Task-based routing logic - COMPLETE
3. ✅ Dynamic model selection in providers - COMPLETE
4. ✅ UI for model catalog and selection - COMPLETE
5. ⏳ Cost tracking per model - TODO
6. ⏳ Usage analytics dashboard - TODO
7. ⏳ Backend API proxy (production) - TODO
8. ⏳ Budget limits and alerts - TODO
