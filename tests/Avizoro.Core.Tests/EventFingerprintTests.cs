using Avizoro.Core.Diagnostics;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;

namespace Avizoro.Core.Tests;

public class EventFingerprintTests
{
    private static AvizoroEvent ExceptionEvent(
        string projectId, string type, string message, string? stackTrace = null)
        => new()
        {
            ProjectId = projectId,
            EventType = EventType.Exception,
            Exception = new ExceptionInfo
            {
                Type = type,
                Message = message,
                StackTrace = stackTrace ?? string.Empty
            }
        };

    [Fact]
    public void Compute_SameExceptionTypeAndFrame_GroupsRegardlessOfMessage()
    {
        const string stack = "   at MyApp.Services.Cart.Checkout() in /src/Cart.cs:line 42";

        var a = EventFingerprint.Compute(ExceptionEvent("proj", "System.NullReferenceException", "order 12 failed", stack));
        var b = EventFingerprint.Compute(ExceptionEvent("proj", "System.NullReferenceException", "order 99 failed", stack));

        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void Compute_LineNumberChurn_DoesNotSplitTheIssue()
    {
        var a = EventFingerprint.Compute(ExceptionEvent(
            "proj", "System.Exception", "x", "   at MyApp.A.B() in /src/A.cs:line 10"));
        var b = EventFingerprint.Compute(ExceptionEvent(
            "proj", "System.Exception", "x", "   at MyApp.A.B() in /src/A.cs:line 55"));

        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void Compute_DifferentProject_DoesNotGroup()
    {
        const string stack = "   at MyApp.A.B() in /src/A.cs:line 1";

        var a = EventFingerprint.Compute(ExceptionEvent("proj-a", "System.Exception", "x", stack));
        var b = EventFingerprint.Compute(ExceptionEvent("proj-b", "System.Exception", "x", stack));

        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void Compute_DifferentExceptionType_DoesNotGroup()
    {
        const string stack = "   at MyApp.A.B() in /src/A.cs:line 1";

        var a = EventFingerprint.Compute(ExceptionEvent("proj", "System.ArgumentException", "x", stack));
        var b = EventFingerprint.Compute(ExceptionEvent("proj", "System.InvalidOperationException", "x", stack));

        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void Compute_TitleIsShortTypeName_CulpritIsShortFrame()
    {
        var result = EventFingerprint.Compute(ExceptionEvent(
            "proj", "System.NullReferenceException", "boom",
            "   at MyApp.Services.Cart.Checkout(int id) in /src/Cart.cs:line 42"));

        Assert.Equal("NullReferenceException", result.Title);
        Assert.Equal("Cart.Checkout", result.Culprit);
    }

    [Fact]
    public void Compute_NoStackTrace_YieldsNullCulprit()
    {
        var result = EventFingerprint.Compute(ExceptionEvent(
            "proj", "System.Exception", "boom", stackTrace: null));

        Assert.Null(result.Culprit);
    }

    [Fact]
    public void Compute_NonException_NormalizesDigits_ToGroupSimilarMessages()
    {
        var a = EventFingerprint.Compute(new AvizoroEvent
        {
            ProjectId = "proj",
            EventType = EventType.Log,
            Message = "User 12 not found"
        });
        var b = EventFingerprint.Compute(new AvizoroEvent
        {
            ProjectId = "proj",
            EventType = EventType.Log,
            Message = "User 34 not found"
        });

        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void Compute_NonException_NormalizesGuids()
    {
        var a = EventFingerprint.Compute(new AvizoroEvent
        {
            ProjectId = "proj",
            EventType = EventType.Log,
            Message = "Order 11111111-2222-3333-4444-555555555555 rejected"
        });
        var b = EventFingerprint.Compute(new AvizoroEvent
        {
            ProjectId = "proj",
            EventType = EventType.Log,
            Message = "Order aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee rejected"
        });

        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void Compute_NonException_TitleFallsBackToMessage()
    {
        var result = EventFingerprint.Compute(new AvizoroEvent
        {
            ProjectId = "proj",
            EventType = EventType.Log,
            Message = "disk almost full"
        });

        Assert.Equal("disk almost full", result.Title);
        Assert.Null(result.Culprit);
    }

    [Fact]
    public void Compute_NonException_EmptyMessage_TitleFallsBackToEventType()
    {
        var result = EventFingerprint.Compute(new AvizoroEvent
        {
            ProjectId = "proj",
            EventType = EventType.Navigation,
            Message = null
        });

        Assert.Equal(EventType.Navigation.ToString(), result.Title);
    }

    [Fact]
    public void Compute_ExceptionWithBlankType_FallsBackToMessagePath()
    {
        // Exception present but no type => the message-based fallback is used,
        // which produces a culprit-less result titled from the message.
        var result = EventFingerprint.Compute(new AvizoroEvent
        {
            ProjectId = "proj",
            EventType = EventType.Exception,
            Message = "weird state",
            Exception = new ExceptionInfo { Type = "   ", Message = "weird state" }
        });

        Assert.Null(result.Culprit);
        Assert.Equal("weird state", result.Title);
    }

    [Fact]
    public void Compute_FingerprintIsLowercaseSha1Hex()
    {
        var result = EventFingerprint.Compute(ExceptionEvent(
            "proj", "System.Exception", "x", "   at A.B() in /f.cs:line 1"));

        Assert.Equal(40, result.Fingerprint.Length);
        Assert.Matches("^[0-9a-f]{40}$", result.Fingerprint);
    }
}
