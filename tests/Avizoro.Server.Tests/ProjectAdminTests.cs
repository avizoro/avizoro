using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avizoro.Server.Contracts;

namespace Avizoro.Server.Tests;

public class ProjectAdminTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public ProjectAdminTests(ServerAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_ReturnsCredentialsWithReadKeyAndGeneratedProjectKey()
    {
        var admin = _factory.CreateAuthorizedClient();

        var res = await admin.PostAsJsonAsync("/admin/projects",
            new CreateProjectRequest("acme", "Acme", null));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await res.Content.ReadFromJsonAsync<ProjectCredentials>();
        Assert.NotNull(created);
        Assert.Equal("acme", created!.ProjectId);
        Assert.StartsWith("avzr_", created.ReadKey);       // read key returned once
        Assert.StartsWith("avzp_", created.ProjectKey);    // ingest key auto-generated
    }

    [Fact]
    public async Task Create_DuplicateProjectId_Returns409()
    {
        var admin = _factory.CreateAuthorizedClient();
        await ProjectApi.CreateAsync(admin, "dup");

        var res = await admin.PostAsJsonAsync("/admin/projects",
            new CreateProjectRequest("dup", "again", null));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsProjects_WithoutLeakingTheReadKey()
    {
        var admin = _factory.CreateAuthorizedClient();
        await ProjectApi.CreateAsync(admin, "listed");

        var raw = await admin.GetStringAsync("/admin/projects");

        // ProjectInfo carries no read key/hash; make sure the payload never does.
        Assert.DoesNotContain("readKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", raw, StringComparison.OrdinalIgnoreCase);

        var list = JsonSerializer.Deserialize<List<JsonElement>>(raw)!;
        Assert.Contains(list, p => p.GetProperty("projectId").GetString() == "listed");
    }

    [Fact]
    public async Task RotateKey_InvalidatesOldKey_AndIssuesAWorkingNewOne()
    {
        var admin = _factory.CreateAuthorizedClient();
        var created = await ProjectApi.CreateAsync(admin, "rotate");

        // Old key works as a scoped reader.
        var oldClient = _factory.CreateClientWithToken(created.ReadKey);
        Assert.Equal(HttpStatusCode.OK, (await oldClient.GetAsync("/events")).StatusCode);

        var rotateRes = await admin.PostAsync($"/admin/projects/{created.Id}/rotate-key", content: null);
        rotateRes.EnsureSuccessStatusCode();
        var rotated = await rotateRes.Content.ReadFromJsonAsync<ProjectCredentials>();
        Assert.NotEqual(created.ReadKey, rotated!.ReadKey);

        // Old key no longer authenticates; the new one does.
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync("/events")).StatusCode);
        var newClient = _factory.CreateClientWithToken(rotated.ReadKey);
        Assert.Equal(HttpStatusCode.OK, (await newClient.GetAsync("/events")).StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesProject_AndItsKeyStopsWorking()
    {
        var admin = _factory.CreateAuthorizedClient();
        var created = await ProjectApi.CreateAsync(admin, "deleteme");

        var del = await admin.DeleteAsync($"/admin/projects/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var scoped = _factory.CreateClientWithToken(created.ReadKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await scoped.GetAsync("/events")).StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_RejectAProjectScopedKey_With403()
    {
        var admin = _factory.CreateAuthorizedClient();
        var created = await ProjectApi.CreateAsync(admin, "notadmin");

        var scoped = _factory.CreateClientWithToken(created.ReadKey);

        // A scoped key can read its own data...
        Assert.Equal(HttpStatusCode.OK, (await scoped.GetAsync("/events")).StatusCode);
        // ...but can't manage projects.
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.GetAsync("/admin/projects")).StatusCode);
    }
}
