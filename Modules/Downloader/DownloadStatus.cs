namespace AtlasAI.Modules.Downloader
{
    public enum DownloadStatus
    {
        Queued = 0,
        Resolving = 1,
        Downloading = 2,
        Converting = 3,
        Paused = 4,
        Completed = 5,
        Error = 6,
        Cancelled = 7
    }
}
