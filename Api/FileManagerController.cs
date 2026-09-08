using System.Net.Mime;
using Jellyfin.Plugin.FileManager.Models;
using Jellyfin.Plugin.FileManager.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.FileManager.Api;

/// <summary>
/// Admin-only filesystem API.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("FileManager")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class FileManagerController : ControllerBase
{
    private readonly FileManagerService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileManagerController"/> class.
    /// </summary>
    public FileManagerController(FileManagerService service)
    {
        _service = service;
    }

    /// <summary>
    /// Returns configured roots.
    /// </summary>
    [HttpGet("Roots")]
    public ActionResult<IReadOnlyList<RootDto>> GetRoots()
        => Ok(_service.GetRoots());

    /// <summary>
    /// Lists a directory.
    /// </summary>
    [HttpGet("List")]
    public ActionResult<DirectoryListingDto> List([FromQuery] string path)
        => Execute(() => _service.List(path));

    /// <summary>
    /// Creates a folder.
    /// </summary>
    [HttpPost("Folder")]
    public ActionResult<OperationResult> CreateFolder([FromBody] CreateFolderRequest request)
        => Execute(() =>
        {
            var path = _service.CreateFolder(request.ParentPath, request.Name);
            return new OperationResult(true, "Carpeta creada.", path);
        });

    /// <summary>
    /// Renames a file or directory.
    /// </summary>
    [HttpPost("Rename")]
    public ActionResult<OperationResult> Rename([FromBody] RenameRequest request)
        => Execute(() =>
        {
            var path = _service.Rename(request.Path, request.NewName);
            return new OperationResult(true, "Elemento renombrado.", path);
        });

    /// <summary>
    /// Moves a file or directory.
    /// </summary>
    [HttpPost("Move")]
    public ActionResult<OperationResult> Move([FromBody] MoveRequest request)
        => Execute(() =>
        {
            var path = _service.Move(request.SourcePath, request.DestinationDirectory);
            return new OperationResult(true, "Elemento movido.", path);
        });

    /// <summary>
    /// Copies a file or directory.
    /// </summary>
    [HttpPost("Copy")]
    public ActionResult<OperationResult> Copy([FromBody] CopyRequest request)
        => Execute(() =>
        {
            var path = _service.Copy(request.SourcePath, request.DestinationDirectory);
            return new OperationResult(true, "Elemento copiado.", path);
        });

    /// <summary>
    /// Deletes a file or directory.
    /// </summary>
    [HttpDelete("Delete")]
    public ActionResult<OperationResult> Delete([FromQuery] string path, [FromQuery] bool recursive = false)
        => Execute(() =>
        {
            _service.Delete(path, recursive);
            return new OperationResult(true, "Elemento eliminado.");
        });

    /// <summary>
    /// Uploads one or more files.
    /// </summary>
    [HttpPost("Upload")]
    [RequestSizeLimit(long.MaxValue)]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<ActionResult<OperationResult>> Upload(
        [FromQuery] string destinationDirectory,
        [FromQuery] bool overwrite,
        [FromForm] List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        try
        {
            if (files.Count == 0)
            {
                return BadRequest(new OperationResult(false, "No se recibieron archivos."));
            }

            var saved = await _service.UploadAsync(
                destinationDirectory,
                files,
                overwrite,
                cancellationToken).ConfigureAwait(false);

            return Ok(new OperationResult(
                true,
                $"{saved.Count} archivo(s) subido(s).",
                destinationDirectory));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new OperationResult(false, ex.Message));
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidOperationException
                                   or IOException)
        {
            return BadRequest(new OperationResult(false, ex.Message));
        }
    }

    /// <summary>
    /// Downloads a file.
    /// </summary>
    [HttpGet("Download")]
    [Produces("application/octet-stream")]
    public IActionResult Download([FromQuery] string path)
    {
        try
        {
            var safePath = _service.ResolveAccessiblePath(path, mustExist: true, requireFile: true);
            return PhysicalFile(
                safePath,
                MediaTypeNames.Application.Octet,
                Path.GetFileName(safePath),
                enableRangeProcessing: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new OperationResult(false, ex.Message));
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidOperationException
                                   or IOException)
        {
            return BadRequest(new OperationResult(false, ex.Message));
        }
    }

    /// <summary>
    /// Downloads a directory as a zip archive.
    /// </summary>
    [HttpGet("DownloadDirectory")]
    [Produces("application/zip")]
    public IActionResult DownloadDirectory([FromQuery] string path)
    {
        try
        {
            var safePath = _service.ResolveAccessiblePath(path, mustExist: true, requireDirectory: true);
            var dirName = Path.GetFileName(safePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(dirName))
            {
                dirName = "archive";
            }

            var tempZip = Path.Combine(Path.GetTempPath(), $"jellyfin_fm_{Guid.NewGuid():N}.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(safePath, tempZip);

            var stream = new FileStream(tempZip, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
            return File(stream, "application/zip", $"{dirName}.zip");
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new OperationResult(false, ex.Message));
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidOperationException
                                   or IOException)
        {
            return BadRequest(new OperationResult(false, ex.Message));
        }
    }

    private ActionResult<T> Execute<T>(Func<T> action)
    {
        try
        {
            return Ok(action());
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, default(T));
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidOperationException
                                   or IOException)
        {
            ModelState.AddModelError("FileManager", ex.Message);
            return ValidationProblem(ModelState);
        }
    }
}
