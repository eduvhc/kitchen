using Azure;

namespace LockingKit.Azure.UnitTests;

[TestClass]
public sealed class AzureTranslationTests
{
    [TestMethod]
    [DataRow(408, null)]
    [DataRow(429, null)]
    [DataRow(500, "InternalError")]
    [DataRow(503, "ServerBusy")]
    public void Throttling_timeouts_and_server_errors_are_transient(int status, string? code)
    {
        var error = AzureBlobLeaseBackend.Translate(new RequestFailedException(status, "failed", code, null));

        Assert.AreEqual(LockingErrors.Unavailable, error?.Code);
        Assert.IsTrue(error!.IsTransient);
    }

    [TestMethod]
    public void A_request_that_never_got_a_response_is_transient()
    {
        var error = AzureBlobLeaseBackend.Translate(
            new RequestFailedException(0, "no response", null, new HttpRequestException("connection refused")));

        Assert.AreEqual(LockingErrors.Unavailable, error?.Code);
    }

    [TestMethod]
    [DataRow(401, "NoAuthenticationInformation")]
    [DataRow(403, "AuthorizationPermissionMismatch")]
    [DataRow(403, "AuthenticationFailed")]
    [DataRow(404, "ContainerNotFound")]
    public void Credentials_permissions_and_a_missing_container_are_misconfiguration(int status, string code)
    {
        var error = AzureBlobLeaseBackend.Translate(new RequestFailedException(status, "failed", code, null));

        Assert.AreEqual(LockingErrors.Misconfigured, error?.Code);
        StringAssert.Contains(error!.Message, code);
    }

    [TestMethod]
    public void Anything_else_is_left_to_the_engine_which_reports_it_as_a_backend_failure()
    {
        Assert.IsNull(AzureBlobLeaseBackend.Translate(new RequestFailedException(400, "failed", "InvalidHeaderValue", null)));
    }

    [TestMethod]
    [DataRow("LeaseAlreadyPresent", true)]
    [DataRow("LeaseIsBreakingAndCannotBeAcquired", true)]
    [DataRow("LeaseIdMismatchWithLeaseOperation", false)]
    [DataRow("BlobAlreadyExists", false)]
    public void Only_another_holder_s_lease_makes_an_attempt_busy(string code, bool busy)
    {
        Assert.AreEqual(busy, AzureBlobLeaseBackend.IsLeaseHeldElsewhere(new RequestFailedException(409, "conflict", code, null)));
    }

    [TestMethod]
    [DataRow(409, "LeaseIdMismatchWithLeaseOperation", true)]
    [DataRow(409, "LeaseNotPresentWithLeaseOperation", true)]
    [DataRow(409, "LeaseIsBrokenAndCannotBeRenewed", true)]
    [DataRow(404, "BlobNotFound", true)]
    [DataRow(500, "InternalError", false)]
    [DataRow(503, "ServerBusy", false)]
    public void A_renewal_is_lost_only_on_a_definite_answer(int status, string code, bool gone)
    {
        Assert.AreEqual(gone, HeldBlobLease.IsGone(new RequestFailedException(status, "failed", code, null)));
    }
}
