# End-to-End Request Flow Examples (GPT Disabled)

## Example 1: "Which devices are offline?"

### Request Flow

```
User Input: "Which devices are offline?"
    ↓
AtlasAIChat.tsx (sendQuery)
    ↓
useAtlasAI.ts (sendQuery)
    ↓
AtlasAI.ts (query method)
    ↓
SmartHomeAgent.ts (processQuery)
    ↓
AICore.ts (execute)
    ↓
AICore.ts (routeTask)
```

### Routing Decision

```typescript
// AICore.ts - routeTask()
queryLower = "which devices are offline?"

// Check routing conditions
needsReasoning() = false (no "why", "how", "should")
needsActions() = false (no "turn on", "turn off")
needsAnalysis() = true (contains "which devices")

// Route to Claude (primary for analysis)
idealProvider = 'claude'
reason = 'Complex reasoning, orchestration, or analysis required (Claude primary while GPT disabled)'

// Get fallback chain
enabledProviders = ['claude', 'gemini'] // GPT excluded (disabled)
fallbackChain = ['claude', 'gemini']

return {
  provider: 'claude',
  reason: 'Complex reasoning, orchestration, or analysis required...',
  fallbacks: ['gemini'],
  availableProviders: ['claude', 'gemini']
}
```

### Provider Execution

```typescript
// AICore.ts - execute()
console.log('[AICore] Routing query to claude. Reason: Complex reasoning...')
console.log('[AICore] Fallback chain: claude -> gemini')

// Check if Claude is available
providerConfig.isAvailable('claude') = true

// Get Claude provider
impl = MockAIProvider('claude')

console.log('[AICore] Attempting claude...')

// Build messages
messages = [
  { role: 'system', content: 'You are ATLAS, a premium smart home AI...' },
  { role: 'user', content: 'DEVICES (5 total):\n- Kitchen Light (light, online)...\n\nUser Query: Which devices are offline?' }
]

// Call Claude
response = await impl.complete(messages, tools)
```

### Claude Response

```typescript
// MockAIProvider.ts - handleClaudeQuery()
console.log('[MockAIProvider:claude] Processing query: "Which devices are offline?..."')

// Match offline query pattern
query.includes('offline') = true

return {
  content: "Let me check which devices are currently offline and analyze potential issues.",
  toolCalls: [{
    id: 'call_claude_3',
    type: 'function',
    function: {
      name: 'get_offline_devices',
      arguments: '{}'
    }
  }],
  provider: 'claude',
  confidence: 0.9,
  requiresAction: false
}
```

### Tool Execution

```typescript
// AICore.ts - executeTools()
console.log('[AICore] claude requested 1 tool calls')

tool = tools.get('get_offline_devices')
args = {}
result = await tool.handler(args)

// SmartHomeAgent.ts - getOfflineDevices()
offlineDevices = state.providers.flatMap(p => 
  p.devices
    .filter(d => d.isOnline === false) // REAL device data
    .map(d => ({
      name: d.name,
      type: d.deviceType,
      provider: p.displayName,
      lastSeen: 'Unknown'
    }))
)

return {
  devices: [
    { name: 'Bedroom Thermostat', type: 'thermostat', provider: 'Ring', lastSeen: 'Unknown' }
  ],
  total: 1
}
```

### Final Response

```typescript
// AICore.ts - execute() continued
followupMessages = [
  ...messages,
  { role: 'assistant', content: "Let me check...", toolCalls: [...] },
  { role: 'tool', content: '{"devices":[{"name":"Bedroom Thermostat",...}],"total":1}', toolCallId: 'call_claude_3' }
]

finalResponse = await impl.complete(followupMessages, [])

console.log('[AICore] claude completed successfully with tools')

return {
  content: "I found 1 device that is currently offline: Bedroom Thermostat (Ring). This device may have lost connection or needs to be reset.",
  provider: 'claude',
  confidence: 0.9,
  requiresAction: false
}
```

### UI Update

```typescript
// useAtlasAI.ts - sendQuery()
assistantMessage = {
  id: 'msg_2',
  role: 'assistant',
  content: "I found 1 device that is currently offline: Bedroom Thermostat...",
  timestamp: Date.now()
}

setAIState(prev => ({
  ...prev,
  messages: [...prev.messages, assistantMessage],
  isProcessing: false
}))
```

---

## Example 2: "Turn off kitchen lights"

### Request Flow

```
User Input: "Turn off kitchen lights"
    ↓
[Same flow as Example 1 through AICore.ts]
```

### Routing Decision

```typescript
// AICore.ts - routeTask()
queryLower = "turn off kitchen lights"

needsReasoning() = false
needsActions() = true (contains "turn off")
needsAnalysis() = false

// Route to Claude (primary for actions while GPT disabled)
idealProvider = 'claude'
reason = 'Complex reasoning, orchestration, or analysis required (Claude primary while GPT disabled)'

fallbackChain = ['claude', 'gemini']

return {
  provider: 'claude',
  reason: 'Complex reasoning, orchestration, or analysis required...',
  fallbacks: ['gemini'],
  availableProviders: ['claude', 'gemini']
}
```

### Claude Response

```typescript
// MockAIProvider.ts - handleClaudeQuery()
query.includes('turn off') = true
deviceName = extractDeviceName("turn off kitchen lights") = "kitchen lights"
action = 'off'

return {
  content: "I'll turn off the kitchen lights for you.",
  toolCalls: [{
    id: 'call_claude_1',
    type: 'function',
    function: {
      name: 'device_control',
      arguments: '{"deviceName":"kitchen lights","action":"off"}'
    }
  }],
  provider: 'claude',
  confidence: 0.9,
  requiresAction: true,
  structured: {
    type: 'device_action',
    data: { deviceName: 'kitchen lights', action: 'off' },
    actions: [{
      id: 'device_control_1',
      label: 'Turn Off kitchen lights',
      type: 'device_control',
      payload: { deviceName: 'kitchen lights', action: 'off' }
    }]
  }
}
```

### Tool Execution (REAL Device Control)

```typescript
// SmartHomeAgent.ts - controlDevice()
deviceName = "kitchen lights"
action = "off"

// Find device in REAL state data
for (const provider of this.state.providers) {
  const device = provider.devices.find(d => 
    d.name.toLowerCase().includes("kitchen lights")
  )
  
  if (device) {
    // Found: Kitchen Light (Philips Hue)
    
    // REAL device control via WebView2 bridge
    await this.executeAction(
      'philips_hue',           // providerId
      'light-001',             // deviceId
      'LCT015',                // sku
      'devices.capabilities.on_off',
      'powerSwitch',
      false                    // Turn off
    )
    
    return { 
      success: true, 
      message: 'Turned off Kitchen Light' 
    }
  }
}
```

### Backend Execution

```
SmartHomeAgent.executeAction()
    ↓
useSmartHome.executeAction()
    ↓
postToHost('smart-home.executeAction', { providerId, deviceId, sku, capabilityType, capabilityInstance, value })
    ↓
WebView2 Bridge
    ↓
C# Backend (SmartHomeManager)
    ↓
Philips Hue API
    ↓
Physical Device (Kitchen Light turns off)
```

### Final Response

```typescript
return {
  content: "I've turned off the Kitchen Light. The device should now be off.",
  provider: 'claude',
  confidence: 0.9,
  requiresAction: true,
  structured: {
    type: 'device_action',
    data: { deviceName: 'kitchen lights', action: 'off' },
    actions: [...]
  }
}
```

---

## Example 3: "Show me all Philips Hue devices"

### Request Flow

```
User Input: "Show me all Philips Hue devices"
    ↓
[Same flow through AICore.ts]
```

### Routing Decision

```typescript
// AICore.ts - routeTask()
queryLower = "show me all philips hue devices"

needsReasoning() = false
needsActions() = false
needsAnalysis() = true (contains "show me")

// Route to Claude
idealProvider = 'claude'
fallbackChain = ['claude', 'gemini']
```

### Claude Response

```typescript
// MockAIProvider.ts - handleClaudeQuery()
// No specific pattern match, but contains "show me"

// Falls through to room-based query check
// No room keyword found

// Default response with tool call
return {
  content: "I can help you analyze your smart home system. What specific information would you like me to review?",
  provider: 'claude',
  confidence: 0.7,
  requiresAction: false
}
```

### Fallback to Gemini (if Claude fails)

```typescript
// If Claude were to fail:
console.error('[AICore] Provider claude failed: Some error')
providerConfig.recordFailure('claude', 'Some error')

// Try next provider
console.log('[AICore] Attempting gemini...')

// MockAIProvider.ts - handleGeminiQuery()
query.includes('lights') = false
query.includes('philips') = false

// Default response
return {
  content: "I'm here to help with your smart home. What would you like to know or control?",
  provider: 'gemini',
  confidence: 0.6,
  requiresAction: false
}
```

---

## GPT Disabled Behavior

### Attempt to Use GPT

```typescript
// If GPT were somehow selected (shouldn't happen)
console.log('[AICore] Attempting gpt...')

// Check availability
providerConfig.isAvailable('gpt') = false

// Skip GPT
console.warn('[AICore] Skipping gpt: Provider disabled - no credits available')

// Continue to Claude
console.log('[AICore] Attempting claude...')
```

### GPT Provider Call (if forced)

```typescript
// MockAIProvider.ts - handleGPTQuery()
throw new Error('GPT provider is currently disabled due to insufficient credits')

// Caught in AICore.ts
console.error('[AICore] Provider gpt failed: GPT provider is currently disabled...')
providerConfig.recordFailure('gpt', 'GPT provider is currently disabled...')

// Fallback to Claude
```

---

## Console Output Example

```
[AtlasAI] Provider configuration: {
  gpt: { enabled: false, available: false, priority: 1, reason: 'Provider disabled - no credits available' },
  claude: { enabled: true, available: true, priority: 1, reason: 'Primary provider' },
  gemini: { enabled: true, available: true, priority: 2, reason: 'Fallback provider' }
}

[AICore] Routing query to claude. Reason: Complex reasoning, orchestration, or analysis required (Claude primary while GPT disabled)
[AICore] Fallback chain: claude -> gemini
[AICore] Attempting claude...
[MockAIProvider:claude] Processing query: "Which devices are offline?..."
[AICore] claude requested 1 tool calls
[AICore] claude completed successfully with tools
```