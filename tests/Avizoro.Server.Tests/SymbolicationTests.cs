using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Server.Symbols;

namespace Avizoro.Server.Tests;

public class SymbolicationTests : IClassFixture<ServerAppFactory>
{
    private const string Mapping = """
        # compiler: R8
        com.example.shop.CartService -> a.b:
            int count -> a
            1:4:void checkout(int):20:23 -> a
            5:5:void reset():40:40 -> b
        com.example.shop.PaymentException -> a.c:
        """;

    private readonly ServerAppFactory _factory;

    public SymbolicationTests(ServerAppFactory factory) => _factory = factory;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowForSymbolication() => throw new InvalidOperationException("release build");

    [Fact]
    public async Task ReleaseStackTrace_GetsFileAndLine_FromUploadedPdb()
    {
        // A "release" exception: frames carry MVID/token/IL offset but no file/line.
        ExceptionInfo info;
        try
        {
            ThrowForSymbolication();
            return;
        }
        catch (Exception ex)
        {
            info = ExceptionInfo.FromException(ex);
        }

        foreach (var frame in info.Frames!)
        {
            frame.FileName = null;
            frame.LineNumber = null;
        }
        info.StackTrace = Symbolicator.Render(info.Frames);
        Assert.DoesNotContain(":line", info.StackTrace);

        var e = new AvizoroEvent { ProjectId = "sym-pdb", EventType = EventType.Exception, Exception = info };
        (await _factory.CreateClient().PostAsJsonAsync("/events", e)).EnsureSuccessStatusCode();

        // Before symbols: returned as-is.
        var authed = _factory.CreateAuthorizedClient();
        var before = await authed.GetFromJsonAsync<JsonElement>($"/events/{e.Id}");
        Assert.False(before.GetProperty("symbolicated").GetBoolean());

        // Upload this test assembly with its PDB, exactly like CI would.
        var assembly = typeof(SymbolicationTests).Assembly.Location;
        var results = await UploadAsync(authed, [assembly, Path.ChangeExtension(assembly, ".pdb")]);
        Assert.Contains(results.EnumerateArray(), r => r.GetProperty("kind").GetString() == "PortablePdb");

        var after = await authed.GetFromJsonAsync<JsonElement>($"/events/{e.Id}");
        Assert.True(after.GetProperty("symbolicated").GetBoolean());
        var stack = after.GetProperty("exception").GetProperty("stackTrace").GetString()!;
        Assert.Contains("SymbolicationTests.cs:line", stack);
        Assert.Contains(nameof(ThrowForSymbolication), stack);
    }

    [Fact]
    public async Task JavaStackTrace_IsRetraced_WithProjectReleaseMapping()
    {
        var authed = _factory.CreateAuthorizedClient();
        await UploadAsync(authed, [], ("mapping.txt", Encoding.UTF8.GetBytes(Mapping)), projectId: "sym-java", release: "2.0.0");

        var e = new AvizoroEvent
        {
            ProjectId = "sym-java",
            Release = "2.0.0",
            EventType = EventType.Exception,
            Exception = new ExceptionInfo
            {
                Type = "a.c",
                Message = "declined",
                StackTrace = "a.c: declined\n\tat a.b.a(SourceFile:3)\n\tat android.os.Handler.dispatchMessage(Handler.java:106)"
            }
        };
        (await _factory.CreateClient().PostAsJsonAsync("/events", e)).EnsureSuccessStatusCode();

        var detail = await authed.GetFromJsonAsync<JsonElement>($"/events/{e.Id}");
        var exception = detail.GetProperty("exception");
        Assert.Equal("com.example.shop.PaymentException", exception.GetProperty("type").GetString());
        var stack = exception.GetProperty("stackTrace").GetString()!;
        Assert.Contains("at com.example.shop.CartService.checkout(CartService.java:22)", stack);
        Assert.Contains("android.os.Handler.dispatchMessage(Handler.java:106)", stack); // untouched
    }

    [Fact]
    public async Task OtherRelease_IsNotRetraced()
    {
        var authed = _factory.CreateAuthorizedClient();
        await UploadAsync(authed, [], ("mapping.txt", Encoding.UTF8.GetBytes(Mapping)), projectId: "sym-other", release: "1.0.0");

        var e = new AvizoroEvent
        {
            ProjectId = "sym-other",
            Release = "9.9.9",
            EventType = EventType.Exception,
            Exception = new ExceptionInfo { Type = "a.c", StackTrace = "\tat a.b.a(SourceFile:3)" }
        };
        (await _factory.CreateClient().PostAsJsonAsync("/events", e)).EnsureSuccessStatusCode();

        var detail = await authed.GetFromJsonAsync<JsonElement>($"/events/{e.Id}");
        Assert.Equal("a.c", detail.GetProperty("exception").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Upload_ReportsErrors_ForUnmatchedPdbAndMappingWithoutRelease()
    {
        var authed = _factory.CreateAuthorizedClient();
        var pdb = Path.ChangeExtension(typeof(SymbolicationTests).Assembly.Location, ".pdb");

        var results = await UploadAsync(authed, [pdb], ("mapping.txt", Encoding.UTF8.GetBytes(Mapping)));

        Assert.All(results.EnumerateArray(), r => Assert.False(string.IsNullOrEmpty(r.GetProperty("error").GetString())));
    }

    [Fact]
    public async Task SymbolEndpoints_AreAdminOnly()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/symbols")).StatusCode);
    }

    [Fact]
    public void Retrace_MapsLineRanges()
    {
        var mapping = ProguardMapping.Parse(Mapping);

        Assert.Equal("\tat com.example.shop.CartService.reset(CartService.java:40)",
            mapping.RetraceStackTrace("\tat a.b.b(SourceFile:5)"));
        Assert.Equal("at com.example.shop.CartService.checkout(CartService.java:20)",
            mapping.RetraceStackTrace("at a.b.a(SourceFile:1)"));
        Assert.Equal("at x.y.z(Unknown Source)", mapping.RetraceStackTrace("at x.y.z(Unknown Source)"));
    }

    private static async Task<JsonElement> UploadAsync(
        HttpClient client, string[] paths, (string Name, byte[] Bytes)? extra = null,
        string? projectId = null, string? release = null)
    {
        using var form = new MultipartFormDataContent();
        foreach (var path in paths)
        {
            var content = new ByteArrayContent(await File.ReadAllBytesAsync(path));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "files", Path.GetFileName(path));
        }
        if (extra is { } x)
            form.Add(new ByteArrayContent(x.Bytes), "files", x.Name);
        if (projectId is not null)
            form.Add(new StringContent(projectId), "projectId");
        if (release is not null)
            form.Add(new StringContent(release), "release");

        var response = await client.PostAsync("/symbols", form);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
