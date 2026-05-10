# Voice Command Testing Guide

## Quick Test Scenarios

### 1. Basic Wake Word Test
**Steps**:
1. Ensure wake word is enabled in Settings
2. Say: "Atlas"
3. Wait for listening cue (beep/visual indicator)
4. Say: "What's the weather?"
5. **Expected**: AI responds with weather information and speaks it

**Debug Logs to Check**:
```
[WakeWordCoordinator] Wake word received: 'Atlas'
[VoiceOrchestrator] Wake word activated via Coordinator
[VoiceOrchestrator] 🎤 SPEECH RECOGNIZED: "What's the weather?"
[VoiceOrchestrator] 🔔 Notifying WakeWordFlowController.OnCommandReceived
═══════════════════════════════════════════════════════════════
[VoiceOrchestrator] ✅ COMMAND RECEIVED FROM FLOW CONTROLLER: 'What's the weather?'
═══════════════════════════════════════════════════════════════
[VoiceOrchestrator] 🔄 Forwarding command to AI pipeline
[VoiceSystemOrchestrator] Routing command: 'What's the weather?'
[VoiceOrchestrator] 🔊 Speech started - notifying WakeWordFlowController
[VoiceOrchestrator] 🏁 Speech ended - notifying WakeWordFlowController
```

---

### 2. Agent Action Test
**Steps**:
1. Say: "Atlas"
2. Say: "Open settings"
3. **Expected**: Settings window opens AND AI confirms

**Debug Logs to Check**:
```
[VoiceOrchestrator] ✅ COMMAND RECEIVED: 'Open settings'
[VoiceSystemOrchestrator] Routing to Agent Action
```

---

### 3. Follow-Up Conversation Test
**Steps**:
1. Say: "Atlas"
2. Say: "Tell me a joke"
3. Wait for AI to finish speaking
4. **Immediately** say: "Tell me another one" (within 4 seconds)
5. **Expected**: AI responds without needing to say "Atlas" again

**Debug Logs to Check**:
```
[VoiceOrchestrator] Speech ended - transitioning to follow-up listening
[WakeWordFlow] Follow-up window started (4s)
[VoiceOrchestrator] 🔔 Notifying WakeWordFlowController.OnCommandReceived
[VoiceOrchestrator] ✅ COMMAND RECEIVED: 'Tell me another one'
```

---

### 4. Mic Button Test
**Steps**:
1. Click microphone button in Chat window
2. Say: "What time is it?"
3. **Expected**: AI responds with current time

**Debug Logs to Check**:
```
[VoiceOrchestrator] StartListeningPipeline(PushToTalk)
[VoiceOrchestrator] 🎤 SPEECH RECOGNIZED: "What time is it?"
[VoiceOrchestrator] ✅ COMMAND RECEIVED: 'What time is it?'
```

---

### 5. Cancellation Test
**Steps**:
1. Say: "Atlas"
2. Press Stop/Cancel button before speaking
3. **Expected**: No command sent, returns to idle

**Debug Logs to Check**:
```
[VoiceOrchestrator] Listening was cancelled - not forwarding command
```

---

### 6. Error Recovery Test
**Steps**:
1. Disable AI API key in settings (or use invalid key)
2. Say: "Atlas"
3. Say: "Hello"
4. **Expected**: Error spoken ("I need an API key to help with that...")

**Debug Logs to Check**:
```
[VoiceSystemOrchestrator] AI not configured
[VoiceOrchestrator] ❌ ERROR: AI not configured
```

---

## Common Issues & Solutions

### Issue: Wake word detected but no listening cue
**Check**:
- Audio cue service is working
- `WakeWordFlowController.OnWakeWordDetected()` is called
- Look for log: `[WakeWordFlow] Wake word detected: 'Atlas'`

**Fix**: Ensure `AudioCueService.Instance.PlayCue()` is working

---

### Issue: Command recognized but no AI response
**Check**:
- `OnFlowControllerCommandReceived` is being called
- Look for log: `✅ COMMAND RECEIVED FROM FLOW CONTROLLER`
- Check if command is forwarded: `📤 Calling RouteCommand`

**Fix**: Check subscription in `Initialize()` method

---

### Issue: AI responds but doesn't speak
**Check**:
- VoiceManager is set: `SetVoiceManager()` called
- TTS is configured in settings
- `OnSpeechStarted()` and `OnSpeechEnded()` are called

**Fix**: Verify VoiceManager configuration

---

### Issue: Follow-up doesn't work
**Check**:
- `OnSpeechEnded()` calls `StartFollowUpListening()`
- Flow controller receives `OnResponseCompleted()`
- Look for log: `[WakeWordFlow] Follow-up window started (4s)`

**Fix**: Ensure `OnResponseCompleted()` is called after TTS ends

---

## Debug Output Analyzer

### Successful Command Flow Log Pattern
```
1. [WakeWordCoordinator] Wake word received: 'Atlas'
2. [VoiceOrchestrator] Wake word activated via Coordinator
3. [VoiceOrchestrator] StartListeningPipeline(WakeWord)
4. [VoiceOrchestrator] 🎤 SPEECH RECOGNIZED: "command"
5. [VoiceOrchestrator] 🔔 Notifying WakeWordFlowController.OnCommandReceived
6. ═══ [VoiceOrchestrator] ✅ COMMAND RECEIVED FROM FLOW CONTROLLER
7. [VoiceOrchestrator] 🔄 Forwarding command to AI pipeline
8. [VoiceOrchestrator] 📤 Calling RouteCommand
9. [VoiceSystemOrchestrator] Routing command
10. [VoiceOrchestrator] 🔊 Speech started - notifying WakeWordFlowController
11. [VoiceOrchestrator] 🏁 Speech ended - notifying WakeWordFlowController
12. [VoiceOrchestrator] ✅ Command routed successfully
13. [VoiceOrchestrator] 🏁 Command processing complete
```

### Missing Link Indicators
If you see **1-4** but NOT **5-13**, the fix didn't work:
- Check subscription: `WakeWordFlowController.Instance.CommandReceived += OnFlowControllerCommandReceived`
- Check flow controller notification: `WakeWordFlowController.Instance.OnCommandReceived(text)`

---

## Performance Metrics

### Expected Latencies
- **Wake word detection → Listening cue**: < 500ms
- **Command spoken → Recognition complete**: 1-3 seconds
- **Recognition → AI call**: < 100ms
- **AI processing**: 2-5 seconds (varies by model)
- **Response → TTS start**: < 500ms
- **TTS duration**: Varies by response length

### Total Round-Trip Time
- Typical: 5-10 seconds (wake word → AI response spoken)
- Fast: 3-5 seconds (short command, fast AI)
- Slow: 10-15 seconds (long command, slow AI)

---

## Troubleshooting Commands

### Check if orchestrator is initialized
```csharp
VoiceSystemOrchestrator.Instance.IsListening
```

### Check flow controller state
```csharp
WakeWordFlowController.Instance.CurrentState
// Returns: Idle, Listening, Processing, Speaking, FollowUp
```

### Check if wake word service is running
```csharp
WakeWordService.Instance.IsListening
```

### Manual test command routing
```csharp
// In debug, you can manually trigger:
WakeWordFlowController.Instance.OnCommandReceived("test command");
// Should see: ✅ COMMAND RECEIVED FROM FLOW CONTROLLER
```

---

## Quick Fixes

### Reset voice system
1. Stop voice system: `VoiceSystemOrchestrator.Instance.Stop()`
2. Wait 1 second
3. Restart: `await VoiceSystemOrchestrator.Instance.StartAsync()`

### Clear stuck state
```csharp
WakeWordFlowController.Instance.Reset();
```

### Force follow-up window
```csharp
WakeWordFlowController.Instance.OnResponseCompleted();
```

---

## Success Indicators

✅ **Working correctly if**:
- Wake word triggers listening mode
- Commands are recognized
- AI responds with relevant answer
- TTS speaks the response
- Follow-up listening works
- Logs show complete flow (1-13 above)

❌ **Not working if**:
- Wake word detected but no response
- Commands recognized but silent
- Missing logs between steps 5-13
- Errors in log with ❌ symbol

---

## Contact for Issues

If voice commands still don't work after this fix:
1. Check all logs for ❌ errors
2. Verify API key is configured
3. Test microphone input (Settings → Voice)
4. Check TTS is working (test in Settings)
5. Ensure wake word service is enabled

---

**Last Updated**: 2025
**Fix Version**: Voice Command Routing v1.0
