using Avizoro.Core.Services;

namespace Avizoro.Core.Tests;

public class HttpStatusClassifierTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(299)]
    public void TwoXx_IsDelivered(int status)
        => Assert.Equal(SendResult.Delivered, HttpStatusClassifier.Classify(status));

    [Fact]
    public void RateLimited_429_IsTransient_SoTheEventIsRetriedNotDropped()
        => Assert.Equal(SendResult.TransientFailure, HttpStatusClassifier.Classify(429));

    [Fact]
    public void RequestTimeout_408_IsTransient()
        => Assert.Equal(SendResult.TransientFailure, HttpStatusClassifier.Classify(408));

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public void OtherFourXx_IsRejected_SoItCannotPoisonTheQueue(int status)
        => Assert.Equal(SendResult.Rejected, HttpStatusClassifier.Classify(status));

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void FiveXx_IsTransient(int status)
        => Assert.Equal(SendResult.TransientFailure, HttpStatusClassifier.Classify(status));
}
