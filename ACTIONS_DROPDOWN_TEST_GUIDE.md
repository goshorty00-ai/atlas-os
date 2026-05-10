# Actions Dropdown Quick Test Guide

## Visual Verification

### Header Layout
1. **Search Bar**
   - ✅ Should be compact (~600px max width, not full-width)
   - ✅ Should be left-aligned in the header
   - ✅ Should show "CTRL+K" hint on the right side
   - ✅ Should have cyan outline on focus

2. **Actions Pill Button**
   - ✅ Located in top-right of header (Grid.Column="2")
   - ✅ Shows "ACTIONS ▾" text with chevron
   - ✅ Has cyan outline border
   - ✅ Shows subtle cyan glow on hover
   - ✅ Rounded pill shape (highly rounded corners)

### Dropdown Behavior
1. **Opening/Closing**
   - Click Actions pill → dropdown appears below button
   - Click outside dropdown → closes automatically
   - Click any action item → executes command AND closes
   - ESC key → closes dropdown

2. **Dropdown Styling**
   - ✅ Dark glass background (matches theme)
   - ✅ Cyan border with glow effect
   - ✅ Rounded corners (~16px)
   - ✅ Positioned 8px below the Actions button
   - ✅ Shadow/glow effect visible

3. **Action Items**
   - ✅ 8 items listed vertically
   - ✅ Each has icon + text
   - ✅ Hover shows glass background + cyan border
   - ✅ Press shows orange glow
   - ✅ All items are left-aligned

## Functional Testing

### Test Each Action Button
Run the app and verify each action in the dropdown works:

1. **Add Media** 
   - Opens folder picker or media scan dialog
   - Command: `ScanSelectedCategoryCommand`

2. **AI Optimize**
   - Triggers library optimization
   - Command: `OptimizeLibraryCommand`

3. **AI Mode**
   - Opens AI Mode window
   - Event: `AiModeButton_Click`

4. **Voice**
   - Opens Voice Mode window
   - Event: `VoiceModeButton_Click`

5. **Fix Albums**
   - Runs album metadata fix
   - Command: `FixMusicAlbumsCommand`

6. **Play All**
   - Starts playing all media in current view
   - Command: `PlayAllMusicCommand`

7. **Undo**
   - Reverts last change
   - Command: `UndoLastChangeCommand`

8. **Clear**
   - Clears current category
   - Command: `ClearCategoryCommand`

## Known Good State
- ✅ Build succeeds without errors
- ✅ No XAML compilation warnings
- ✅ All resources (NeoGlass*, NeoBorder*, etc.) exist in theme
- ✅ Old button row is hidden (Visibility="Collapsed")
- ✅ Code-behind handler added and compiles

## Rollback (if needed)
To restore old button row:
1. Open `Controls/MediaCenterControl.xaml`
2. Find line ~1919: `<Grid Grid.Row="0" Grid.Column="1" Grid.ColumnSpan="2" VerticalAlignment="Center" Visibility="Collapsed">`
3. Remove `Visibility="Collapsed"` attribute
4. Optionally hide Actions pill by adding `Visibility="Collapsed"` to ActionsDropdownToggle

## Hot Reload Note
If app is running in debug mode with hot reload enabled:
- Changes should apply automatically
- If not, stop debugging and restart
- Full rebuild recommended for clean state
