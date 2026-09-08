namespace Jellyfin.Plugin.FileManager.Models;

/// <summary>
/// An allowed root directory.
/// </summary>
public sealed record RootDto(string Name, string Path);

/// <summary>
/// A file or directory entry.
/// </summary>
public sealed record FileSystemEntryDto(
    string Name,
    string Path,
    bool IsDirectory,
    long? Size,
    DateTime LastModifiedUtc,
    string Extension);

/// <summary>
/// Directory listing response.
/// </summary>
public sealed record DirectoryListingDto(
    string CurrentPath,
    string? ParentPath,
    IReadOnlyList<FileSystemEntryDto> Items);

/// <summary>
/// Create folder request.
/// </summary>
public sealed record CreateFolderRequest(string ParentPath, string Name);

/// <summary>
/// Rename request.
/// </summary>
public sealed record RenameRequest(string Path, string NewName);

/// <summary>
/// Move request.
/// </summary>
public sealed record MoveRequest(string SourcePath, string DestinationDirectory);

/// <summary>
/// Copy request.
/// </summary>
public sealed record CopyRequest(string SourcePath, string DestinationDirectory);

/// <summary>
/// Generic operation result.
/// </summary>
public sealed record OperationResult(bool Success, string Message, string? Path = null);
