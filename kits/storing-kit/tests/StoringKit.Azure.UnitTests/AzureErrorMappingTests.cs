using System.Net.Http;
using Azure;
using ResultingKit;

namespace StoringKit.Azure.UnitTests;

[TestClass]
public sealed class AzureErrorMappingTests
{
    private const string Key = "a/b.txt";

    [TestMethod]
    [DataRow(404, "BlobNotFound", ObjectStoreOperation.Get, StoringErrors.NotFound, ErrorKind.NotFound)]
    [DataRow(404, "BlobNotFound", ObjectStoreOperation.GetProperties, StoringErrors.NotFound, ErrorKind.NotFound)]
    [DataRow(404, null, ObjectStoreOperation.GetProperties, StoringErrors.NotFound, ErrorKind.NotFound)]
    [DataRow(400, "Md5Mismatch", ObjectStoreOperation.Put, StoringErrors.ChecksumMismatch, ErrorKind.Validation)]
    [DataRow(400, "InvalidMd5", ObjectStoreOperation.Put, StoringErrors.ChecksumMismatch, ErrorKind.Validation)]
    [DataRow(413, "RequestBodyTooLarge", ObjectStoreOperation.Put, StoringErrors.TooLarge, ErrorKind.Validation)]
    [DataRow(429, null, ObjectStoreOperation.Get, StoringErrors.Throttled, ErrorKind.Transient)]
    [DataRow(503, "ServerBusy", ObjectStoreOperation.Put, StoringErrors.Throttled, ErrorKind.Transient)]
    [DataRow(503, "ServiceUnavailable", ObjectStoreOperation.Put, StoringErrors.Unavailable, ErrorKind.Transient)]
    [DataRow(500, "InternalError", ObjectStoreOperation.List, StoringErrors.Unavailable, ErrorKind.Transient)]
    [DataRow(500, "OperationTimedOut", ObjectStoreOperation.Get, StoringErrors.Timeout, ErrorKind.Transient)]
    [DataRow(408, null, ObjectStoreOperation.Get, StoringErrors.Timeout, ErrorKind.Transient)]
    public void Maps_expected_service_responses(int status, string? code, ObjectStoreOperation operation, string expectedCode, ErrorKind expectedKind)
    {
        var error = AzureBlobObjectStore.Translate(new RequestFailedException(status, "message", code, null), operation, Key, null);

        Assert.IsNotNull(error);
        Assert.AreEqual(expectedCode, error.Code);
        Assert.AreEqual(expectedKind, error.Kind);
    }

    [TestMethod]
    public void A_create_only_put_that_finds_a_blob_is_already_exists()
    {
        var error = AzureBlobObjectStore.Translate(
            new RequestFailedException(409, "exists", "BlobAlreadyExists", null), ObjectStoreOperation.Put, Key, WriteCondition.IfNotExists);

        Assert.AreEqual(StoringErrors.AlreadyExists, error?.Code);
        Assert.AreEqual(ErrorKind.Conflict, error?.Kind);
    }

    [TestMethod]
    public void A_create_only_put_that_fails_its_precondition_is_already_exists()
    {
        var error = AzureBlobObjectStore.Translate(
            new RequestFailedException(412, "not met", "ConditionNotMet", null), ObjectStoreOperation.Put, Key, WriteCondition.IfNotExists);

        Assert.AreEqual(StoringErrors.AlreadyExists, error?.Code);
    }

    [TestMethod]
    [DataRow(412, "ConditionNotMet")]
    [DataRow(404, "BlobNotFound")]
    public void An_if_match_put_against_a_changed_or_missing_blob_is_a_failed_precondition(int status, string code)
    {
        var error = AzureBlobObjectStore.Translate(
            new RequestFailedException(status, "message", code, null), ObjectStoreOperation.Put, Key, WriteCondition.IfMatch("\"0x1\""));

        Assert.AreEqual(StoringErrors.PreconditionFailed, error?.Code);
        Assert.AreEqual(ErrorKind.Conflict, error?.Kind);
    }

    [TestMethod]
    public void Azurite_md5_mismatches_are_recognised()
    {
        var error = AzureBlobObjectStore.Translate(
            new RequestFailedException(400, "Provided contentMD5 doesn't match.", "InvalidOperation", null), ObjectStoreOperation.Put, Key, null);

        Assert.AreEqual(StoringErrors.ChecksumMismatch, error?.Code);
    }

    [TestMethod]
    [DataRow(404, "ContainerNotFound")]
    [DataRow(403, "AuthorizationFailure")]
    [DataRow(403, "AuthenticationFailed")]
    [DataRow(400, "InvalidHeaderValue")]
    [DataRow(409, "LeaseIdMissing")]
    public void Misconfiguration_and_bugs_are_not_mapped_so_they_throw(int status, string code)
    {
        Assert.IsNull(AzureBlobObjectStore.Translate(new RequestFailedException(status, "message", code, null), ObjectStoreOperation.Put, Key, null));
    }

    [TestMethod]
    public void A_response_that_never_arrived_is_classified_by_its_cause()
    {
        var error = AzureBlobObjectStore.Translate(
            new RequestFailedException(0, "no response", null, new HttpRequestException("connection reset")), ObjectStoreOperation.Get, Key, null);

        Assert.AreEqual(StoringErrors.Unavailable, error?.Code);
        Assert.AreEqual(ErrorKind.Transient, error?.Kind);
    }
}
