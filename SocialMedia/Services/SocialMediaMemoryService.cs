using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AtlasAI.SocialMedia.Models;

namespace AtlasAI.SocialMedia.Services
{
    /// <summary>
    /// Persistent storage for social media data - brands, content, campaigns
    /// Stores data in AppData for persistence across sessions
    /// </summary>
    public class SocialMediaMemoryService
    {
        private readonly string _dataPath;
        private readonly string _brandsFile;
        private readonly string _contentFile;
        private readonly string _campaignsFile;
        private readonly string _schedulesFile;
        
        private List<BrandProfile> _brands = new();
        private List<SocialContent> _content = new();
        private List<SocialCampaign> _campaigns = new();
        private List<ScheduledPost> _schedules = new();
        
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        
        public SocialMediaMemoryService()
        {
            _dataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "SocialMedia");
            
            Directory.CreateDirectory(_dataPath);
            
            _brandsFile = Path.Combine(_dataPath, "brands.json");
            _contentFile = Path.Combine(_dataPath, "content.json");
            _campaignsFile = Path.Combine(_dataPath, "campaigns.json");
            _schedulesFile = Path.Combine(_dataPath, "schedules.json");
            
            LoadAll();
        }
        
        #region Brand Management
        
        public Task<List<BrandProfile>> GetAllBrandsAsync()
        {
            return Task.FromResult(_brands.Where(b => b.IsActive).ToList());
        }
        
        public Task<BrandProfile?> GetBrandAsync(string brandId)
        {
            return Task.FromResult(_brands.FirstOrDefault(b => b.Id == brandId));
        }
        
        public Task<BrandProfile?> GetActiveBrandAsync()
        {
            return Task.FromResult(_brands.FirstOrDefault(b => b.IsActive));
        }
        
        public async Task<BrandProfile> SaveBrandAsync(BrandProfile brand)
        {
            var existing = _brands.FirstOrDefault(b => b.Id == brand.Id);
            if (existing != null)
            {
                _brands.Remove(existing);
            }
            
            brand.UpdatedAt = DateTime.Now;
            _brands.Add(brand);
            await SaveBrandsAsync();
            
            return brand;
        }
        
        public async Task DeleteBrandAsync(string brandId)
        {
            var brand = _brands.FirstOrDefault(b => b.Id == brandId);
            if (brand != null)
            {
                brand.IsActive = false;
                await SaveBrandsAsync();
            }
        }
        
        public async Task AddToHashtagLibraryAsync(string brandId, IEnumerable<string> hashtags)
        {
            var brand = await GetBrandAsync(brandId);
            if (brand != null)
            {
                brand.HashtagLibrary = brand.HashtagLibrary
                    .Union(hashtags)
                    .Distinct()
                    .ToList();
                await SaveBrandAsync(brand);
            }
        }
        
        public async Task AddDoNotSayPhraseAsync(string brandId, string phrase)
        {
            var brand = await GetBrandAsync(brandId);
            if (brand != null && !brand.DoNotSayPhrases.Contains(phrase))
            {
                brand.DoNotSayPhrases.Add(phrase);
                await SaveBrandAsync(brand);
            }
        }
        
        #endregion
        
        #region Content Management
        
        public Task<List<SocialContent>> GetAllContentAsync(string? brandId = null)
        {
            var query = _content.AsEnumerable();
            if (!string.IsNullOrEmpty(brandId))
                query = query.Where(c => c.BrandId == brandId);
            
            return Task.FromResult(query.OrderByDescending(c => c.CreatedAt).ToList());
        }
        
        public Task<SocialContent?> GetContentAsync(string contentId)
        {
            return Task.FromResult(_content.FirstOrDefault(c => c.Id == contentId));
        }
        
        public Task<List<SocialContent>> GetContentByStatusAsync(ContentStatus status, string? brandId = null)
        {
            var query = _content.Where(c => c.Status == status);
            if (!string.IsNullOrEmpty(brandId))
                query = query.Where(c => c.BrandId == brandId);
            
            return Task.FromResult(query.OrderByDescending(c => c.CreatedAt).ToList());
        }
        
        public Task<List<SocialContent>> GetContentByCampaignAsync(string campaignId)
        {
            return Task.FromResult(_content
                .Where(c => c.CampaignId == campaignId)
                .OrderByDescending(c => c.CreatedAt)
                .ToList());
        }
        
        public async Task<SocialContent> SaveContentAsync(SocialContent content)
        {
            var existing = _content.FirstOrDefault(c => c.Id == content.Id);
            if (existing != null)
            {
                _content.Remove(existing);
            }
            
            content.UpdatedAt = DateTime.Now;
            _content.Add(content);
            await SaveContentFileAsync();
            
            return content;
        }
        
        public async Task UpdateContentStatusAsync(string contentId, ContentStatus status)
        {
            var content = await GetContentAsync(contentId);
            if (content != null)
            {
                content.Status = status;
                content.UpdatedAt = DateTime.Now;
                await SaveContentFileAsync();
            }
        }
        
        public async Task DeleteContentAsync(string contentId)
        {
            var content = _content.FirstOrDefault(c => c.Id == contentId);
            if (content != null)
            {
                content.Status = ContentStatus.Archived;
                await SaveContentFileAsync();
            }
        }
        
        #endregion
        
        #region Campaign Management
        
        public Task<List<SocialCampaign>> GetAllCampaignsAsync(string? brandId = null)
        {
            var query = _campaigns.AsEnumerable();
            if (!string.IsNullOrEmpty(brandId))
                query = query.Where(c => c.BrandId == brandId);
            
            return Task.FromResult(query.OrderByDescending(c => c.CreatedAt).ToList());
        }
        
        public Task<SocialCampaign?> GetCampaignAsync(string campaignId)
        {
            return Task.FromResult(_campaigns.FirstOrDefault(c => c.Id == campaignId));
        }
        
        public Task<List<SocialCampaign>> GetActiveCampaignsAsync(string? brandId = null)
        {
            var query = _campaigns.Where(c => c.Status == CampaignStatus.Active);
            if (!string.IsNullOrEmpty(brandId))
                query = query.Where(c => c.BrandId == brandId);
            
            return Task.FromResult(query.ToList());
        }
        
        public async Task<SocialCampaign> SaveCampaignAsync(SocialCampaign campaign)
        {
            var existing = _campaigns.FirstOrDefault(c => c.Id == campaign.Id);
            if (existing != null)
            {
                _campaigns.Remove(existing);
            }
            
            _campaigns.Add(campaign);
            await SaveCampaignsAsync();
            
            return campaign;
        }
        
        public async Task AddContentToCampaignAsync(string campaignId, string contentId)
        {
            var campaign = await GetCampaignAsync(campaignId);
            if (campaign != null && !campaign.ContentIds.Contains(contentId))
            {
                campaign.ContentIds.Add(contentId);
                
                var content = await GetContentAsync(contentId);
                if (content != null)
                {
                    content.CampaignId = campaignId;
                    await SaveContentAsync(content);
                }
                
                await SaveCampaignsAsync();
            }
        }
        
        #endregion
        
        #region Schedule Management
        
        public Task<List<ScheduledPost>> GetScheduledPostsAsync(DateTime? from = null, DateTime? to = null)
        {
            var query = _schedules.AsEnumerable();
            
            if (from.HasValue)
                query = query.Where(s => s.ScheduledTime >= from.Value);
            if (to.HasValue)
                query = query.Where(s => s.ScheduledTime <= to.Value);
            
            return Task.FromResult(query.OrderBy(s => s.ScheduledTime).ToList());
        }
        
        public Task<List<ScheduledPost>> GetPendingSchedulesAsync()
        {
            return Task.FromResult(_schedules
                .Where(s => s.Status == ScheduleStatus.Pending || s.Status == ScheduleStatus.Approved)
                .Where(s => s.ScheduledTime > DateTime.Now)
                .OrderBy(s => s.ScheduledTime)
                .ToList());
        }
        
        public async Task<ScheduledPost> SchedulePostAsync(ScheduledPost schedule)
        {
            var existing = _schedules.FirstOrDefault(s => s.Id == schedule.Id);
            if (existing != null)
            {
                _schedules.Remove(existing);
            }
            
            _schedules.Add(schedule);
            await SaveSchedulesAsync();
            
            return schedule;
        }
        
        public async Task ApproveScheduleAsync(string scheduleId, string approvedBy)
        {
            var schedule = _schedules.FirstOrDefault(s => s.Id == scheduleId);
            if (schedule != null)
            {
                schedule.Status = ScheduleStatus.Approved;
                schedule.ApprovedBy = approvedBy;
                schedule.ApprovedAt = DateTime.Now;
                await SaveSchedulesAsync();
            }
        }
        
        public async Task CancelScheduleAsync(string scheduleId)
        {
            var schedule = _schedules.FirstOrDefault(s => s.Id == scheduleId);
            if (schedule != null)
            {
                schedule.Status = ScheduleStatus.Cancelled;
                await SaveSchedulesAsync();
            }
        }
        
        public async Task MarkPublishedAsync(string scheduleId, string? result = null)
        {
            var schedule = _schedules.FirstOrDefault(s => s.Id == scheduleId);
            if (schedule != null)
            {
                schedule.Status = ScheduleStatus.Published;
                schedule.PublishedAt = DateTime.Now;
                schedule.PublishResult = result;
                await SaveSchedulesAsync();
            }
        }
        
        #endregion
        
        #region Best Performing Content
        
        public Task<List<SocialContent>> GetBestPerformingContentAsync(string brandId, int count = 10)
        {
            // For now, return most recent published content
            // Phase 2 will add analytics-based sorting
            return Task.FromResult(_content
                .Where(c => c.BrandId == brandId && c.Status == ContentStatus.Published)
                .OrderByDescending(c => c.UpdatedAt)
                .Take(count)
                .ToList());
        }
        
        #endregion
        
        #region Persistence
        
        private void LoadAll()
        {
            try
            {
                if (File.Exists(_brandsFile))
                    _brands = JsonSerializer.Deserialize<List<BrandProfile>>(File.ReadAllText(_brandsFile), JsonOptions) ?? new();
                
                if (File.Exists(_contentFile))
                    _content = JsonSerializer.Deserialize<List<SocialContent>>(File.ReadAllText(_contentFile), JsonOptions) ?? new();
                
                if (File.Exists(_campaignsFile))
                    _campaigns = JsonSerializer.Deserialize<List<SocialCampaign>>(File.ReadAllText(_campaignsFile), JsonOptions) ?? new();
                
                if (File.Exists(_schedulesFile))
                    _schedules = JsonSerializer.Deserialize<List<ScheduledPost>>(File.ReadAllText(_schedulesFile), JsonOptions) ?? new();
            }
            catch
            {
                // Start fresh if files are corrupted
            }
        }
        
        private Task SaveBrandsAsync()
        {
            var json = JsonSerializer.Serialize(_brands, JsonOptions);
            File.WriteAllText(_brandsFile, json);
            return Task.CompletedTask;
        }
        
        private Task SaveContentFileAsync()
        {
            var json = JsonSerializer.Serialize(_content, JsonOptions);
            File.WriteAllText(_contentFile, json);
            return Task.CompletedTask;
        }
        
        private Task SaveCampaignsAsync()
        {
            var json = JsonSerializer.Serialize(_campaigns, JsonOptions);
            File.WriteAllText(_campaignsFile, json);
            return Task.CompletedTask;
        }
        
        private Task SaveSchedulesAsync()
        {
            var json = JsonSerializer.Serialize(_schedules, JsonOptions);
            File.WriteAllText(_schedulesFile, json);
            return Task.CompletedTask;
        }
        
        #endregion
    }
}
