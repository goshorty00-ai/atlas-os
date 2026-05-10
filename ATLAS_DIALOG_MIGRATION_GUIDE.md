# Atlas Dialog Service - Migration Guide

## Replacing MessageBox.Show with AtlasDialogService

### Setup

Add using statement:
```csharp
using AtlasAI.Services;
```

Get service instance:
```csharp
var dialogService = App.DialogService;
```

---

## Common Patterns

### 1. Simple Information Message

**BEFORE:**
```csharp
MessageBox.Show("Operation completed successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
```

**AFTER:**
```csharp
await App.DialogService.ShowInfoAsync("Success", "Operation completed successfully.");
```

---

### 2. Warning Message

**BEFORE:**
```csharp
MessageBox.Show("This action cannot be undone.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
```

**AFTER:**
```csharp
await App.DialogService.ShowWarningAsync("Warning", "This action cannot be undone.");
```

---

### 3. Error Message

**BEFORE:**
```csharp
MessageBox.Show($"Failed to save file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
```

**AFTER:**
```csharp
await App.DialogService.ShowErrorAsync("Error", $"Failed to save file: {ex.Message}");
```

---

### 4. Confirmation (Yes/No)

**BEFORE:**
```csharp
var result = MessageBox.Show("Are you sure you want to delete this item?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
if (result == MessageBoxResult.Yes)
{
    // Delete
}
```

**AFTER:**
```csharp
if (await App.DialogService.ShowConfirmAsync("Confirm", "Are you sure you want to delete this item?"))
{
    // Delete
}
```

---

### 5. OK/Cancel

**BEFORE:**
```csharp
var result = MessageBox.Show("Save changes before closing?", "Confirm", MessageBoxButton.OKCancel);
if (result == MessageBoxResult.OK)
{
    // Save
}
```

**AFTER:**
```csharp
var result = await App.DialogService.ShowAsync("Confirm", "Save changes before closing?", AtlasDialogButtons.OKCancel, AtlasDialogIcon.Question);
if (result == AtlasDialogResult.OK)
{
    // Save
}
```

---

### 6. "Don't Show Again" Option

**BEFORE:**
```csharp
MessageBox.Show("Tip: You can press Ctrl+S to save quickly.", "Tip", MessageBoxButton.OK, MessageBoxImage.Information);
```

**AFTER:**
```csharp
await App.DialogService.ShowAsync(
    "Tip",
    "Tip: You can press Ctrl+S to save quickly.",
    AtlasDialogButtons.OK,
    AtlasDialogIcon.Info,
    showDontShowAgain: true,
    dontShowAgainKey: "quick_save_tip");
```

---

### 7. From Non-Async Method

If you can't make the method async, use `.GetAwaiter().GetResult()` or fire-and-forget:

**BEFORE:**
```csharp
private void Button_Click(object sender, RoutedEventArgs e)
{
    MessageBox.Show("Clicked!", "Info");
}
```

**AFTER (Option 1 - Make async):**
```csharp
private async void Button_Click(object sender, RoutedEventArgs e)
{
    await App.DialogService.ShowInfoAsync("Info", "Clicked!");
}
```

**AFTER (Option 2 - Sync wrapper):**
```csharp
private void Button_Click(object sender, RoutedEventArgs e)
{
    App.DialogService.ShowInfoAsync("Info", "Clicked!").GetAwaiter().GetResult();
}
```

---

### 8. Complex Buttons (Yes/No/Cancel)

**BEFORE:**
```csharp
var result = MessageBox.Show("Save changes?", "Confirm", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
switch (result)
{
    case MessageBoxResult.Yes:
        Save();
        break;
    case MessageBoxResult.No:
        // Don't save
        break;
    case MessageBoxResult.Cancel:
        return; // Cancel close
}
```

**AFTER:**
```csharp
var result = await App.DialogService.ShowAsync("Confirm", "Save changes?", AtlasDialogButtons.YesNoCancel, AtlasDialogIcon.Question);
switch (result)
{
    case AtlasDialogResult.Yes:
        Save();
        break;
    case AtlasDialogResult.No:
        // Don't save
        break;
    case AtlasDialogResult.Cancel:
        return; // Cancel close
}
```

---

## Result Enum Mapping

| MessageBoxResult | AtlasDialogResult |
|-----------------|-------------------|
| OK | OK |
| Cancel | Cancel |
| Yes | Yes |
| No | No |
| None | None |

---

## Button Enum Mapping

| MessageBoxButton | AtlasDialogButtons |
|-----------------|-------------------|
| OK | OK |
| OKCancel | OKCancel |
| YesNo | YesNo |
| YesNoCancel | YesNoCancel |
| (custom) | RetryCancel |
| (custom) | AbortRetryIgnore |

---

## Icon Enum Mapping

| MessageBoxImage | AtlasDialogIcon |
|----------------|----------------|
| None | None |
| Information | Info |
| Warning | Warning |
| Error | Error |
| Question | Question |
| (new) | Success |

---

## Thread Safety

The service automatically marshals to the UI thread, so you can call it from any thread:

```csharp
Task.Run(async () =>
{
    // This works even from background thread!
    await App.DialogService.ShowInfoAsync("Background", "Called from background thread");
});
```

---

## Files to Update

Run this to find all MessageBox.Show usages:
```powershell
Get-ChildItem -Recurse -Filter *.cs | Select-String "MessageBox\.Show" -List
```

Found in 25 files - update each one following the patterns above.
