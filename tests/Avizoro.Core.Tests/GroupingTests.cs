using Avizoro.Core.Diagnostics;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;

namespace Avizoro.Core.Tests;

public class GroupingTests
{
    private static AvizoroEvent Ex(string stack, string type = "System.NullReferenceException", List<string>? fingerprint = null, string message = "m")
        => new()
        {
            ProjectId = "p",
            EventType = EventType.Exception,
            Exception = new ExceptionInfo { Type = type, Message = message, StackTrace = stack },
            Fingerprint = fingerprint
        };

    [Fact]
    public void TopFrame_SkipsFrameworkFrames()
    {
        const string stack = """
               at System.Linq.Enumerable.First[TSource](IEnumerable`1 source)
               at Microsoft.Maui.Controls.Button.SendClicked()
               at MyApp.Pages.CartPage.OnPay(Object sender, EventArgs e) in /src/CartPage.cs:line 30
            """;

        Assert.Equal("MyApp.Pages.CartPage.OnPay(Object sender, EventArgs e)", EventFingerprint.TopFrame(stack));
        Assert.Equal("CartPage.OnPay", EventFingerprint.Compute(Ex(stack)).Culprit);
    }

    [Fact]
    public void SameFrameworkCrash_FromDifferentAppCallers_SplitsIntoTwoIssues()
    {
        const string fromCart = "   at System.Linq.Enumerable.First[T](IEnumerable`1 s)\n   at MyApp.Cart.Checkout()";
        const string fromProfile = "   at System.Linq.Enumerable.First[T](IEnumerable`1 s)\n   at MyApp.Profile.Load()";

        Assert.NotEqual(EventFingerprint.Compute(Ex(fromCart)).Fingerprint, EventFingerprint.Compute(Ex(fromProfile)).Fingerprint);
    }

    [Fact]
    public void AllFrameworkFrames_FallsBackToFirstFrame()
        => Assert.Equal("System.Threading.Tasks.Task.Wait()",
            EventFingerprint.TopFrame("   at System.Threading.Tasks.Task.Wait()\n   at System.Threading.Thread.Start()"));

    [Fact]
    public void AsyncAndLambdaNames_AreNormalized()
    {
        Assert.Equal("MyApp.Cart.Checkout()",
            EventFingerprint.TopFrame("   at MyApp.Cart.<Checkout>d__12.MoveNext() in /src/Cart.cs:line 9"));
        Assert.Equal("MyApp.Cart.<>c.Load(Int32 id)",
            EventFingerprint.TopFrame("   at MyApp.Cart.<>c.<Load>b__3_0(Int32 id)"));

        // Recompiling renumbers state machines: still one issue.
        var a = EventFingerprint.Compute(Ex("   at MyApp.Cart.<Checkout>d__12.MoveNext()"));
        var b = EventFingerprint.Compute(Ex("   at MyApp.Cart.<Checkout>d__7.MoveNext()"));
        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void JavaStack_UsesFirstAppFrame_AndIgnoresMessageAndLines()
    {
        const string a = "java.lang.IllegalStateException: order 12\n\tat androidx.fragment.app.Fragment.requireContext(Fragment.java:900)\n\tat com.shop.CartFragment.onPay(CartFragment.java:41)";
        const string b = "java.lang.IllegalStateException: order 99\n\tat androidx.fragment.app.Fragment.requireContext(Fragment.java:900)\n\tat com.shop.CartFragment.onPay(CartFragment.java:57)";

        Assert.Equal("com.shop.CartFragment.onPay", EventFingerprint.TopFrame(a));
        Assert.Equal(
            EventFingerprint.Compute(Ex(a, "java.lang.IllegalStateException")).Fingerprint,
            EventFingerprint.Compute(Ex(b, "java.lang.IllegalStateException")).Fingerprint);
    }

    [Fact]
    public void CustomFingerprint_OverridesStackGrouping()
    {
        var a = EventFingerprint.Compute(Ex("   at MyApp.A.One()", fingerprint: ["payment-gateway-timeout"]));
        var b = EventFingerprint.Compute(Ex("   at MyApp.B.Two()", type: "System.TimeoutException", fingerprint: ["payment-gateway-timeout"]));

        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal("NullReferenceException", a.Title); // title still comes from the event
    }

    [Fact]
    public void CustomFingerprint_WithDefaultToken_RefinesDefaultGrouping()
    {
        const string stack = "   at MyApp.Cart.Checkout()";
        var tenantA = EventFingerprint.Compute(Ex(stack, fingerprint: [EventFingerprint.DefaultToken, "tenant-a"]));
        var tenantB = EventFingerprint.Compute(Ex(stack, fingerprint: [EventFingerprint.DefaultToken, "tenant-b"]));
        var tenantA2 = EventFingerprint.Compute(Ex(stack, fingerprint: [EventFingerprint.DefaultToken, "tenant-a"]));

        Assert.NotEqual(tenantA.Fingerprint, tenantB.Fingerprint);
        Assert.Equal(tenantA.Fingerprint, tenantA2.Fingerprint);
    }

    [Fact]
    public void DefaultGrouping_IsUnchanged_ForPlainAppStacks()
    {
        // Events stored before this change must keep landing in their existing issue.
        const string stack = "   at MyApp.Services.Cart.Checkout() in /src/Cart.cs:line 42";
        var fp = EventFingerprint.Compute(Ex(stack)).Fingerprint;

        var expected = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes("p\nSystem.NullReferenceException\nMyApp.Services.Cart.Checkout()"))).ToLowerInvariant();
        Assert.Equal(expected, fp);
    }
}
