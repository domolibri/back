using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DomoLibri.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace DomoLibri.Infrastructure.Services;

[ExcludeFromCodeCoverage(Justification = "Requires Azure Blob Storage — covered by integration tests")]
public class BlobStorageService : IStorageService
{
    private const string ContainerName = "editoras-logos";

    private readonly Lazy<Task<BlobContainerClient>> _containerClient;
    private readonly string? _publicEndpoint;

    public BlobStorageService(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BlobStorage")
            ?? throw new InvalidOperationException("Connection string 'BlobStorage' not found.");

        var blobServiceClient = new BlobServiceClient(connectionString);

        // Optional public endpoint used to rewrite blob URLs for browser access.
        // Required when the internal upload endpoint (e.g. http://azurite:10000) differs
        // from the publicly accessible URL (e.g. http://localhost:10000).
        _publicEndpoint = configuration["BlobStorage:PublicEndpoint"]?.TrimEnd('/');

        // CreateIfNotExistsAsync is called exactly once: on the first upload.
        // All subsequent calls await the already-completed Task at negligible cost.
        _containerClient = new Lazy<Task<BlobContainerClient>>(async () =>
        {
            var client = blobServiceClient.GetBlobContainerClient(ContainerName);
            await client.CreateIfNotExistsAsync(PublicAccessType.Blob);
            return client;
        });
    }

    public async Task<string> UploadLogoAsync(IFormFile logoFile, Guid tenantId)
    {
        var containerClient = await _containerClient.Value;

        var extension = Path.GetExtension(logoFile.FileName);
        var blobName = $"{tenantId}/{Guid.NewGuid()}{extension}";

        var blobClient = containerClient.GetBlobClient(blobName);

        await using var stream = logoFile.OpenReadStream();
        await blobClient.UploadAsync(stream, overwrite: true);

        // When a public endpoint is configured, rewrite the URL so the browser can
        // access the blob via the publicly mapped port instead of the internal host.
        if (!string.IsNullOrWhiteSpace(_publicEndpoint))
            return $"{_publicEndpoint}/{ContainerName}/{blobName}";

        return blobClient.Uri.AbsoluteUri;
    }
}
