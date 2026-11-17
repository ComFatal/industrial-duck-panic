using IndustrialDuckPanic.Core;
using Velopack;
using Velopack.Sources;

namespace IndustrialDuckPanic.Services;

/// <summary>Velopack updates from the GitHub releases of the project.</summary>
public static class UpdateService
{
    public static string CurrentVersion
    {
        get
        {
            try
            {
                var mgr = CreateManager();
                if (mgr?.IsInstalled == true && mgr.CurrentVersion is { } v) return v.ToString();
            }
            catch { /* not installed */ }
            return typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "?";
        }
    }

    private static UpdateManager? CreateManager() =>
        string.IsNullOrWhiteSpace(AppConstants.UpdateRepositoryUrl)
            ? null
            : new UpdateManager(new GithubSource(AppConstants.UpdateRepositoryUrl, null, false));

    /// <summary>Checks for a new version, downloads it and restarts. Returns a status text.</summary>
    public static async Task<string> CheckAndApplyAsync(Action<string> progress)
    {
        var mgr = CreateManager();
        if (mgr is null) return $"Version {CurrentVersion}. Automatic updates are not set up in this build yet.";
        if (!mgr.IsInstalled) return $"Version {CurrentVersion}. Updates work only in the installed app (Setup.exe).";

        try
        {
            progress("Checking for updates…");
            var update = await mgr.CheckForUpdatesAsync();
            if (update is null) return $"Version {CurrentVersion} is the latest.";

            progress($"Downloading {update.TargetFullRelease.Version}…");
            await mgr.DownloadUpdatesAsync(update, p => progress($"Downloading {update.TargetFullRelease.Version}… {p}%"));
            mgr.ApplyUpdatesAndRestart(update);
            return "Restarting…";
        }
        catch (Exception ex)
        {
            return $"Update failed: {ex.Message}";
        }
    }
}
