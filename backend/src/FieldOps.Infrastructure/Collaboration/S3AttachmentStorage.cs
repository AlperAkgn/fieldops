using System.Diagnostics;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using FieldOps.Application.Collaboration;
using FieldOps.Application.Common;
using Microsoft.Extensions.Configuration;

namespace FieldOps.Infrastructure.Collaboration;

internal sealed class S3AttachmentStorage(IConfiguration configuration) : IAttachmentStorage, IDisposable
{
    private readonly string bucket = configuration["ObjectStorage:Bucket"] ?? "fieldops-private";
    private readonly Lazy<IAmazonS3> client = new(() => CreateClient(configuration));
    private readonly SemaphoreSlim bucketInitialization = new(1);
    private bool bucketReady;

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var operation = FieldOpsTelemetry.Activities.StartActivity("storage.put", ActivityKind.Client);
        using var deadline = Bound(cancellationToken);
        try
        {
            await EnsureLocalBucketAsync(deadline.Token);
            await client.Value.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false,
                DisableDefaultChecksumValidation = false,
                UseChunkEncoding = false,
                ChecksumAlgorithm = ChecksumAlgorithm.SHA256
            }, deadline.Token);
            FieldOpsTelemetry.Record("storage.put", "ok");
        }
        catch (Exception error) when (IsProviderFailure(error, cancellationToken))
        {
            FieldOpsTelemetry.Record("storage.put", "failed");
            operation?.SetStatus(ActivityStatusCode.Error);
            throw Unavailable();
        }
    }

    private async Task EnsureLocalBucketAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("ObjectStorage:CreateLocalBucket")) return;
        await bucketInitialization.WaitAsync(cancellationToken);
        try
        {
            if (bucketReady) return;
            try { await client.Value.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken); }
            catch (AmazonS3Exception error) when (error.ErrorCode == "BucketAlreadyOwnedByYou") { }
            bucketReady = true;
        }
        finally { bucketInitialization.Release(); }
    }

    public async Task<(Stream Stream, IDisposable Owner)> GetAsync(string key, CancellationToken cancellationToken)
    {
        using var operation = FieldOpsTelemetry.Activities.StartActivity("storage.get", ActivityKind.Client);
        using var deadline = Bound(cancellationToken);
        try
        {
            var response = await client.Value.GetObjectAsync(bucket, key, deadline.Token);
            FieldOpsTelemetry.Record("storage.get", "ok");
            return (response.ResponseStream, response);
        }
        catch (Exception error) when (IsProviderFailure(error, cancellationToken))
        {
            FieldOpsTelemetry.Record("storage.get", "failed");
            operation?.SetStatus(ActivityStatusCode.Error);
            throw Unavailable();
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        using var operation = FieldOpsTelemetry.Activities.StartActivity("storage.cleanup", ActivityKind.Client);
        using var deadline = Bound(cancellationToken);
        try
        {
            await client.Value.DeleteObjectAsync(bucket, key, deadline.Token);
            FieldOpsTelemetry.Record("storage.cleanup", "ok");
        }
        catch (Exception error) when (IsProviderFailure(error, cancellationToken))
        {
            FieldOpsTelemetry.Record("storage.cleanup", "failed");
            operation?.SetStatus(ActivityStatusCode.Error);
            throw Unavailable();
        }
    }

    private static CancellationTokenSource Bound(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        return deadline;
    }

    private static bool IsProviderFailure(Exception error, CancellationToken cancellationToken) =>
        error is AmazonServiceException or HttpRequestException || error is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static IAmazonS3 CreateClient(IConfiguration config)
    {
        var endpoint = config["ObjectStorage:Endpoint"];
        var accessKey = config["ObjectStorage:AccessKey"];
        var secretKey = config["ObjectStorage:SecretKey"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
            throw Unavailable();
        return new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = config["ObjectStorage:Region"] ?? "us-east-1",
            Timeout = TimeSpan.FromSeconds(8),
            MaxErrorRetry = 0
        });
    }

    private static FieldOpsException Unavailable() => new(ErrorKind.Unavailable, "object_storage_unavailable", "File storage is temporarily unavailable.");

    public void Dispose()
    {
        if (client.IsValueCreated) client.Value.Dispose();
        bucketInitialization.Dispose();
    }
}
