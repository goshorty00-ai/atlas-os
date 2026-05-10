# Voice & Personality System Diagnostics

## ✅ Servers Implementation Status: **COMPLETE**

The Servers view is fully implemented and navigation is already wired. Line 5359 in `MediaCentreViewModel.cs`:
```csharp
Categories.Add(new MediaCategory { Id = "servers", Label = "Servers", Subtitle = "Addon Servers", Count = 0, IconGeometry = IconForCategory("servers") });
```

**The Servers button should appear in your Media Centre sidebar.** If you don't see it, restart the app.

---

## 🔊 Voice System Issues - Diagnosis & Fixes

### Issue 1: "ElevenLabs voices are there but can't hear it - only text comes on screen"

#### Root Causes (Most Likely → Least Likely):

**1. Speech is Disabled in VoiceManager** ✅ **CHECK THIS FIRST**
- **What to check:** VoiceManager.SpeechEnabled property
- **How to fix:** 
  - Open Settings/Voice Settings in the UI
  - Ensure "Enable Speech" is checked
  - Or programmatically: `_voiceManager.SpeechEnabled = true;`

**2. ElevenLabs API Key Missing or Invalid**
- **Location:** `%AppData%\AtlasAI\voice_keys.json`
- **Expected format:**
  ```json
  {
    "elevenlabs": "sk_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
    "openai": "optional-openai-key-here"
  }
  ```
- **How to fix:**
  - Get API key from https://elevenlabs.io/
  - Save to `voice_keys.json`
  - Restart app

**3. Wrong TTS Provider Selected**
- **What to check:** `ActiveProviderType` in VoiceManager
- **How to fix:**
  - Open Voice Settings UI
  - Select "ElevenLabs" as provider
  - Ensure an ElevenLabs voice is selected (preferably "Atlas AI" voice ID: rz1Dju9fYa3YHIYzRCM4)

**4. Audio Playback Failing (MediaPlayer issue)**
- **Debug log locations:**
  ```
  [VoiceManager] SpeakAsync called - TurnId: xxx, Enabled: True, Text length: 50
  [VoiceManager] Synthesis completed in 1234ms (success=True)
  [VoiceManager] Playing audio...
  [VoiceManager] Playback completed in 3000ms
  ```
- **If you see "Synthesis completed" but no "Playing audio" or "Playback completed":**
  - Check Windows audio settings
  - Ensure default playback device is set correctly
  - Check volume levels (both Windows and VoiceManager.Volume)

**5. AssistantUtterance has SuppressSpeech=true**
- **Check:** Response creation in ChatWindow
- **Fix:** Ensure `AssistantUtterance` is created with `SuppressSpeech = false`

#### Quick Diagnostic Steps:

1. **Open Output Window in Visual Studio** (View → Output)
2. **Look for these patterns when you send a message:**
   - `✅ GOOD`: `[VoiceManager] SpeakAsync called - TurnId: xxx, Enabled: True`
   - `❌ BAD`: `[VoiceManager] SpeakAsync called - TurnId: xxx, Enabled: False`
   - `❌ BAD`: `[VoiceManager] ❌ REJECTED: Duplicate TurnId`
   - `✅ GOOD`: `[VoiceManager] Synthesis completed in 1500ms (success=True)`
   - `❌ BAD`: `[VoiceManager] Synthesis completed in 50ms (success=False)`
   - `✅ GOOD`: `[VoiceManager] Playback completed in 3000ms`

3. **Check VoiceManager initialization:**
   ```csharp
   // In ChatWindow.xaml.cs constructor or OnLoaded
   _voiceManager = new VoiceManager();
   _voiceManager.SpeechEnabled = true;  // ← CRITICAL
   _voiceManager.Volume = 1.0;
   await _voiceManager.SetProviderAsync(VoiceProviderType.ElevenLabs);
   ```

4. **Check API Keys:**
   ```powershell
   # In PowerShell
   $path = "$env:APPDATA\AtlasAI\voice_keys.json"
   if (Test-Path $path) { 
       Get-Content $path | ConvertFrom-Json 
   } else { 
       Write-Host "❌ voice_keys.json NOT FOUND" 
   }
   ```

---

### Issue 2: "Personalities don't work"

#### Root Causes:

**1. Personality Not Applied to System Prompt**
- **Check:** Where AI messages are generated (likely in ChatWindow or AIOrchestrator)
- **Expected behavior:** System prompt should include personality context
- **Fix:** Ensure personality from `AtlasSettings.PersonalitySelected` is included in system prompt

**2. Wrong Personality Selected**
- **Location:** `AtlasSettings.PersonalitySelected` (defaults to "Butler")
- **Options:** 
  - Butler
  - Engineer
  - Analyst
  - Unfiltered (if PERSONAL_BUILD defined)
- **How to check:**
  ```csharp
  var settings = SettingsManager.Load();
  Debug.WriteLine($"Current personality: {settings.PersonalitySelected}");
  ```

**3. Greetings Work But Responses Don't**
- **Issue:** GreetingManager generates personality-based greetings, but main AI responses ignore personality
- **Fix:** Ensure the AI system prompt builder includes personality instructions
- **Location to check:** Search for where Anthropic/OpenAI API calls are made

**4. ChaosTestingEngineV2 Not Integrated**
- **Check:** `Personality\ChaosTestingEngineV2.cs` exists but may not be connected to response pipeline
- **Fix:** Ensure chaos testing hooks are called when `UnfilteredStyle = "ChaosTesting"`

#### Quick Diagnostic Steps:

1. **Check personality setting:**
   ```csharp
   var settings = AtlasAI.Settings.SettingsManager.Load();
   System.Diagnostics.Debug.WriteLine($"Personality: {settings.PersonalitySelected}");
   System.Diagnostics.Debug.WriteLine($"Salutation: {settings.SalutationPreference}");
   System.Diagnostics.Debug.WriteLine($"Preferred Name: {settings.PreferredName}");
   ```

2. **Test greeting generation:**
   ```csharp
   var personality = Enum.Parse<PersonalityType>(settings.PersonalitySelected);
   var greeting = GreetingManager.GetGreeting(
       personality, 
       settings.SalutationPreference, 
       settings.PreferredName
   );
   System.Diagnostics.Debug.WriteLine($"Generated greeting: {greeting}");
   ```

3. **Check AI system prompt** (most important):
   - Find where `CreateChatCompletionAsync` or similar API call is made
   - Verify system message includes personality context
   - Example expected content:
     ```
     You are Atlas, an AI assistant with a Butler personality.
     Be formal, polite, and use salutations like "sir" or "ma'am".
     ...
     ```

---

## 🔧 Recommended Fixes

### Fix #1: Enable Speech (Immediate)

Add this to ChatWindow initialization:
```csharp
// In ChatWindow.xaml.cs - constructor or Loaded event
_voiceManager = new VoiceManager();
_voiceManager.SpeechEnabled = true;  // ← Add this line
_voiceManager.Volume = 1.0;
```

### Fix #2: Verify ElevenLabs Configuration

```csharp
// In ChatWindow or SettingsWindow
private async Task VerifyVoiceSystemAsync()
{
    var vm = VoiceManager.Instance; // or _voiceManager
    
    // Check speech enabled
    if (!vm.SpeechEnabled)
    {
        Debug.WriteLine("❌ Speech is DISABLED");
        return;
    }
    
    // Check provider
    Debug.WriteLine($"Active provider: {vm.ActiveProviderType}");
    
    // Check voices
    var voices = await vm.GetVoicesAsync();
    Debug.WriteLine($"Available voices: {voices.Count}");
    foreach (var voice in voices)
    {
        Debug.WriteLine($"  - {voice.DisplayName} ({voice.Id})");
    }
    
    // Check selected voice
    if (vm.SelectedVoice != null)
    {
        Debug.WriteLine($"Selected: {vm.SelectedVoice.Value.DisplayName}");
    }
    else
    {
        Debug.WriteLine("❌ No voice selected");
    }
}
```

### Fix #3: Ensure Personality in AI Prompt

Find where you build the system message for the AI (likely in ChatWindow or AI service), then ensure it includes:

```csharp
private string BuildSystemPrompt()
{
    var settings = SettingsManager.Load();
    var personality = Enum.Parse<PersonalityType>(settings.PersonalitySelected);
    
    var prompt = new StringBuilder();
    prompt.AppendLine("You are Atlas, an advanced AI assistant.");
    
    // Add personality context
    switch (personality)
    {
        case PersonalityType.Butler:
            prompt.AppendLine("You have a Butler personality:");
            prompt.AppendLine("- Be formal, polite, and attentive");
            prompt.AppendLine("- Use appropriate salutations (sir/ma'am)");
            prompt.AppendLine("- Speak with refinement and professionalism");
            break;
            
        case PersonalityType.Engineer:
            prompt.AppendLine("You have an Engineer personality:");
            prompt.AppendLine("- Be direct, practical, and solution-focused");
            prompt.AppendLine("- Use technical language when appropriate");
            prompt.AppendLine("- Focus on building and fixing things");
            break;
            
        // Add other personalities...
    }
    
    return prompt.ToString();
}
```

---

## 🧪 Testing Checklist

### Voice System:
- [ ] Run app and open Settings → Voice
- [ ] Verify "Enable Speech" is checked
- [ ] Verify "ElevenLabs" is selected as provider
- [ ] Verify an ElevenLabs voice is selected
- [ ] Send a test message: "Hello Atlas"
- [ ] Check Debug Output for `[VoiceManager]` logs
- [ ] Verify you HEAR audio (not just see text)

### Personality System:
- [ ] Open Settings → Personality
- [ ] Select "Butler" personality
- [ ] Set salutation preference
- [ ] Send message: "Hello"
- [ ] Verify response uses butler language (formal, polite)
- [ ] Switch to "Engineer" personality
- [ ] Send same message
- [ ] Verify response is more casual/technical

---

## 📁 Key Files to Check

**Voice System:**
- `Voice\VoiceManager.cs` - Main TTS logic (lines 320-550 for SpeakAsync)
- `Voice\VoiceSystemOrchestrator.cs` - Coordinates voice input/output
- `Voice\ElevenLabsProvider.cs` - ElevenLabs TTS implementation
- `ChatWindow.xaml.cs` - Where VoiceManager is initialized and used
- `%AppData%\AtlasAI\voice_keys.json` - API keys storage
- `%AppData%\AtlasAI\voice_settings.json` - Voice preferences

**Personality System:**
- `Personality\GreetingManager.cs` - Personality-based greetings
- `Personality\PersonalityType.cs` - Enum of personality types
- `Personality\ChaosTestingEngineV2.cs` - Unfiltered personality logic
- `Settings\AtlasSettings.cs` - Where PersonalitySelected is stored
- `ChatWindow.xaml.cs` - Where AI system prompt is built

---

## 🚀 Next Steps

1. **Check VoiceManager.SpeechEnabled** - This is the #1 most likely cause
2. **Verify ElevenLabs API key** - Second most likely cause
3. **Review Debug Output** - Look for synthesis success/failure messages
4. **Test with WindowsSAPI** - Fallback to verify audio playback works
5. **Check personality in system prompt** - Ensure AI knows its personality
6. **Test greeting generation** - Verify GreetingManager works correctly

Let me know what you find in the Debug Output when you send a message!
