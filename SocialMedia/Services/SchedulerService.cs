using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using AtlasAI.SocialMedia.Models;

namespace AtlasAI.SocialMedia.Services
{
    /// <summary>
    /// Queue-based scheduling service with approval workflow
    /// Handles scheduled posts and publishing triggers
    /// </summary>
    public class SchedulerService : IDisposable
    {
        private readonly SocialMediaMemoryService _memoryService;
        private readonly PlatformPublisherService _publisher;
        private System.Timers.Timer? _checkTimer;
        private bool _isRunning;
        
        public event EventHandler<ScheduledPost>? PostDue;
        public event EventHandler<(ScheduledPost Post, string Result)>? PostPublished;
        public event EventHandler<(ScheduledPost Post, string Error)>? PostFailed;
        
        public SchedulerService(SocialMediaMemoryService memoryService, PlatformPublisherService publisher)
        {
            _memoryService = memoryService;
            _publisher = publisher;
        }
        
        /// <summary>
        /// Start the scheduler - checks for due posts every minute
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            
            _checkTimer = new System.Timers.Timer(60000); // Check every minute
            _checkTimer.Elapsed += async (s, e) => await CheckDuePostsAsync();
            _checkTimer.Start();
            _isRunning = true;
        }
        
        /// <summary>
        /// Stop the scheduler
        /// </summary>
        public void Stop()
        {
            _checkTimer?.Stop();
            _checkTimer?.Dispose();
            _checkTimer = null;
            _isRunning = false;
        }
        
        /// <summary>
        /// Schedule content for posting
        /// </summary>
        public async Task<ScheduledPost> ScheduleAsync(
            string contentId,
            SocialPlatform platform,
            DateTime scheduledTime,
            bool requiresApproval = true)
        {
            var schedule = new ScheduledPost
            {
                ContentId = contentId,
                Platform = platform,
                ScheduledTime = scheduledTime,
                RequiresApproval = requiresApproval,
                Status = requiresApproval ? ScheduleStatus.Pending : ScheduleStatus.Approved
            };
            
            await _memoryService.SchedulePostAsync(schedule);
            
            // Update content status
            await _memoryService.UpdateContentStatusAsync(contentId, ContentStatus.Scheduled);
            
            return schedule;
        }
        
        /// <summary>
        /// Schedule content for multiple platforms
        /// </summary>
        public async Task<List<ScheduledPost>> ScheduleMultiPlatformAsync(
            string contentId,
            List<SocialPlatform> platforms,
            DateTime scheduledTime,
            bool staggerPosts = true,
            int staggerMinutes = 30)
        {
            var schedules = new List<ScheduledPost>();
            var currentTime = scheduledTime;
            
            foreach (var platform in platforms)
            {
                var schedule = await ScheduleAsync(contentId, platform, currentTime);
                schedules.Add(schedule);
                
                if (staggerPosts)
                    currentTime = currentTime.AddMinutes(staggerMinutes);
            }
            
            return schedules;
        }
        
        /// <summary>
        /// Approve a scheduled post
        /// </summary>
        public async Task ApproveAsync(string scheduleId, string approvedBy = "User")
        {
            await _memoryService.ApproveScheduleAsync(scheduleId, approvedBy);
        }
        
        /// <summary>
        /// Cancel a scheduled post
        /// </summary>
        public async Task CancelAsync(string scheduleId)
        {
            await _memoryService.CancelScheduleAsync(scheduleId);
        }
        
        /// <summary>
        /// Get all pending posts awaiting approval
        /// </summary>
        public async Task<List<ScheduledPost>> GetPendingApprovalsAsync()
        {
            var schedules = await _memoryService.GetScheduledPostsAsync();
            return schedules.Where(s => s.Status == ScheduleStatus.Pending && s.RequiresApproval).ToList();
        }
        
        /// <summary>
        /// Get upcoming scheduled posts
        /// </summary>
        public async Task<List<ScheduledPost>> GetUpcomingAsync(int days = 7)
        {
            var from = DateTime.Now;
            var to = DateTime.Now.AddDays(days);
            return await _memoryService.GetScheduledPostsAsync(from, to);
        }
        
        /// <summary>
        /// Reschedule a post to a new time
        /// </summary>
        public async Task<ScheduledPost> RescheduleAsync(string scheduleId, DateTime newTime)
        {
            var schedules = await _memoryService.GetScheduledPostsAsync();
            var schedule = schedules.FirstOrDefault(s => s.Id == scheduleId);
            
            if (schedule == null)
                throw new ArgumentException("Schedule not found");
            
            schedule.ScheduledTime = newTime;
            schedule.Status = schedule.RequiresApproval ? ScheduleStatus.Pending : ScheduleStatus.Approved;
            schedule.ApprovedAt = null;
            schedule.ApprovedBy = null;
            
            await _memoryService.SchedulePostAsync(schedule);
            return schedule;
        }
        
        /// <summary>
        /// Publish a post immediately (bypasses schedule)
        /// </summary>
        public async Task<string> PublishNowAsync(string scheduleId)
        {
            var schedules = await _memoryService.GetScheduledPostsAsync();
            var schedule = schedules.FirstOrDefault(s => s.Id == scheduleId);
            
            if (schedule == null)
                throw new ArgumentException("Schedule not found");
            
            return await PublishPostAsync(schedule);
        }
        
        /// <summary>
        /// Check for posts that are due and publish them
        /// </summary>
        private async Task CheckDuePostsAsync()
        {
            try
            {
                var pending = await _memoryService.GetPendingSchedulesAsync();
                var dueNow = pending.Where(s => 
                    s.Status == ScheduleStatus.Approved && 
                    s.ScheduledTime <= DateTime.Now).ToList();
                
                foreach (var schedule in dueNow)
                {
                    PostDue?.Invoke(this, schedule);
                    await PublishPostAsync(schedule);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Scheduler] Error checking due posts: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Publish a scheduled post
        /// </summary>
        private async Task<string> PublishPostAsync(ScheduledPost schedule)
        {
            try
            {
                var content = await _memoryService.GetContentAsync(schedule.ContentId);
                if (content == null)
                {
                    var error = "Content not found";
                    PostFailed?.Invoke(this, (schedule, error));
                    return error;
                }
                
                var result = await _publisher.PublishAsync(content, schedule.Platform);
                
                if (result.Success)
                {
                    await _memoryService.MarkPublishedAsync(schedule.Id, result.Message);
                    await _memoryService.UpdateContentStatusAsync(content.Id, ContentStatus.Published);
                    PostPublished?.Invoke(this, (schedule, result.Message));
                }
                else
                {
                    schedule.Status = ScheduleStatus.Failed;
                    schedule.PublishResult = result.Message;
                    await _memoryService.SchedulePostAsync(schedule);
                    PostFailed?.Invoke(this, (schedule, result.Message));
                }
                
                return result.Message;
            }
            catch (Exception ex)
            {
                var error = $"Publishing failed: {ex.Message}";
                PostFailed?.Invoke(this, (schedule, error));
                return error;
            }
        }
        
        public void Dispose()
        {
            Stop();
        }
    }
}
