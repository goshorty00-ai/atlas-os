# Download Manager Fixes

## Issues Fixed

### 1. Incorrect Filename Display
**Problem:** Downloads were showing cryptic hash-like names instead of proper filenames.

**Root Causes:**
- The `GuessFileName` method was too simplistic and didn't properly extract filenames from URLs
- Content-Disposition headers from servers were not being parsed
- Files were being saved in individual folders named with job IDs

**Solutions:**
- Enhanced `GuessFileName` to properly decode URL-encoded filenames and extract from URL segments
- Added Content-Disposition header parsing in `DownloadToFileAsync` to extract server-provided filenames
- Modified folder creation logic to save regular downloads directly to the root folder instead of creating individual subfolders
- Added fallback to use the filename property when available in `GetUiState`

### 2. Incorrect Progress Display
**Problem:** Progress was showing as 0.8% instead of 80%

**Root Cause:**
- Backend was sending progress as a decimal (0-1) but UI expected percentage (0-100)

**Solution:**
- Modified `GetUiState` to convert progress from decimal to percentage: `progress = Math.Clamp(j.Progress * 100, 0, 100)`
- Updated UI comment to clarify that progress is now in percentage format

### 3. File Organization
**Problem:** Each download was creating its own subfolder with a hash-like name

**Solution:**
- Changed folder creation logic to only create subfolders for:
  - Playlist/package downloads (identified by `MetaTrackNumber > 0`)
  - Transcoded MP3 downloads
- Regular single-file downloads now save directly to the configured download folder

## Files Modified

1. `Modules/Downloader/DownloadManager.cs`
   - Enhanced `GuessFileName` method
   - Added Content-Disposition header parsing in `DownloadToFileAsync`
   - Fixed progress calculation in `GetUiState` (decimal to percentage)
   - Improved folder creation logic in `ExecuteJobAsync`

2. `Figma/Futuristic AI Command Center (6)/src/app/components/DownloadManager.tsx`
   - Added comment clarifying progress is in percentage format

## Testing Recommendations

1. Test with various URL types:
   - Direct file URLs
   - URLs with query parameters
   - URL-encoded filenames
   - Short URLs that redirect

2. Verify Content-Disposition header parsing:
   - Download from servers that provide proper filenames
   - Test with special characters in filenames

3. Check progress display:
   - Verify progress shows as percentage (0-100%)
   - Confirm progress bar animates correctly

4. Verify file organization:
   - Single downloads should save to root folder
   - Playlist/CSV imports should create subfolders
   - No more cryptic hash-named folders for regular downloads
