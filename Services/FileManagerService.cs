using System.Collections.ObjectModel;
using Jellyfin.Plugin.FileManager.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FileManager.Services;

/// <summary>
/// Performs filesystem operations using exactly the filesystem namespace and
/// operating-system permissions available to the Jellyfin process.
/// </summary>
public sealed class FileManagerService
{
    private readonly ILogger<FileManagerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileManagerService"/> class.
    /// </summary>
    public FileManagerService(ILogger<FileManagerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets filesystem roots visible to Jellyfin.
    /// In unrestricted mode Windows exposes its ready drives; Unix exposes "/".
    /// In restricted mode the configured roots are returned instead.
    /// </summary>
    public IReadOnlyList<RootDto> GetRoots()
    {
        List<RootDto> roots;

        if (IsFullAccess())
        {
            if (OperatingSystem.IsWindows())
            {
                roots = DriveInfo.GetDrives()
                    .Where(d =>
                    {
                        try { return d.IsReady; }
                        catch { return false; }
                    })
                    .Select(d => new RootDto(
                        string.IsNullOrWhiteSpace(d.VolumeLabel)
                            ? d.Name
                            : $"{d.Name} ({d.VolumeLabel})",
                        Path.GetFullPath(d.RootDirectory.FullName)))
                    .ToList();
            }
            else
            {
                roots = new List<RootDto>
                {
                    new("/", "/")
                };
            }
        }
        else
        {
            roots = GetNormalizedRestrictedRoots()
                .Where(Directory.Exists)
                .Select(path => new RootDto(
                    Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                        is { Length: > 0 } name ? name : path,
                    path))
                .ToList();
        }

        return new ReadOnlyCollection<RootDto>(roots);
    }

    /// <summary>
    /// Lists a directory.
    /// </summary>
    public DirectoryListingDto List(string path)
    {
        var safePath = ResolveAccessiblePath(path, mustExist: true, requireDirectory: true);

        var entries = new List<FileSystemEntryDto>();
        var directory = new DirectoryInfo(safePath);

        foreach (var child in directory.EnumerateFileSystemInfos())
        {
            try
            {
                if (child is DirectoryInfo childDirectory)
                {
                    entries.Add(new FileSystemEntryDto(
                        childDirectory.Name,
                        childDirectory.FullName,
                        true,
                        null,
                        childDirectory.LastWriteTimeUtc,
                        string.Empty));
                }
                else if (child is FileInfo file)
                {
                    entries.Add(new FileSystemEntryDto(
                        file.Name,
                        file.FullName,
                        false,
                        file.Length,
                        file.LastWriteTimeUtc,
                        file.Extension));
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogDebug(ex, "Cannot inspect {Path}", child.FullName);
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Cannot inspect {Path}", child.FullName);
            }
        }

        var ordered = entries
            .OrderByDescending(i => i.IsDirectory)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new DirectoryListingDto(
            safePath,
            GetAccessibleParent(safePath),
            ordered);
    }

    /// <summary>
    /// Creates a directory.
    /// </summary>
    public string CreateFolder(string parentPath, string name)
    {
        ValidateLeafName(name);
        var parent = ResolveAccessiblePath(parentPath, mustExist: true, requireDirectory: true);
        var target = ResolveAccessiblePath(Path.Combine(parent, name), mustExist: false);

        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new IOException("Ya existe un archivo o carpeta con ese nombre.");
        }

        Directory.CreateDirectory(target);
        _logger.LogInformation("Created directory {Path}", target);
        return target;
    }

    /// <summary>
    /// Renames a file or directory in its current parent directory.
    /// </summary>
    public string Rename(string path, string newName)
    {
        ValidateLeafName(newName);
        var source = ResolveAccessiblePath(path, mustExist: true);

        if (IsFileSystemRoot(source))
        {
            throw new InvalidOperationException("No se puede renombrar una raíz del sistema.");
        }

        var parent = Path.GetDirectoryName(
            source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            ?? throw new InvalidOperationException("No se pudo determinar la carpeta padre.");

        var destination = ResolveAccessiblePath(Path.Combine(parent, newName), mustExist: false);

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException("Ya existe un elemento con el nuevo nombre.");
        }

        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }

        _logger.LogInformation("Renamed {Source} to {Destination}", source, destination);
        return destination;
    }

    /// <summary>
    /// Moves a file or directory. If the native move cannot cross a filesystem/volume,
    /// it transparently falls back to copy + delete.
    /// </summary>
    public string Move(string sourcePath, string destinationDirectory)
    {
        var source = ResolveAccessiblePath(sourcePath, mustExist: true);
        var destinationDir = ResolveAccessiblePath(
            destinationDirectory,
            mustExist: true,
            requireDirectory: true);

        if (IsFileSystemRoot(source))
        {
            throw new InvalidOperationException("No se puede mover una raíz del sistema.");
        }

        var name = Path.GetFileName(
            source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var destination = ResolveAccessiblePath(
            Path.Combine(destinationDir, name),
            mustExist: false);

        if (PathsEqual(source, destination))
        {
            throw new IOException("El origen y el destino son iguales.");
        }

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException("Ya existe un elemento con ese nombre en el destino.");
        }

        if (Directory.Exists(source))
        {
            MoveDirectoryWithCrossVolumeFallback(source, destination);
        }
        else
        {
            MoveFileWithCrossVolumeFallback(source, destination);
        }

        _logger.LogInformation("Moved {Source} to {Destination}", source, destination);
        return destination;
    }

    /// <summary>
    /// Copies a file or directory recursively.
    /// </summary>
    public string Copy(string sourcePath, string destinationDirectory)
    {
        if (!(Plugin.Instance?.Configuration.AllowCopy ?? false))
        {
            throw new InvalidOperationException("La copia de archivos está deshabilitada.");
        }

        var source = ResolveAccessiblePath(sourcePath, mustExist: true);
        var destinationDir = ResolveAccessiblePath(
            destinationDirectory,
            mustExist: true,
            requireDirectory: true);

        var name = Path.GetFileName(
            source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var destination = ResolveAccessiblePath(
            Path.Combine(destinationDir, name),
            mustExist: false);

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException("Ya existe un elemento con ese nombre en el destino.");
        }

        if (Directory.Exists(source))
        {
            CopyDirectory(source, destination);
        }
        else
        {
            File.Copy(source, destination, overwrite: false);
        }

        _logger.LogInformation("Copied {Source} to {Destination}", source, destination);
        return destination;
    }

    /// <summary>
    /// Deletes a file or directory.
    /// </summary>
    public void Delete(string path, bool recursive)
    {
        if (!(Plugin.Instance?.Configuration.AllowDelete ?? false))
        {
            throw new InvalidOperationException("La eliminación está deshabilitada.");
        }

        var safePath = ResolveAccessiblePath(path, mustExist: true);

        if (IsFileSystemRoot(safePath))
        {
            throw new InvalidOperationException("No se puede eliminar una raíz del sistema.");
        }

        if (!IsFullAccess() && IsConfiguredRestrictedRoot(safePath))
        {
            throw new InvalidOperationException("No se puede eliminar una raíz configurada.");
        }

        if (Directory.Exists(safePath))
        {
            Directory.Delete(safePath, recursive);
        }
        else
        {
            File.Delete(safePath);
        }

        _logger.LogWarning("Deleted {Path}", safePath);
    }

    /// <summary>
    /// Saves uploaded files into an accessible directory.
    /// </summary>
    public async Task<IReadOnlyList<string>> UploadAsync(
        string destinationDirectory,
        IReadOnlyList<IFormFile> files,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        if (!(Plugin.Instance?.Configuration.AllowUpload ?? false))
        {
            throw new InvalidOperationException("La subida de archivos está deshabilitada.");
        }

        var destination = ResolveAccessiblePath(
            destinationDirectory,
            mustExist: true,
            requireDirectory: true);

        var maxMb = Plugin.Instance?.Configuration.MaxUploadMegabytes ?? 0;
        var maxBytes = maxMb <= 0 ? long.MaxValue : checked(maxMb * 1024L * 1024L);
        var saved = new List<string>();

        foreach (var upload in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var safeName = Path.GetFileName(upload.FileName);
            ValidateLeafName(safeName);

            if (upload.Length > maxBytes)
            {
                throw new InvalidOperationException(
                    $"El archivo '{safeName}' supera el límite de {maxMb} MB.");
            }

            var target = ResolveAccessiblePath(
                Path.Combine(destination, safeName),
                mustExist: false);

            if ((File.Exists(target) || Directory.Exists(target)) && !overwrite)
            {
                throw new IOException($"Ya existe '{safeName}'.");
            }

            await using var stream = new FileStream(
                target,
                overwrite ? FileMode.Create : FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                useAsync: true);

            await upload.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            saved.Add(target);
            _logger.LogInformation("Uploaded {Path} ({Length} bytes)", target, upload.Length);
        }

        return saved;
    }

    /// <summary>
    /// Resolves a path and verifies it is accessible under the configured mode.
    /// Full mode intentionally imposes no plugin-level root boundary; operating-system
    /// permissions and the process/container namespace are the authority.
    /// </summary>
    public string ResolveAccessiblePath(
        string path,
        bool mustExist,
        bool requireDirectory = false,
        bool requireFile = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("La ruta está vacía.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);

        if (!IsFullAccess())
        {
            var roots = GetNormalizedRestrictedRoots();
            if (roots.Count == 0)
            {
                throw new InvalidOperationException(
                    "No hay rutas permitidas configuradas.");
            }

            var root = roots.FirstOrDefault(r => IsInsideRoot(fullPath, r));
            if (root is null)
            {
                throw new UnauthorizedAccessException(
                    "La ruta está fuera de las raíces permitidas.");
            }

            EnsureNoLinks(root, fullPath);
        }

        if (mustExist && !File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("La ruta no existe.", fullPath);
        }

        if (requireDirectory && !Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException("La ruta no es una carpeta existente.");
        }

        if (requireFile && !File.Exists(fullPath))
        {
            throw new FileNotFoundException("La ruta no es un archivo existente.", fullPath);
        }

        return fullPath;
    }

    private void MoveFileWithCrossVolumeFallback(string source, string destination)
    {
        try
        {
            File.Move(source, destination);
            return;
        }
        catch (IOException ex)
        {
            _logger.LogInformation(
                ex,
                "Native file move failed; falling back to copy/delete for {Source}",
                source);
        }

        var sourceInfo = new FileInfo(source);
        if (!string.IsNullOrEmpty(sourceInfo.LinkTarget))
        {
            throw new IOException(
                "No se puede hacer fallback copy/delete de un enlace simbólico entre volúmenes.");
        }

        File.Copy(source, destination, overwrite: false);

        var destinationInfo = new FileInfo(destination);

        if (!destinationInfo.Exists || destinationInfo.Length != sourceInfo.Length)
        {
            TryDeleteFailedDestination(destination);
            throw new IOException(
                "La copia de verificación falló; el archivo original no fue eliminado.");
        }

        File.Delete(source);
    }

    private void MoveDirectoryWithCrossVolumeFallback(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
            return;
        }
        catch (IOException ex)
        {
            _logger.LogInformation(
                ex,
                "Native directory move failed; falling back to recursive copy/delete for {Source}",
                source);
        }

        try
        {
            CopyDirectory(source, destination);
            Directory.Delete(source, recursive: true);
        }
        catch
        {
            // Never delete the source if the copy did not finish.
            // Keep the partial destination so the admin can inspect/recover it.
            throw;
        }
    }

    private static void TryDeleteFailedDestination(string destination)
    {
        try
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }
        catch
        {
            // Preserve the original exception; cleanup is best effort only.
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var info = new FileInfo(file);
            if (!string.IsNullOrEmpty(info.LinkTarget))
            {
                throw new IOException(
                    $"La carpeta contiene un enlace simbólico y no puede copiarse de forma segura entre volúmenes: {info.FullName}");
            }

            var target = Path.Combine(destination, info.Name);
            File.Copy(file, target, overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var info = new DirectoryInfo(directory);
            if (!string.IsNullOrEmpty(info.LinkTarget))
            {
                throw new IOException(
                    $"La carpeta contiene un enlace simbólico y no puede copiarse de forma segura entre volúmenes: {info.FullName}");
            }

            CopyDirectory(directory, Path.Combine(destination, info.Name));
        }
    }

    private static void ValidateLeafName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre está vacío.", nameof(name));
        }

        if (name is "." or "..")
        {
            throw new ArgumentException("Nombre no válido.", nameof(name));
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains(Path.DirectorySeparatorChar)
            || name.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException(
                "El nombre contiene caracteres no permitidos.",
                nameof(name));
        }
    }

    private static bool IsFullAccess()
        => Plugin.Instance?.Configuration.FullFileSystemAccess ?? true;

    private static IReadOnlyList<string> GetNormalizedRestrictedRoots()
    {
        var raw = Plugin.Instance?.Configuration.AllowedRoots ?? string.Empty;

        return raw
            .Split(
                new[] { '\r', '\n', ';' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFullPath)
            .Distinct(GetPathComparer())
            .ToList();
    }

    private static string? GetAccessibleParent(string path)
    {
        if (IsFileSystemRoot(path))
        {
            return null;
        }

        var parent = Directory.GetParent(path)?.FullName;
        if (parent is null)
        {
            return null;
        }

        if (IsFullAccess())
        {
            return parent;
        }

        return GetNormalizedRestrictedRoots().Any(root => IsInsideRoot(parent, root))
            ? parent
            : null;
    }

    private static bool IsConfiguredRestrictedRoot(string path)
        => GetNormalizedRestrictedRoots().Any(root => PathsEqual(path, root));

    private static bool IsFileSystemRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return root is not null && PathsEqual(full, root);
    }

    private static bool IsInsideRoot(string path, string root)
    {
        if (PathsEqual(path, root))
        {
            return true;
        }

        var rootWithSeparator =
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return path.StartsWith(rootWithSeparator, GetPathComparison());
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(
            a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            GetPathComparison());

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>
    /// Restricted-mode symlink/junction protection. Full filesystem mode deliberately
    /// does not impose this restriction because the entire namespace is allowed.
    /// </summary>
    private static void EnsureNoLinks(string root, string target)
    {
        var relative = Path.GetRelativePath(root, target);
        if (relative == ".")
        {
            return;
        }

        var current = root;

        foreach (var part in relative.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);

            if (Directory.Exists(current))
            {
                var info = new DirectoryInfo(current);
                if (!string.IsNullOrEmpty(info.LinkTarget))
                {
                    throw new UnauthorizedAccessException(
                        "No se permite navegar mediante enlaces simbólicos en modo restringido.");
                }
            }
            else if (File.Exists(current))
            {
                var info = new FileInfo(current);
                if (!string.IsNullOrEmpty(info.LinkTarget))
                {
                    throw new UnauthorizedAccessException(
                        "No se permite acceder mediante enlaces simbólicos en modo restringido.");
                }
            }
            else
            {
                break;
            }
        }
    }
}
