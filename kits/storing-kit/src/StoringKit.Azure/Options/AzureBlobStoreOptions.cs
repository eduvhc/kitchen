using Azure.Core;

namespace StoringKit.Azure;

/// <summary>
/// Settings for one Azure Blob Storage container. Bound from <c>Storing:{name}</c> by
/// <c>AddAzureBlobStore(configuration, name)</c>.
/// </summary>
/// <remarks>
/// Authenticate with <see cref="ConnectionString"/>, or with <see cref="ServiceUri"/> plus a <see cref="Credential"/>
/// (Microsoft Entra ID, set in code). <see cref="IObjectStore.GetReadUrlAsync"/> signs with the account key when the
/// connection string has one, and with a user delegation key otherwise — which needs the identity to hold
/// <c>Storage Blob Delegator</c> or an equivalent role.
/// </remarks>
public sealed class AzureBlobStoreOptions : ObjectStoreOptions
{
    /// <summary>
    /// A storage account connection string: account key, SAS, or <c>UseDevelopmentStorage=true</c> for Azurite.
    /// Keep it in a secret store or an environment variable, never in <c>appsettings.json</c>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The blob service endpoint, <c>https://{account}.blob.core.windows.net</c>, used with <see cref="Credential"/>
    /// instead of <see cref="ConnectionString"/>. A URL carrying a SAS token works without a credential.
    /// </summary>
    public Uri? ServiceUri { get; set; }

    /// <summary>The identity used with <see cref="ServiceUri"/>, such as <c>DefaultAzureCredential</c>. Code only.</summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>The container. Required; it must already exist — StoringKit does not create or reconfigure it.</summary>
    public string? ContainerName { get; set; }
}
