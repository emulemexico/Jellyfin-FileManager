using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.FileManager.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the plugin can access the complete
    /// filesystem visible to the Jellyfin process.
    /// </summary>
    public bool FullFileSystemAccess { get; set; } = true;

    /// <summary>
    /// Gets or sets the list of allowed root directories.
    /// One path per line. Semicolons are also accepted.
    /// </summary>
    public string AllowedRoots { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether uploads are enabled.
    /// </summary>
    public bool AllowUpload { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether deletion is enabled.
    /// </summary>
    public bool AllowDelete { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether copy operations are enabled.
    /// </summary>
    public bool AllowCopy { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum upload size per file in MB.
    /// Zero means no plugin-level limit.
    /// </summary>
    public long MaxUploadMegabytes { get; set; } = 2048;
}
