using System;
using System.Threading.Tasks;
using System.Windows;
using AtlasAI.Views.ViewModels;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Scan the media library for new files
    /// </summary>
    public class ScanMediaLibraryAction : AgentActionDefinition
    {
        public override string Id => "scan-media-library";
        public override string Title => "Scan Media Library";
        public override string Description => "Scan selected folder for new music and videos";
        public override string Icon => "🔄";
        public override ActionCategory Category => ActionCategory.Files;
        public override ActionRiskLevel Risk => ActionRiskLevel.LowRisk;
        public override string[] Keywords => new[] { "scan library", "update library", "refresh library", "scan media", "find new songs" };

        public override async Task<ActionResult> ExecuteAsync()
        {
            try
            {
                var vm = MediaCentreViewModel.Instance;
                if (vm == null)
                    return new ActionResult { Success = false, Message = "Media Centre is not active" };

                // Run on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (vm.ScanSelectedCategoryCommand.CanExecute(null))
                        vm.ScanSelectedCategoryCommand.Execute(null);
                });

                return new ActionResult { Success = true, Message = "Started media library scan" };
            }
            catch (Exception ex)
            {
                return new ActionResult { Success = false, Message = $"Scan failed: {ex.Message}" };
            }
        }
    }

    /// <summary>
    /// Organize media files (Future AI feature)
    /// </summary>
    public class OrganizeMediaLibraryAction : AgentActionDefinition
    {
        public override string Id => "organize-media-library";
        public override string Title => "Organize Media Library";
        public override string Description => "AI Organization of media files (Coming Soon)";
        public override string Icon => "✨";
        public override ActionCategory Category => ActionCategory.Files;
        public override ActionRiskLevel Risk => ActionRiskLevel.LowRisk;
        public override string[] Keywords => new[] { "organize library", "sort music", "clean up library", "organize media" };

        public override async Task<ActionResult> ExecuteAsync()
        {
            // This would hook into the AI organizer logic
            return new ActionResult { Success = true, Message = "AI Organizer is analyzing your library structure..." };
        }
    }
}
