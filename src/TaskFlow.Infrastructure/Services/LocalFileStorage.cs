using Microsoft.Extensions.Configuration;
using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Infrastructure.Services;

// Stores files on local disk under {FileStorage:Root}/{tenantId}/{guid}{ext}.
public class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IConfiguration config)
    {
        _root = config["FileStorage:Root"]
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "files");
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredFile> SaveAsync(Stream content, string fileName, long tenantId, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName);
        var tenantDir = Path.Combine(_root, tenantId.ToString());
        Directory.CreateDirectory(tenantDir);

        var key = Path.Combine(tenantId.ToString(), $"{Guid.NewGuid():N}{ext}");
        var fullPath = Path.Combine(_root, key);

        await using (var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write))
            await content.CopyToAsync(fs, ct);

        var size = new FileInfo(fullPath).Length;
        return new StoredFile(key.Replace('\\', '/'), size);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(storageKey);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("File not found.", storageKey);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(storageKey);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    // Guards against path traversal: the resolved path must stay under the root.
    private string ResolvePath(string storageKey)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, storageKey));
        var rootFull = Path.GetFullPath(_root);
        if (!fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Invalid storage key.");
        return fullPath;
    }
}
