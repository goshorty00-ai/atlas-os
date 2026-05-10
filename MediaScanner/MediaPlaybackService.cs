// Media Playback Service
// Manages playback queue, current track, and player state

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AtlasAI.MediaScanner
{
    /// <summary>
    /// Service for managing media playback queue and state
    /// </summary>
    public class MediaPlaybackService
    {
        public static MediaPlaybackService Instance { get; private set; }

        private List<MediaItem> _queue = new List<MediaItem>();
        private int _currentIndex = -1;
        private bool _isShuffled = false;
        private bool _isRepeat = false;
        private long _lastNavigationUtcTicks = 0;
        
        public event EventHandler<MediaItem>? CurrentMediaChanged;
        public event EventHandler? QueueChanged;
        
        public MediaItem? CurrentMedia
        {
            get
            {
                lock (_queue)
                {
                    return _currentIndex >= 0 && _currentIndex < _queue.Count ? _queue[_currentIndex] : null;
                }
            }
        }
        
        public List<MediaItem> Queue
        {
            get
            {
                lock (_queue)
                {
                    return _queue.ToList();
                }
            }
        }
        
        public MediaPlaybackService() : this(false)
        {
        }

        private MediaPlaybackService(bool setAsGlobal)
        {
            if (setAsGlobal && Instance == null)
                Instance = this;
        }

        public static MediaPlaybackService GetOrCreate()
        {
            if (Instance != null) return Instance;
            Instance = new MediaPlaybackService(true);
            return Instance;
        }

        public static MediaPlaybackService CreateIsolated()
        {
            return new MediaPlaybackService(false);
        }

        public bool IsShuffled
        {
            get => _isShuffled;
            set
            {
                _isShuffled = value;
                if (value)
                {
                    ShuffleQueue();
                }
            }
        }
        
        public bool IsRepeat
        {
            get => _isRepeat;
            set => _isRepeat = value;
        }
        
        /// <summary>
        /// Play a single media item
        /// </summary>
        public void PlaySingle(MediaItem item)
        {
            lock (_queue)
            {
                _queue.Clear();
                _queue.Add(item);
                _currentIndex = 0;
            }
            
            CurrentMediaChanged?.Invoke(this, item);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Play an album (all tracks in the same folder)
        /// </summary>
        public void PlayAlbum(List<MediaItem> albumTracks, MediaItem? startTrack = null)
        {
            System.Diagnostics.Debug.WriteLine("[PlaybackService] ===== PLAY ALBUM CALLED =====");
            System.Diagnostics.Debug.WriteLine($"[PlaybackService] Album has {albumTracks.Count} tracks");
            
            MediaItem? currentTrack = null;
            
            lock (_queue)
            {
                _queue.Clear();
                _queue.AddRange(albumTracks.OrderBy(t => t.TrackNumber ?? 999).ThenBy(t => t.DisplayName));
                System.Diagnostics.Debug.WriteLine($"[PlaybackService] Queue populated with {_queue.Count} tracks");
                
                if (startTrack != null)
                {
                    _currentIndex = _queue.FindIndex(t => t.FilePath == startTrack.FilePath);
                    if (_currentIndex < 0) _currentIndex = 0;
                    System.Diagnostics.Debug.WriteLine($"[PlaybackService] Start track specified: {startTrack.DisplayName}, index: {_currentIndex}");
                }
                else
                {
                    _currentIndex = 0;
                    System.Diagnostics.Debug.WriteLine("[PlaybackService] No start track - starting at index 0");
                }
                
                if (_currentIndex >= 0 && _currentIndex < _queue.Count)
                {
                    currentTrack = _queue[_currentIndex];
                }
            }
            
            if (currentTrack != null)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaybackService] Firing CurrentMediaChanged event for: {currentTrack.DisplayName}");
                System.Diagnostics.Debug.WriteLine($"[PlaybackService] File path: {currentTrack.FilePath}");
                CurrentMediaChanged?.Invoke(this, currentTrack);
                System.Diagnostics.Debug.WriteLine("[PlaybackService] CurrentMediaChanged event fired");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[PlaybackService] ERROR: Invalid current index: {_currentIndex}");
            }
            
            QueueChanged?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine("[PlaybackService] ===== PLAY ALBUM COMPLETE =====");
        }

        public void PlayQueue(List<MediaItem> items, int startIndex = 0)
        {
            if (items == null || items.Count == 0) return;

            lock (_queue)
            {
                _queue.Clear();
                _queue.AddRange(items);
                _currentIndex = Math.Clamp(startIndex, 0, _queue.Count - 1);
            }

            if (_currentIndex >= 0 && _currentIndex < _queue.Count)
                CurrentMediaChanged?.Invoke(this, _queue[_currentIndex]);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Add items to queue
        /// </summary>
        public void AddToQueue(List<MediaItem> items)
        {
            lock (_queue)
            {
                _queue.AddRange(items);
            }
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        public void QueueNext(MediaItem item)
        {
            lock (_queue)
            {
                if (_queue.Count == 0)
                {
                    _queue.Add(item);
                    _currentIndex = 0;
                    CurrentMediaChanged?.Invoke(this, item);
                }
                else
                {
                    var insertIndex = Math.Clamp(_currentIndex + 1, 0, _queue.Count);
                    _queue.Insert(insertIndex, item);
                }
            }
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Play next track in queue
        /// </summary>
        public void PlayNext()
        {
            try
            {
                var nowTicks = DateTime.UtcNow.Ticks;
                var prevTicks = Interlocked.Read(ref _lastNavigationUtcTicks);
                if (prevTicks != 0 && (nowTicks - prevTicks) < TimeSpan.FromMilliseconds(250).Ticks)
                    return;
                Interlocked.Exchange(ref _lastNavigationUtcTicks, nowTicks);

                // Lock to ensure queue stability during index calculation
                lock (_queue)
                {
                    if (_queue.Count == 0) return;
                    
                    _currentIndex++;
                    
                    if (_currentIndex >= _queue.Count)
                    {
                        if (_isRepeat)
                        {
                            _currentIndex = 0;
                        }
                        else
                        {
                            _currentIndex = _queue.Count - 1;
                            return;
                        }
                    }
                }
                
                // Use Application.Current.Dispatcher to ensure thread safety
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => 
                {
                    try
                    {
                        // CRITICAL: Re-check bounds inside Dispatcher as queue might have changed
                        if (_currentIndex >= 0 && _currentIndex < _queue.Count)
                        {
                            CurrentMediaChanged?.Invoke(this, _queue[_currentIndex]);
                        }
                    }
                    catch (Exception ex)
                    {
                         System.Diagnostics.Debug.WriteLine($"[PlaybackService] PlayNext Dispatcher Error: {ex.Message}");
                    }
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaybackService] PlayNext Error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Play previous track in queue
        /// </summary>
        public void PlayPrevious()
        {
            try
            {
                var nowTicks = DateTime.UtcNow.Ticks;
                var prevTicks = Interlocked.Read(ref _lastNavigationUtcTicks);
                if (prevTicks != 0 && (nowTicks - prevTicks) < TimeSpan.FromMilliseconds(250).Ticks)
                    return;
                Interlocked.Exchange(ref _lastNavigationUtcTicks, nowTicks);

                lock (_queue)
                {
                    if (_queue.Count == 0) return;
                    
                    _currentIndex--;
                    
                    if (_currentIndex < 0)
                    {
                        if (_isRepeat)
                        {
                            _currentIndex = _queue.Count - 1;
                        }
                        else
                        {
                            _currentIndex = 0;
                            return;
                        }
                    }
                }
                
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => 
                {
                    try
                    {
                        // CRITICAL: Re-check bounds inside Dispatcher
                        if (_currentIndex >= 0 && _currentIndex < _queue.Count)
                        {
                            CurrentMediaChanged?.Invoke(this, _queue[_currentIndex]);
                        }
                    }
                    catch (Exception ex)
                    {
                         System.Diagnostics.Debug.WriteLine($"[PlaybackService] PlayPrevious Dispatcher Error: {ex.Message}");
                    }
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaybackService] PlayPrevious Error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Play specific track from queue
        /// </summary>
        public void PlayFromQueue(int index)
        {
            lock (_queue)
            {
                if (index < 0 || index >= _queue.Count) return;
                _currentIndex = index;
            }
            CurrentMediaChanged?.Invoke(this, _queue[_currentIndex]);
        }

        public void RemoveFromQueue(int index)
        {
            MediaItem? nextCurrent = null;

            lock (_queue)
            {
                if (index < 0 || index >= _queue.Count) return;

                var removedCurrent = index == _currentIndex;
                _queue.RemoveAt(index);

                if (_queue.Count == 0)
                {
                    _currentIndex = -1;
                }
                else if (removedCurrent)
                {
                    _currentIndex = Math.Clamp(index, 0, _queue.Count - 1);
                    nextCurrent = _queue[_currentIndex];
                }
                else if (index < _currentIndex)
                {
                    _currentIndex--;
                }
            }

            if (nextCurrent != null)
            {
                CurrentMediaChanged?.Invoke(this, nextCurrent);
            }

            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        public void MoveQueueItem(int fromIndex, int toIndex)
        {
            lock (_queue)
            {
                if (fromIndex < 0 || fromIndex >= _queue.Count) return;
                if (toIndex < 0 || toIndex >= _queue.Count) return;
                if (fromIndex == toIndex) return;

                var item = _queue[fromIndex];
                _queue.RemoveAt(fromIndex);
                _queue.Insert(toIndex, item);

                if (_currentIndex == fromIndex)
                {
                    _currentIndex = toIndex;
                }
                else if (fromIndex < _currentIndex && toIndex >= _currentIndex)
                {
                    _currentIndex--;
                }
                else if (fromIndex > _currentIndex && toIndex <= _currentIndex)
                {
                    _currentIndex++;
                }
            }

            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Clear the queue
        /// </summary>
        public void ClearQueue()
        {
            lock (_queue)
            {
                _queue.Clear();
                _currentIndex = -1;
            }
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Shuffle the queue
        /// </summary>
        private void ShuffleQueue()
        {
            lock (_queue)
            {
                if (_queue.Count <= 1) return;
                
                var currentMedia = _currentIndex >= 0 && _currentIndex < _queue.Count ? _queue[_currentIndex] : null;
                var random = new Random();
                _queue = _queue.OrderBy(x => random.Next()).ToList();
                
                // Keep current track at current position
                if (currentMedia != null)
                {
                    var newIndex = _queue.FindIndex(t => t.FilePath == currentMedia.FilePath);
                    if (newIndex >= 0 && newIndex != _currentIndex)
                    {
                        var temp = _queue[_currentIndex];
                        _queue[_currentIndex] = _queue[newIndex];
                        _queue[newIndex] = temp;
                    }
                }
            }
            
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
