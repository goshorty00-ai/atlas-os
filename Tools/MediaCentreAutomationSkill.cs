using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AtlasAI.Views.ViewModels;

namespace AtlasAI.Tools;

public static class MediaCentreAutomationSkill
{
    public static async Task<string?> TryExecuteAsync(string userMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return null;

        var clean = userMessage.Trim();
        var lower = clean.ToLowerInvariant();

        // Supported:
        // - set media filter trending
        // - set filter trending
        if (!Regex.IsMatch(lower, @"^\s*(?:set\s+)?(?:media\s+)?filter\s+trending\s*$", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(lower, @"^\s*set\s+media\s+filter\s+trending\s*$", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(lower, @"^\s*set\s+filter\s+trending\s*$", RegexOptions.IgnoreCase))
            return null;

        if (Application.Current == null)
            return "⚠️ Can't set Media Centre filter: app not ready.";

        ct.ThrowIfCancellationRequested();

        MediaCentreViewModel? vm = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            vm = FindMediaCentreViewModel();
            if (vm == null) return;

            try
            {
                // Treat "Trending" as the server catalog popularity sort.
                if (vm.OpenServersViewCommand?.CanExecute(null) ?? false)
                    vm.OpenServersViewCommand.Execute(null);

                vm.SelectedServerCatalogSort = "popularity";
                vm.SelectedServerCatalogGenre = "All genres";
                vm.SelectedServerCatalogYear = "All years";
                vm.ServerCatalogSearchQuery = "";
            }
            catch
            {
            }
        });

        if (vm == null)
            return "⚠️ Media Centre view isn't loaded yet. Try: open media centre";

        return "✅ Media Centre filter set to Trending (Popularity).";
    }

    private static MediaCentreViewModel? FindMediaCentreViewModel()
    {
        try
        {
            foreach (Window w in Application.Current.Windows)
            {
                if (!w.IsLoaded) continue;

                var found = FindDataContextInVisualTree<MediaCentreViewModel>(w);
                if (found != null) return found;
            }
        }
        catch
        {
        }

        return null;
    }

    private static T? FindDataContextInVisualTree<T>(DependencyObject root) where T : class
    {
        try
        {
            if (root is FrameworkElement fe && fe.DataContext is T hit)
                return hit;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var found = FindDataContextInVisualTree<T>(child);
                if (found != null) return found;
            }
        }
        catch
        {
        }

        return null;
    }
}
