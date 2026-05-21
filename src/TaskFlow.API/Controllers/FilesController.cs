using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Features.Files;
using TaskFlow.Domain.Enums;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/files")]
[Authorize]
public class FilesController(IFileService files) : ControllerBase
{
    // Multipart upload. Form fields: targetType (enum), targetId, file.
    [HttpPost]
    [RequirePermission(Permissions.Tasks.Update)]
    [RequestSizeLimit(52_428_800)] // 50 MB
    public async Task<ActionResult<FileDto>> Upload(
        [FromForm] AttachmentTargetType targetType,
        [FromForm] long targetId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationAppException("error.validation");
        await using var stream = file.OpenReadStream();
        var dto = await files.UploadAsync(targetType, targetId, stream, file.FileName, file.ContentType, ct);
        return Ok(dto);
    }

    [HttpGet]
    [RequirePermission(Permissions.Tasks.View)]
    public async Task<ActionResult<IReadOnlyList<FileDto>>> List(
        [FromQuery] AttachmentTargetType targetType, [FromQuery] long targetId, CancellationToken ct)
        => Ok(await files.ListAsync(targetType, targetId, ct));

    [HttpGet("{id:long}/download")]
    [RequirePermission(Permissions.Tasks.View)]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        var f = await files.DownloadAsync(id, ct);
        return File(f.Content, f.ContentType, f.FileName);
    }

    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await files.DeleteAsync(id, ct);
        return NoContent();
    }
}
