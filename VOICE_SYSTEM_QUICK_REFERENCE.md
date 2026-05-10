# Voice System Quick Reference

## What Changed

### Before
- ❌ Whisper STT hard-disabled (`false && useWhisper`)
- ❌ Silent failures when speech recognition not available
- ❌ No way to debug why wake word doesn't work
- ❌ Users confused when voice features don't work

### After
- ✅ Whisper enabled as primary STT (when OpenAI key exists)
- ✅ Clear error messages when initialization fails
- ✅ Comprehensive diagnostics panel shows system status
- ✅ Wake word toggle automatically disables if system unavailable

---

## How to Use Voice Diagnostics

### Option 1: Add to Settings Window
```xaml
<!-- In SettingsWindow.xaml -->
<TabItem Header="🎤 Voice">
    <ui:VoiceDiagnosticsView />
</TabItem>
```

### Option 2: Standalone Window
```csharp
// Add menu item or keyboard shortcut
private void ShowDiagnostics_Click(object sender, RoutedEventArgs e)
{
    var window = new Window
    {
        Title = "Voice Diagnostics",
        Content = new AtlasAI.UI.VoiceDiagnosticsView(),
        Width = 600,
        Height = 700
    };
    window.ShowDialog();
}
```

---

## Troubleshooting Guide

### Wake Word Not Working

**Check 1: Is Windows Speech Recognition enabled?**
```
Settings → Time & Language → Speech → Turn on speech recognition
```

**Check 2: Is microphone working?**
- Open Notepad
- Press Windows+H (dictation)
- Speak into mic - text should appear
- If not, check Windows Sound Settings

**Check 3: Use Diagnostics Panel**
```
Open Voice Diagnostics
Look at "System Status" - should be green ✅
Look at "Speech Recognizers" - should show "1 installed" or more
Click "Test Wake Word" button - say "Atlas"
```

---

### Transcription Not Working

**Check 1: Is Whisper configured?**
```
Open Voice Diagnostics
Look at "Whisper STT" section
Should say "✅ Configured"
If not, add OpenAI API key in Settings
```

**Check 2: Test Whisper directly**
```
Open Voice Diagnostics
Click "☁️ Test Whisper"
Speak for 3-6 seconds
Should show transcript
```

**Fallback:** If Whisper not available, system uses Windows Speech (less accurate)

---

##  Error Messages Explained

### ⚠️ "Speech recognizer not installed"
**Cause:** Windows Speech Recognition not enabled
**Fix:**
1. Open Windows Settings
2. Go to Time & Language → Speech
3. Enable Speech Recognition
4. Restart Atlas

### ⚠️ "No microphone detected"
**Cause:** No audio input device found or microphone disabled
**Fix:**
1. Check microphone is plugged in
2. Open Windows Sound Settings
3. Ensure microphone is enabled and not muted
4. Set as default device
5. Restart Atlas

### ⚠️ "Using Windows Speech fallback"
**Cause:** OpenAI API key not configured
**Fix:**
1. Open Atlas Settings
2. Go to AI Keys tab
3. Add OpenAI API key
4. Restart Atlas

---

## Voice Pipeline Flow

```
User says "Atlas" 
    ↓
Wake Word Detected (Windows Speech)
    ↓
Activation Sound Plays
    ↓
"🎤 Listening..." shown
    ↓
User speaks command
    ↓
STT Transcription:
  • If OpenAI key: Whisper (high accuracy)
  • If no key: Windows Speech (lower accuracy)
    ↓
Transcript appears in chat
    ↓
Sent to AI for processing
    ↓
Response generated
    ↓
TTS speaks response
    ↓
Wake word listening resumes
```

---

## Testing Checklist

### Initial Setup
- [ ] Windows Speech Recognition enabled
- [ ] Microphone working in Windows
- [ ] OpenAI API key configured (optional, for Whisper)

### Wake Word Test
- [ ] Open app
- [ ] Wake word toggle is ON (green)
- [ ] Say "Atlas"
- [ ] Activation sound plays
- [ ] "🎤 Listening..." appears
- [ ] Speak command
- [ ] Transcript appears
- [ ] Get AI response

### Push-to-Talk Test
- [ ] Click microphone button
- [ ] Speak for 3-6 seconds
- [ ] Transcript appears
- [ ] Get AI response

### Diagnostics Test
- [ ] Open Voice Diagnostics
- [ ] System Status shows ✅
- [ ] All sections populate with data
- [ ] Click "Test Wake Word" - say "Atlas"
- [ ] Click "Test Whisper" - speak command
- [ ] Click "Open Log" - file opens

---

## Log Files

### Wake Word Log
**Location:** `%AppData%\AtlasAI\wake_word_log.txt`
**Contains:**
- Wake word detection events
- Confidence scores
- Errors and warnings
- Audio state changes

**How to open:**
1. Open Voice Diagnostics
2. Click "📄 Open Log" button
OR
1. Press Windows+R
2. Type: `%AppData%\AtlasAI`
3. Open `wake_word_log.txt`

---

## API Keys

### Where to get them:
- **OpenAI:** https://platform.openai.com/api-keys
- **ElevenLabs:** https://elevenlabs.io/app/settings

### Where to add them:
1. Open Atlas Settings
2. Go to "AI Keys" tab
3. Paste API key
4. Click "Save"
5. Restart Atlas

---

## Performance Tips

### Reduce CPU Usage
- Use Windows Speech for both wake word AND transcription (don't add OpenAI key)
- Trade-off: Lower transcription accuracy

### Improve Accuracy
- Add OpenAI API key (enables Whisper)
- Use USB microphone instead of built-in mic
- Speak clearly and avoid background noise

### Reduce Latency
- Use built-in microphone (faster than Bluetooth)
- Ensure good internet connection (for Whisper)
- Close unnecessary applications

---

## Advanced

### Customize Wake Words
Edit `Voice\WakeWordService.cs` line 61:
```csharp
private readonly string[] _wakeWords = { 
    "Atlas", "atlas",
    "Hey Atlas", "Okay Atlas", // Add more here
};
```

### Change Confidence Threshold
Currently set to 0.0 (accept all) for maximum detection
Edit `Voice\WakeWordService.cs` line 66:
```csharp
private const double MinConfidence = 0.0; // Increase to 0.5 for stricter matching
```

### Change Cooldown Period
Currently 1.5 seconds between wake word triggers
Edit `Voice\WakeWordService.cs` line 67:
```csharp
private const double DebounceSeconds = 1.5; // Adjust as needed
```

---

## Support

### If voice features still don't work:
1. Check all error messages in diagnostics panel
2. Review wake word log file
3. Ensure Windows Speech Recognition works in other apps (e.g., Notepad dictation)
4. Restart computer
5. Reinstall Windows Speech Recognition language pack

### Known Limitations:
- Bluetooth headphones (AirPods) may not work for wake word detection
- Works best with wired USB microphones
- English language only by default
- Requires Windows 10 or later

---

## Build Status
✅ **All changes compiled successfully**
✅ **No errors or warnings**
✅ **Ready for testing**
