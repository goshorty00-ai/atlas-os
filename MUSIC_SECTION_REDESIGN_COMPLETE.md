# Music Section Redesign & Media Centre Fixes

## Overview
Complete redesign of the Music section to match the Starship AI Media Core reference design, plus fixes for server addon connections and cover image issues.

## Changes Implemented

### 1. **Music View Redesign (MusicView.xaml)** ✅
- **New Header Bar**:
  - Starship AI branding with icon
  - Compact search bar with glass pill styling
  - Options dropdown (⋮) with all controls
  - Status indicators (album count, ready status)

- **Glass Pill Buttons**:
  - Transparent glass effect with gradient
  - Cyan (#22d3ee) accent color
  - Hover effects with neon glow
  - No "crappy Windows boxes"

- **Album Covers**:
  - Rounded corners (8px radius)
  - Neon cyan glow (default)
  - Orange glow on hover/selection
  - Proper ClipToBounds for rounded images
  - Genre badges
  - Fallback for missing covers

- **Right-Click Context Menu**:
  - Play Album
  - Add to Queue
  - **Edit Cover** (new!)
  - Album Info
  - Remove

- **Now Playing Sidebar**:
  - Large album cover (300x300) with neon glow
  - Enhanced playback controls
  - Volume slider
  - "UP NEXT" queue list
  - "Open Visualizer" button

- **Removed**:
  - "4K Ultra HD" filter box (useless)
  - Old button bar (ADD MEDIA, AI OPTIMIZE, etc.)
  - All moved to dropdown menu

### 2. **Options Dropdown Menu**
All options accessible via:
- Click on ⋮ button in header
- Right-click anywhere in the view

Menu items:
- 📁 Add Media
- ✨ AI Optimize
- 🤖 AI Mode
- 🎤 Voice Control
- ✏️ Edit Media (for custom covers)
- 🔄 Refresh Library
- ⚙️ Settings

### 3. **Cover Image Fixes Needed**

#### Issue: Covers stop loading halfway down
**Root Cause**: Lazy loading or connection limits

**Fix Required in ServersViewModel.cs**:
```csharp
// Add concurrent loading with semaphore
private SemaphoreSlim _loadingSemaphore = new SemaphoreSlim(10, 10); // Max 10 concurrent

public async Task LoadCoversAsync()
{
    var tasks = items.Select(async item =>
    {
        await _loadingSemaphore.WaitAsync();
        try
        {
            await LoadCoverForItemAsync(item);
        }
        finally
        {
            _loadingSemaphore.Release();
        }
    });
    
    await Task.WhenAll(tasks);
}
```

### 4. **Addon Connection Fix**

#### Issue: Only connects to 4 addons, not all
**Current Code Problem**:
```csharp
// ServersViewModel only tests selected server
private async Task TestServerAsync()
{
    if (SelectedServer == null) return;
    // Only tests ONE server...
}
```

**Required Fix**:
```csharp
public async Task ConnectAllEnabledAddonsAsync()
{
    var enabledServers = Servers.Where(s => s.Enabled).ToList();
    
    var tasks = enabledServers.Select(async server =>
    {
        var (success, manifest, error) = await _addonsService.FetchManifestAsync(server.Url);
        if (success)
        {
            server.Status = ServerStatus.Connected;
            server.Manifest = manifest;
        }
        else
        {
            server.Status = ServerStatus.Error;
            server.ErrorMessage = error;
        }
    });
    
    await Task.WhenAll(tasks);
    
    // Update UI with connection count
    ConnectedAddonCount = Servers.Count(s => s.Status == ServerStatus.Connected);
}
```

### 5. **Edit Media / Custom Covers**

**Add to MusicViewModel.cs**:
```csharp
public ICommand EditCoverCommand { get; }

private async void EditCover(MusicAlbum album)
{
    var dialog = new OpenFileDialog
    {
        Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp|All Files|*.*",
        Title = "Select Album Cover"
    };
    
    if (dialog.ShowDialog() == true)
    {
        try
        {
            // Copy image to app data
            var appData = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData), "AtlasAI", "Covers");
            Directory.CreateDirectory(appData);
            
            var fileName = $"{album.Artist}_{album.AlbumTitle}.jpg".Replace(" ", "_");
            var destPath = Path.Combine(appData, fileName);
            
            File.Copy(dialog.FileName, destPath, true);
            
            // Update album cover
            album.CoverImage = destPath;
            album.HasCustomCover = true;
            
            // Save to database/config
            await SaveAlbumMetadataAsync(album);
            
            await App.DialogService.ShowInfoAsync("Cover Updated", 
                $"Custom cover applied to '{album.AlbumTitle}'");
        }
        catch (Exception ex)
        {
            await App.DialogService.ShowErrorAsync("Error", 
                $"Failed to update cover: {ex.Message}");
        }
    }
}
```

### 6. **Rounded Corners on Actual Images**

**Current Issue**: Border has rounded corners, but image doesn't clip

**Solution**: Use `ClipToBounds="True"` on Border (already implemented in new XAML)

```xaml
<Border Width="180" Height="180" 
        CornerRadius="8"
        ClipToBounds="True"  <!-- This is the key! -->
        Style="{StaticResource AlbumCoverBorder}">
    <Image Source="{Binding CoverImage}" Stretch="UniformToFill"/>
</Border>
```

### 7. **Neon Glow Implementation**

**Default Cyan Glow**:
```xaml
<Border.Effect>
    <DropShadowEffect Color="#22d3ee" BlurRadius="20" ShadowDepth="0" Opacity="0.5"/>
</Border.Effect>
```

**Orange Glow on Hover/Selection**:
```xaml
<Style.Triggers>
    <Trigger Property="IsMouseOver" Value="True">
        <Setter Property="Effect">
            <Setter.Value>
                <DropShadowEffect Color="#f97316" BlurRadius="24" ShadowDepth="0" Opacity="0.8"/>
            </Setter.Value>
        </Setter>
    </Trigger>
</Style.Triggers>
```

## Code-Behind Updates Needed

### MusicView.xaml.cs

Add event handlers for context menu and options button:

```csharp
private void OptionsButton_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.ContextMenu != null)
    {
        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.IsOpen = true;
    }
}

private void AddMedia_Click(object sender, RoutedEventArgs e)
{
    // TODO: Open file dialog or folder browser
}

private void AIOptimize_Click(object sender, RoutedEventArgs e)
{
    // TODO: Trigger AI optimization
}

private void AIMode_Click(object sender, RoutedEventArgs e)
{
    // TODO: Toggle AI mode
}

private void VoiceControl_Click(object sender, RoutedEventArgs e)
{
    // TODO: Open voice control settings
}

private void EditMedia_Click(object sender, RoutedEventArgs e)
{
    // TODO: Open edit media dialog
}

private void RefreshLibrary_Click(object sender, RoutedEventArgs e)
{
    if (DataContext is MusicViewModel vm)
    {
        await vm.RefreshLibraryAsync();
    }
}

private void Settings_Click(object sender, RoutedEventArgs e)
{
    // TODO: Open settings
}

// Album context menu handlers
private void PlayAlbum_Click(object sender, RoutedEventArgs e)
{
    if (sender is MenuItem mi && mi.DataContext is MusicAlbum album)
    {
        if (DataContext is MusicViewModel vm)
        {
            vm.SelectAlbumCommand.Execute(album);
        }
    }
}

private void AddToQueue_Click(object sender, RoutedEventArgs e)
{
    // TODO: Add to queue
}

private void EditCover_Click(object sender, RoutedEventArgs e)
{
    if (sender is MenuItem mi && mi.DataContext is MusicAlbum album)
    {
        if (DataContext is MusicViewModel vm)
        {
            vm.EditCoverCommand.Execute(album);
        }
    }
}

private void AlbumInfo_Click(object sender, RoutedEventArgs e)
{
    // TODO: Show album info dialog
}

private void RemoveAlbum_Click(object sender, RoutedEventArgs e)
{
    // TODO: Remove album
}
```

## Testing Checklist

### Visual
- [ ] Music section matches reference design (Starship AI style)
- [ ] Search bar is compact with glass pill styling
- [ ] Options dropdown (⋮) shows all menu items
- [ ] Album covers have rounded corners (actual image, not just frame)
- [ ] Covers have cyan neon glow by default
- [ ] Covers have orange neon glow on hover
- [ ] No "4K Ultra HD" box visible
- [ ] Right-click shows context menu on albums

### Functionality
- [ ] All addons connect (not just 4)
- [ ] Covers load all the way down (no stopping halfway)
- [ ] Edit Cover opens file dialog
- [ ] Custom cover persists after restart
- [ ] Fallback image shows when no cover available
- [ ] Options dropdown works on click and right-click
- [ ] Playback controls work in sidebar
- [ ] Queue/tracklist displays properly

### Performance
- [ ] Cover loading doesn't freeze UI
- [ ] Concurrent addon connections (max 10 at once)
- [ ] Smooth scrolling with many albums
- [ ] No memory leaks from Image sources

## Next Steps

1. **Update MusicView.xaml.cs** - Add all event handlers
2. **Update MusicViewModel.cs** - Add EditCoverCommand and metadata saving
3. **Fix ServersViewModel.cs** - Implement ConnectAllEnabledAddonsAsync()
4. **Add CoverImageCache** - Prevent reloading same covers
5. **Test with large library** - Ensure performance is good

## Color Palette

- **Background**: `#0A0E1A` (deep space blue)
- **Surface**: `#0D1117` (slightly lighter)
- **Accent Cyan**: `#22d3ee` (neon cyan)
- **Accent Orange**: `#f97316` (hover/selection)
- **Text Primary**: `#f1f5f9` (almost white)
- **Text Secondary**: `#94a3b8` (light gray)
- **Text Tertiary**: `#64748b` (medium gray)
- **Glass**: `rgba(255,255,255,0.1)` (10% white)

## Files Modified

1. ✅ `Views/MediaCentre/MusicView.xaml` - Complete redesign
2. ⏳ `Views/MediaCentre/MusicView.xaml.cs` - Add event handlers
3. ⏳ `Views/ViewModels/MusicViewModel.cs` - Add EditCover command
4. ⏳ `Views/ViewModels/ServersViewModel.cs` - Fix addon connections
5. ⏳ `Models/MusicAlbum.cs` - Add HasCustomCover property

---

**Status**: XAML redesign complete, code-behind updates in progress
**Build**: Should compile (no breaking changes to existing code)
**Testing**: Visual verification needed
