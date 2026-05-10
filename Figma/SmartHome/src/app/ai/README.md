# ATLAS AI - Smart Home Intelligence

ATLAS AI is a premium, tool-using assistant designed specifically for smart home management. It goes beyond basic chatbot functionality to provide grounded, action-capable intelligence.

## Architecture Overview

### Core Components

1. **AICore** - Provider-agnostic orchestration layer
   - Routes tasks to optimal AI providers (GPT/Claude/Gemini)
   - Handles fallbacks and retries
   - Manages tool execution
   - Calculates confidence scores

2. **SmartHomeAgent** - Domain-specific intelligence
   - Understands smart home devices and capabilities
   - Provides grounded responses using real device data
   - Executes device control actions
   - Maintains smart home context

3. **MemoryManager** - Persistent and session memory
   - Session memory (cleared on new session)
   - Long-term user preferences
   - Domain-specific smart home memory
   - Usage pattern tracking

4. **AtlasAI** - Main orchestration service
   - Coordinates all components
   - Provides unified interface
   - Tracks interactions and performance
   - Manages user preferences

## Provider Routing Strategy

### GPT (OpenAI)
**Best for:** Reasoning, planning, tool orchestration, structured actions
- Complex device control scenarios
- Multi-step automations
- Action planning and execution
- Structured response generation

### Claude (Anthropic)  
**Best for:** Long context, careful analysis, summaries
- Large device inventories (50+ devices)
- Detailed system analysis
- Comprehensive status reports
- Careful troubleshooting

### Gemini (Google)
**Best for:** Multimodal inputs, broad retrieval, general queries
- Image/video analysis of devices
- General smart home questions
- Fallback for other providers
- Multimodal device interactions

## Tool Registry

### Available Tools

1. **get_device_state** - Get current state of devices
2. **get_room_devices** - Get devices by room/location
3. **device_control** - Control smart home devices
4. **get_offline_devices** - List offline devices
5. **get_devices_by_type** - Filter devices by type

### Tool Execution Flow

1. AI determines if tools are needed
2. Tools are called with parsed arguments
3. Results are fed back to AI
4. AI generates final response with context

## Memory System

### Session Memory (Temporary)
- Current conversation context
- Recent queries and responses
- Active device states
- Temporary preferences

### Long-term Memory (Persistent)
- User response style preferences
- Frequently used devices
- Custom room names
- Interaction patterns

### Domain Memory (Smart Home Specific)
- Device preferences and settings
- Room configurations
- User routines and scenes
- Usage statistics

## Grounding Strategy

ATLAS AI never hallucinates device data. All responses are grounded in:

1. **Real Device State** - Current online/offline status, power state, capabilities
2. **Provider Information** - Available integrations and their status
3. **Historical Data** - Recent device events and usage patterns
4. **User Preferences** - Learned behaviors and explicit settings

## Response Types

### Text Responses
Natural language answers with context and explanations

### Structured Responses
- **device_list** - Formatted device information
- **device_action** - Action confirmations with details
- **scene_suggestion** - Recommended scenes or automations
- **error** - Error messages with troubleshooting
- **confirmation** - Action confirmation requests

### Suggested Actions
Interactive buttons for common tasks:
- Device control actions
- Scene applications
- Configuration changes

## Integration Guide

### Basic Setup

```typescript
import { AtlasAI } from './ai/AtlasAI';
import { useSmartHomeContext } from './SmartHomeContext';

// In your component
const { executeAction, isDeviceOn } = useSmartHomeContext();
const atlas = new AtlasAI(executeAction, isDeviceOn);
```

### Using the Hook

```typescript
import { useAtlasAI } from './ai/useAtlasAI';

function MyComponent() {
  const {
    messages,
    isProcessing,
    sendQuery,
    executeAIAction,
    quickActions
  } = useAtlasAI();
  
  // Send a query
  await sendQuery("Turn on the living room lights");
  
  // Execute suggested action
  await executeAIAction(actionId, payload);
}
```

### Chat Component

```typescript
import { AtlasAIChat } from './components/AtlasAIChat';

function SmartDevices() {
  const [showAI, setShowAI] = useState(false);
  
  return (
    <>
      <button onClick={() => setShowAI(true)}>
        Ask ATLAS
      </button>
      
      <AtlasAIChat 
        isOpen={showAI} 
        onClose={() => setShowAI(false)} 
      />
    </>
  );
}
```

## Example Queries

### Device Control
- "Turn on the kitchen lights"
- "Set bedroom brightness to 50%"
- "Turn off everything in the living room"

### Status Queries
- "Which lights are still on?"
- "Show me all offline devices"
- "What's the status of my smart home?"

### Room-based Queries
- "What devices are in the kitchen?"
- "Turn off everything downstairs"
- "Show me bedroom devices"

### Troubleshooting
- "Why is the thermostat unavailable?"
- "Which devices went offline today?"
- "Check my Philips Hue connection"

## Performance Considerations

### Response Times
- Simple queries: < 1 second
- Tool-based queries: 1-3 seconds
- Complex analysis: 3-5 seconds

### Memory Usage
- Session memory: Cleared on new session
- Long-term memory: Persisted to localStorage
- Domain memory: Optimized for smart home data

### Error Handling
- Provider fallbacks for reliability
- Graceful degradation on failures
- User-friendly error messages
- Automatic retry logic

## Future Enhancements

### Planned Features
1. **Voice Integration** - Voice commands and responses
2. **Scene Learning** - AI-generated scenes based on usage
3. **Predictive Actions** - Proactive device suggestions
4. **Advanced Analytics** - Energy usage and optimization
5. **Multi-room Audio** - Coordinated audio control
6. **Security Integration** - Camera and sensor analysis

### Provider Expansion
- Support for additional AI providers
- Custom model fine-tuning
- Local AI model support
- Edge computing capabilities

## Development Notes

### Mock Provider
The current implementation uses `MockAIProvider` for development. Replace with real providers:

```typescript
// Replace mock providers with real implementations
aiCore.registerProvider('gpt', new OpenAIProvider(apiKey));
aiCore.registerProvider('claude', new ClaudeProvider(apiKey));
aiCore.registerProvider('gemini', new GeminiProvider(apiKey));
```

### Testing
- Unit tests for core components
- Integration tests for tool execution
- End-to-end tests for user scenarios
- Performance benchmarks

### Monitoring
- Response time tracking
- Provider success rates
- User satisfaction metrics
- Memory usage statistics

## Security Considerations

- API key management
- User data privacy
- Device access controls
- Audit logging
- Rate limiting
- Input validation

---

ATLAS AI represents a new paradigm in smart home assistants - moving from simple chat interfaces to intelligent, action-capable systems that understand and interact with your home environment.