namespace TaskFlow.Application.Common.Interfaces;

public record StoredFile(string StorageKey, long SizeBytes);

// Storage backend abstraction. Local disk for dev; swap for Azure Blob / S3 in prod.
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, string fileName, long tenantId, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default);
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
