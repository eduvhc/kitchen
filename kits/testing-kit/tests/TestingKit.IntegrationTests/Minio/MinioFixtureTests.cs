using System.Net;
using Amazon.S3.Model;
using TestingKit.MSTest;

namespace TestingKit.IntegrationTests.Minio;

[TestClass]
public class MinioFixtureTests : IntegrationTest
{
    protected override TestEnvironment Environment => TestHost.Environment;

    [TestMethod]
    public async Task Creates_the_configured_buckets_on_start()
    {
        var buckets = await TestHost.Minio.S3Client.ListBucketsAsync(CancellationToken);

        Assert.IsTrue(buckets.Buckets.Any(b => b.BucketName == TestHost.Bucket));
    }

    [TestMethod]
    public async Task Round_trips_an_object()
    {
        await TestHost.Minio.UploadAsync(TestHost.Bucket, "docs/hello.txt", "hello", CancellationToken);

        Assert.AreEqual("hello", await TestHost.Minio.DownloadAsync(TestHost.Bucket, "docs/hello.txt", CancellationToken));
        Assert.IsNull(await TestHost.Minio.DownloadAsync(TestHost.Bucket, "docs/missing.txt", CancellationToken));
        CollectionAssert.AreEqual(new[] { "docs/hello.txt" }, (await TestHost.Minio.ListObjectsAsync(TestHost.Bucket, "docs/", CancellationToken)).ToList());
    }

    [TestMethod]
    public async Task Reset_empties_the_listed_buckets()
    {
        await TestHost.Minio.UploadAsync(TestHost.Bucket, "a.txt", "a", CancellationToken);

        await TestHost.Minio.ResetAsync(CancellationToken);

        Assert.IsEmpty(await TestHost.Minio.ListObjectsAsync(TestHost.Bucket, ct: CancellationToken));
    }

    [TestMethod]
    public async Task Public_read_serves_objects_without_signing()
    {
        const string bucket = "testingkit-public";
        await TestHost.Minio.CreateBucketAsync(bucket, CancellationToken);
        await TestHost.Minio.AllowPublicReadAsync(bucket, CancellationToken);
        await TestHost.Minio.S3Client.PutObjectAsync(
            new PutObjectRequest { BucketName = bucket, Key = "logo.txt", ContentBody = "logo" },
            CancellationToken);

        using var http = new HttpClient();
        using var response = await http.GetAsync(new Uri(TestHost.Minio.Endpoint, $"{bucket}/logo.txt"), CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("logo", await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
