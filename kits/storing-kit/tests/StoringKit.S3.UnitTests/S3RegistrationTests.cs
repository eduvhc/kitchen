using Amazon;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace StoringKit.S3.UnitTests;

[TestClass]
public sealed class S3RegistrationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void An_r2_endpoint_only_sends_checksums_when_required()
    {
        var config = S3ObjectStore.CreateConfig(new S3StoreOptions
        {
            BucketName = "files",
            ServiceUrl = new Uri("https://0123456789abcdef.r2.cloudflarestorage.com"),
            Region = "auto",
        });

        Assert.AreEqual(RequestChecksumCalculation.WHEN_REQUIRED, config.RequestChecksumCalculation);
        Assert.AreEqual(ResponseChecksumValidation.WHEN_REQUIRED, config.ResponseChecksumValidation);
        Assert.AreEqual("https://0123456789abcdef.r2.cloudflarestorage.com/", config.ServiceURL);
        Assert.AreEqual("auto", config.AuthenticationRegion);
        Assert.IsFalse(config.ForcePathStyle);
    }

    [TestMethod]
    public void A_minio_endpoint_uses_path_style_and_signs_for_us_east_1_by_default()
    {
        var config = S3ObjectStore.CreateConfig(new S3StoreOptions
        {
            BucketName = "files",
            ServiceUrl = new Uri("http://localhost:9000"),
            ForcePathStyle = true,
        });

        Assert.AreEqual(RequestChecksumCalculation.WHEN_REQUIRED, config.RequestChecksumCalculation);
        Assert.IsTrue(config.ForcePathStyle);
        Assert.AreEqual("us-east-1", config.AuthenticationRegion);
    }

    [TestMethod]
    public void Aws_keeps_the_sdk_checksum_defaults()
    {
        var byRegion = S3ObjectStore.CreateConfig(new S3StoreOptions { BucketName = "files", Region = "eu-west-1" });
        var byEndpoint = S3ObjectStore.CreateConfig(new S3StoreOptions
        {
            BucketName = "files",
            ServiceUrl = new Uri("https://s3.eu-west-1.amazonaws.com"),
            Region = "eu-west-1",
        });
        var defaults = new Amazon.S3.AmazonS3Config();

        Assert.AreEqual(RegionEndpoint.EUWest1, byRegion.RegionEndpoint);
        Assert.AreEqual(defaults.RequestChecksumCalculation, byRegion.RequestChecksumCalculation);
        Assert.AreEqual(defaults.ResponseChecksumValidation, byRegion.ResponseChecksumValidation);
        Assert.AreEqual(defaults.RequestChecksumCalculation, byEndpoint.RequestChecksumCalculation);
    }

    [TestMethod]
    public async Task Binds_each_named_store_from_its_own_section_and_presigns_offline()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storing:Default:BucketName"] = "documents",
                ["Storing:Default:ServiceUrl"] = "https://0123456789abcdef.r2.cloudflarestorage.com",
                ["Storing:Default:Region"] = "auto",
                ["Storing:Default:AccessKeyId"] = "AKIDEXAMPLE",
                ["Storing:Default:SecretAccessKey"] = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY",
                ["Storing:Public:BucketName"] = "avatars",
                ["Storing:Public:Region"] = "eu-west-1",
                ["Storing:Public:AccessKeyId"] = "AKIDEXAMPLE",
                ["Storing:Public:SecretAccessKey"] = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY",
                ["Storing:Public:PublicBaseUrl"] = "https://avatars.example.com",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddS3Store(configuration);
        services.AddS3Store(configuration, "Public");
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();

        var url = await provider.GetRequiredService<IObjectStore>()
            .GetReadUrlAsync("reports/q3 final.pdf", TimeSpan.FromMinutes(15), TestContext.CancellationToken);

        Assert.IsTrue(url.IsSuccess, url.ToString());
        Assert.AreEqual("https", url.Value.Scheme);
        StringAssert.Contains(url.Value.AbsoluteUri, "documents");
        StringAssert.Contains(url.Value.AbsoluteUri, "reports/q3%20final.pdf");
        StringAssert.Contains(url.Value.Query, "X-Amz-Expires=900");
        StringAssert.Contains(url.Value.Query, "X-Amz-Signature=");

        Assert.AreEqual(
            "https://avatars.example.com/ada%20lovelace.png",
            provider.GetRequiredKeyedService<IObjectStore>("Public").GetPublicUrl("ada lovelace.png").Value.AbsoluteUri);
    }

    [TestMethod]
    [DataRow(null, "eu-west-1", null, "BucketName is required")]
    [DataRow("files", null, null, "set Region for AWS, or ServiceUrl")]
    [DataRow("files", "mars-north-1", null, "not a known AWS region")]
    [DataRow("files", "eu-west-1", "AKIDONLY", "both AccessKeyId and SecretAccessKey")]
    public async Task Invalid_options_fail_startup(string? bucket, string? region, string? accessKeyId, string message)
    {
        var services = new ServiceCollection();
        services.AddS3Store(o =>
        {
            o.BucketName = bucket;
            o.Region = region;
            o.AccessKeyId = accessKeyId;
        });
        await using var provider = services.BuildServiceProvider();

        var ex = Assert.ThrowsExactly<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        StringAssert.Contains(ex.Message, message);
        StringAssert.Contains(ex.Message, "Storing:Default");
    }

    [TestMethod]
    public void Only_amazonaws_hosts_count_as_aws()
    {
        Assert.IsTrue(new S3StoreOptions().IsAwsEndpoint);
        Assert.IsTrue(new S3StoreOptions { ServiceUrl = new Uri("https://s3.us-east-1.amazonaws.com") }.IsAwsEndpoint);
        Assert.IsTrue(new S3StoreOptions { ServiceUrl = new Uri("https://s3.cn-north-1.amazonaws.com.cn") }.IsAwsEndpoint);
        Assert.IsFalse(new S3StoreOptions { ServiceUrl = new Uri("https://acct.r2.cloudflarestorage.com") }.IsAwsEndpoint);
        Assert.IsFalse(new S3StoreOptions { ServiceUrl = new Uri("https://amazonaws.com.evil.example") }.IsAwsEndpoint);
    }
}
