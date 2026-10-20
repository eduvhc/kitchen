using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace StoringKit.Azure.UnitTests;

[TestClass]
public sealed class AzureRegistrationTests
{
    // The well-known Azurite development account; signing a SAS needs no network.
    private const string DevelopmentStorage = "UseDevelopmentStorage=true";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Binds_each_named_store_from_its_own_section()
    {
        var configuration = Configuration(
            ("Storing:Default:ConnectionString", DevelopmentStorage),
            ("Storing:Default:ContainerName", "documents"),
            ("Storing:Public:ConnectionString", DevelopmentStorage),
            ("Storing:Public:ContainerName", "avatars"),
            ("Storing:Public:PublicBaseUrl", "https://cdn.example.com/avatars/"));

        var services = new ServiceCollection();
        services.AddAzureBlobStore(configuration);
        services.AddAzureBlobStore(configuration, "Public");
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();

        var monitor = provider.GetRequiredService<IOptionsMonitor<AzureBlobStoreOptions>>();
        Assert.AreEqual("documents", monitor.Get(ObjectStores.DefaultName).ContainerName);
        Assert.AreEqual("avatars", monitor.Get("Public").ContainerName);

        var publicUrl = provider.GetRequiredKeyedService<IObjectStore>("Public").GetPublicUrl("ada.png");
        Assert.AreEqual("https://cdn.example.com/avatars/ada.png", publicUrl.Value.AbsoluteUri);
        Assert.ThrowsExactly<InvalidOperationException>(() => provider.GetRequiredService<IObjectStore>().GetPublicUrl("ada.png"));
    }

    [TestMethod]
    public async Task Signs_read_urls_with_the_account_key()
    {
        var services = new ServiceCollection();
        services.AddAzureBlobStore(o =>
        {
            o.ConnectionString = DevelopmentStorage;
            o.ContainerName = "documents";
        });
        await using var provider = services.BuildServiceProvider();

        var url = await provider.GetRequiredService<IObjectStore>().GetReadUrlAsync("reports/q3 final.pdf", TimeSpan.FromMinutes(10), TestContext.CancellationToken);

        Assert.IsTrue(url.IsSuccess, url.ToString());
        StringAssert.StartsWith(url.Value.AbsoluteUri, "http://127.0.0.1:10000/devstoreaccount1/documents/reports/q3%20final.pdf?");
        StringAssert.Contains(url.Value.Query, "sp=r");
        StringAssert.Contains(url.Value.Query, "sr=b");
        StringAssert.Contains(url.Value.Query, "sig=");
    }

    [TestMethod]
    public async Task A_sas_connection_string_cannot_sign_read_urls()
    {
        var services = new ServiceCollection();
        services.AddAzureBlobStore(o =>
        {
            o.ConnectionString = "BlobEndpoint=https://acct.blob.core.windows.net/;SharedAccessSignature=sv=2024-08-04&sig=abc";
            o.ContainerName = "documents";
        });
        await using var provider = services.BuildServiceProvider();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IObjectStore>().GetReadUrlAsync("a", TimeSpan.FromMinutes(1), TestContext.CancellationToken));
    }

    [TestMethod]
    [DataRow(null, null, "documents", "exactly one of ConnectionString and ServiceUri")]
    [DataRow(DevelopmentStorage, "https://acct.blob.core.windows.net", "documents", "exactly one of ConnectionString and ServiceUri")]
    [DataRow(DevelopmentStorage, null, null, "ContainerName is required")]
    [DataRow(DevelopmentStorage, null, "Has_Upper", "ContainerName is required")]
    [DataRow(DevelopmentStorage, null, "ab", "ContainerName is required")]
    [DataRow(DevelopmentStorage, null, "double--hyphen", "ContainerName is required")]
    public async Task Invalid_options_fail_startup(string? connectionString, string? serviceUri, string? container, string message)
    {
        var services = new ServiceCollection();
        services.AddAzureBlobStore(o =>
        {
            o.ConnectionString = connectionString;
            o.ServiceUri = serviceUri is null ? null : new Uri(serviceUri);
            o.ContainerName = container;
        });
        await using var provider = services.BuildServiceProvider();

        var ex = Assert.ThrowsExactly<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        StringAssert.Contains(ex.Message, message);
        StringAssert.Contains(ex.Message, "Storing:Default");
    }

    [TestMethod]
    public async Task A_relative_public_base_url_fails_startup()
    {
        var services = new ServiceCollection();
        services.AddAzureBlobStore("Public", o =>
        {
            o.ConnectionString = DevelopmentStorage;
            o.ContainerName = "avatars";
            o.PublicBaseUrl = new Uri("/avatars", UriKind.Relative);
        });
        await using var provider = services.BuildServiceProvider();

        var ex = Assert.ThrowsExactly<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        StringAssert.Contains(ex.Message, "Storing:Public:PublicBaseUrl");
    }

    [TestMethod]
    public async Task A_service_uri_with_a_token_credential_is_accepted()
    {
        var services = new ServiceCollection();
        services.AddAzureBlobStore(o =>
        {
            o.ServiceUri = new Uri("https://acct.blob.core.windows.net");
            o.Credential = new StaticCredential();
            o.ContainerName = "documents";
        });
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.IsNotNull(provider.GetRequiredService<IObjectStore>());
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private sealed class StaticCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
