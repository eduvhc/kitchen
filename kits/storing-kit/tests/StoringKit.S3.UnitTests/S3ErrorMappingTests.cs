using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using ResultingKit;

namespace StoringKit.S3.UnitTests;

[TestClass]
public sealed class S3ErrorMappingTests
{
    private const string Key = "a/b.txt";

    [TestMethod]
    [DataRow(404, "NoSuchKey", ObjectStoreOperation.Get, StoringErrors.NotFound, ErrorKind.NotFound)]
    [DataRow(404, "NotFound", ObjectStoreOperation.GetProperties, StoringErrors.NotFound, ErrorKind.NotFound)]
    [DataRow(400, "BadDigest", ObjectStoreOperation.Put, StoringErrors.ChecksumMismatch, ErrorKind.Validation)]
    [DataRow(400, "InvalidDigest", ObjectStoreOperation.Put, StoringErrors.ChecksumMismatch, ErrorKind.Validation)]
    [DataRow(400, "EntityTooLarge", ObjectStoreOperation.Put, StoringErrors.TooLarge, ErrorKind.Validation)]
    [DataRow(503, "SlowDown", ObjectStoreOperation.Put, StoringErrors.Throttled, ErrorKind.Transient)]
    [DataRow(429, "TooManyRequests", ObjectStoreOperation.Get, StoringErrors.Throttled, ErrorKind.Transient)]
    [DataRow(429, null, ObjectStoreOperation.Get, StoringErrors.Throttled, ErrorKind.Transient)]
    [DataRow(400, "RequestTimeout", ObjectStoreOperation.Put, StoringErrors.Timeout, ErrorKind.Transient)]
    [DataRow(500, "InternalError", ObjectStoreOperation.List, StoringErrors.Unavailable, ErrorKind.Transient)]
    [DataRow(503, "ServiceUnavailable", ObjectStoreOperation.Delete, StoringErrors.Unavailable, ErrorKind.Transient)]
    [DataRow(502, null, ObjectStoreOperation.Get, StoringErrors.Unavailable, ErrorKind.Transient)]
    public void Maps_expected_service_responses(int status, string? code, ObjectStoreOperation operation, string expectedCode, ErrorKind expectedKind)
    {
        var error = S3ObjectStore.Translate(Exception(status, code), operation, Key, null);

        Assert.IsNotNull(error);
        Assert.AreEqual(expectedCode, error.Code);
        Assert.AreEqual(expectedKind, error.Kind);
    }

    [TestMethod]
    [DataRow(412, "PreconditionFailed")]
    [DataRow(409, "ConditionalRequestConflict")]
    public void A_create_only_put_that_loses_is_already_exists(int status, string code)
    {
        var error = S3ObjectStore.Translate(Exception(status, code), ObjectStoreOperation.Put, Key, WriteCondition.IfNotExists);

        Assert.AreEqual(StoringErrors.AlreadyExists, error?.Code);
        Assert.AreEqual(ErrorKind.Conflict, error?.Kind);
    }

    [TestMethod]
    [DataRow(412, "PreconditionFailed")]
    [DataRow(404, "NoSuchKey")]
    [DataRow(409, "ConditionalRequestConflict")]
    public void An_if_match_put_against_a_changed_or_missing_object_is_a_failed_precondition(int status, string code)
    {
        var error = S3ObjectStore.Translate(Exception(status, code), ObjectStoreOperation.Put, Key, WriteCondition.IfMatch("\"abc\""));

        Assert.AreEqual(StoringErrors.PreconditionFailed, error?.Code);
        Assert.AreEqual(ErrorKind.Conflict, error?.Kind);
    }

    [TestMethod]
    [DataRow(404, "NoSuchBucket")]
    [DataRow(403, "AccessDenied")]
    [DataRow(403, "SignatureDoesNotMatch")]
    [DataRow(403, "InvalidAccessKeyId")]
    [DataRow(400, "InvalidArgument")]
    [DataRow(501, "NotImplemented")]
    public void Misconfiguration_and_unsupported_features_are_not_mapped_so_they_throw(int status, string code)
    {
        Assert.IsNull(S3ObjectStore.Translate(Exception(status, code), ObjectStoreOperation.Put, Key, null));
    }

    private static AmazonS3Exception Exception(int status, string? code) =>
        new("message", ErrorType.Sender, code, "request-id", (HttpStatusCode)status);
}
