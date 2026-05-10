using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.Modules.Downloader;

namespace AtlasAI.Services
{
    public sealed class DownloadService
    {
        public static DownloadService Instance { get; } = new DownloadService();

        public event EventHandler<DownloadJobErrorEventArgs>? JobErrored;

        private DownloadService()
        {
        }

        public async Task ResumeQueuedDownloads()
        {
            await DownloadManager.Instance.InitializeAsync();
        }

        public IReadOnlyList<DownloadJob> DownloadJobs
        {
            get
            {
                try
                {
                    var jobs = DownloadManager.Instance.GetJobsSnapshot();
                    return jobs.Select(j => new DownloadJob
                    {
                        Id = j.Id,
                        Url = j.Url,
                        DisplayName = string.IsNullOrWhiteSpace(j.Filename) ? j.Url : j.Filename!,
                        Status = j.Status.ToString()
                    }).ToList();
                }
                catch
                {
                    return Array.Empty<DownloadJob>();
                }
            }
        }

        public void AddDownload(string input)
        {
            _ = AddDownloadAsync(input);
        }

        public async Task AddDownloadAsync(string input)
        {
            await DownloadManager.Instance.InitializeAsync();

            var raw = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return;

            if (File.Exists(raw) && raw.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                await DownloadManager.Instance.ImportCsvFromPathAsync(raw);
                return;
            }

            await DownloadManager.Instance.AddUrlAsync(raw, "Auto");
        }

        public async Task AddDownloadsAsync(IEnumerable<string> urls)
        {
            await DownloadManager.Instance.InitializeAsync();
            await DownloadManager.Instance.AddUrlsAsync((urls ?? Array.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)), "Auto");
        }

        public void RetryWithSmallerChunks(DownloadJob job)
        {
            if (job == null) return;
            if (string.IsNullOrWhiteSpace(job.Url)) return;
            _ = DownloadManager.Instance.AddUrlAsync(job.Url, "Auto");
        }

        public class DownloadJob
        {
            public string Id { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string Url { get; set; } = "";
            public string Status { get; set; } = "";
        }

        public class DownloadJobErrorEventArgs : EventArgs
        {
            public DownloadJob? Job { get; set; }
            public bool LooksLikeTimeout { get; set; }
        }
    }
}
