using System.Net.Http.Json;
using Avizoro.Server.Contracts;

namespace Avizoro.Server.Tests;

/// <summary>Helpers to drive the admin project API from tests.</summary>
internal static class ProjectApi
{
    public static async Task<ProjectCredentials> CreateAsync(
        HttpClient admin, string projectId, string name = "Test")
    {
        var res = await admin.PostAsJsonAsync("/admin/projects",
            new CreateProjectRequest(projectId, name, null));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ProjectCredentials>())!;
    }
}
