using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Amazon.S3.Transfer;
using DomoLibri.Application.Interfaces;
using DomoLibri.Domain.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DomoLibri.Infrastructure.Services;

[ExcludeFromCodeCoverage(Justification = "Requires MinIO/S3 — covered by integration tests")]
public class S3StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly AwsSettings _settings;
    // Guaranteed to run once per service lifetime, regardless of concurrent callers.
    // If bucket creation fails the faulted Task is cached, surfacing the error on every
    // subsequent upload — which is the desired fail-fast behaviour.
    private readonly Lazy<Task> _bucketInitializer;

    public S3StorageService(IOptions<AwsSettings> awsOptions)
    {
        _settings = awsOptions.Value;

        _s3Client = new AmazonS3Client(
            _settings.AccessKey,
            _settings.SecretKey,
            new AmazonS3Config
            {
                ServiceURL = _settings.ServiceURL,
                ForcePathStyle = true
            }
        );

        _bucketInitializer = new Lazy<Task>(
            () => EnsureBucketExistsAsync(_settings.BucketName),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<string> UploadLogoAsync(IFormFile logoFile, Guid tenantId)
    {
        await _bucketInitializer.Value;

        var extension = Path.GetExtension(logoFile.FileName);
        var objectKey = $"{tenantId}/{Guid.NewGuid()}{extension}";

        await using var stream = logoFile.OpenReadStream();
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _settings.BucketName,
            Key = objectKey,
            InputStream = stream,
            ContentType = logoFile.ContentType
        });

        var url = $"{_settings.ServiceURL.TrimEnd('/')}/{_settings.BucketName}/{objectKey}";

        return url.Replace("http://minio:", "http://localhost:");
    }

    private async Task EnsureBucketExistsAsync(string bucketName)
    {
        var exists = await AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, bucketName);
        if (exists) return;

        await _s3Client.PutBucketAsync(new PutBucketRequest
        {
            BucketName = bucketName,
            UseClientRegion = true
        });

        var policy = JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Effect = "Allow",
                    Principal = "*",
                    Action = new[] { "s3:GetObject" },
                    Resource = new[] { $"arn:aws:s3:::{bucketName}/*" }
                }
            }
        });

        await _s3Client.PutBucketPolicyAsync(new PutBucketPolicyRequest
        {
            BucketName = bucketName,
            Policy = policy
        });
    }
}
