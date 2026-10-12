using System.Text;
using Avizoro.Server.Contracts;
using Avizoro.Server.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Symbols;

/// <summary>
/// Debug-symbol management (admin-only; typically called from CI / the MSBuild
/// upload target). <c>POST /symbols</c> takes a multipart form with any mix of:
/// <list type="bullet">
///   <item>an assembly (.dll) + its portable .pdb — stored keyed by the assembly MVID;</item>
///   <item>an assembly with an embedded PDB on its own;</item>
///   <item>an Android <c>mapping.txt</c>, which needs the <c>projectId</c> and
///   <c>release</c> form fields.</item>
/// </list>
/// Re-uploading the same key replaces the previous file.
/// </summary>
public static class SymbolEndpoints
{
    private const long MaxUploadBytes = 200L * 1024 * 1024;

    public static void MapSymbolEndpoints(this WebApplication app, string adminPolicy)
    {
        var group = app.MapGroup("/symbols").RequireAuthorization(adminPolicy);

        group.MapPost("/", async (HttpRequest request, AvizoroDbContext db, SymbolCache cache) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data.");

            var form = await request.ReadFormAsync();
            var projectId = form["projectId"].ToString();
            var release = form["release"].ToString();

            var files = new List<(string Name, byte[] Bytes)>();
            foreach (var file in form.Files)
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer);
                files.Add((Path.GetFileName(file.FileName), buffer.ToArray()));
            }

            if (files.Count == 0)
                return Results.BadRequest("No files were uploaded.");

            var (toStore, results) = Classify(files, projectId, release);

            // Replace any previous upload for the same key.
            foreach (var file in toStore)
            {
                await db.Symbols.Where(s => s.Kind == file.Kind && s.Key == file.Key).ExecuteDeleteAsync();
                db.Symbols.Add(file);
                results.Add(new SymbolUploadResult(file.FileName, file.Kind, file.Key, null));
            }

            await db.SaveChangesAsync();
            cache.Clear();

            return Results.Ok(results);
        }).WithMetadata(new RequestSizeLimitAttribute(MaxUploadBytes))
          .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxUploadBytes });

        group.MapGet("/", async (AvizoroDbContext db) =>
            Results.Ok(await db.Symbols.AsNoTracking()
                .OrderByDescending(s => s.UploadedAt)
                .Select(s => new SymbolFileInfo(s.Id, s.Kind, s.Key, s.FileName, s.Size, s.UploadedAt))
                .ToListAsync()));

        group.MapDelete("/{id:guid}", async (Guid id, AvizoroDbContext db, SymbolCache cache) =>
        {
            var deleted = await db.Symbols.Where(s => s.Id == id).ExecuteDeleteAsync();
            cache.Clear();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    /// <summary>Sorts the uploaded files into storable symbols and per-file errors.</summary>
    private static (List<SymbolFile> ToStore, List<SymbolUploadResult> Results) Classify(
        List<(string Name, byte[] Bytes)> files, string projectId, string release)
    {
        var results = new List<SymbolUploadResult>();
        var toStore = new List<SymbolFile>();

        var assemblies = files
            .Where(f => IsAssembly(f.Name))
            .Select(f => (f.Name, f.Bytes, Identity: AssemblyIdentity.TryRead(f.Bytes)))
            .ToList();
        var paired = new HashSet<string>();

        foreach (var (name, bytes) in files.Where(f => HasExtension(f.Name, ".pdb")))
        {
            Guid pdbId;
            try
            {
                using var pdb = PortablePdb.FromPdb(bytes);
                pdbId = pdb.PdbId;
            }
            catch (Exception)
            {
                results.Add(new(name, null, null, "Not a portable PDB (Windows/full PDBs are not supported)."));
                continue;
            }

            var owner = assemblies.FirstOrDefault(a => a.Identity?.ExpectedPdbId == pdbId);
            if (owner.Identity is null)
            {
                results.Add(new(name, null, null, "No uploaded assembly matches this PDB; upload the .dll it was built with alongside it."));
                continue;
            }

            paired.Add(owner.Name);
            toStore.Add(NewFile(SymbolKinds.PortablePdb, owner.Identity.Mvid.ToString("N"), name, bytes));
        }

        foreach (var (name, bytes, identity) in assemblies.Where(a => !paired.Contains(a.Name)))
        {
            if (identity is null)
                results.Add(new(name, null, null, "Not a .NET assembly."));
            else if (identity.HasEmbeddedPdb)
                toStore.Add(NewFile(SymbolKinds.EmbeddedPdbAssembly, identity.Mvid.ToString("N"), name, bytes));
            else
                results.Add(new(name, null, null, "Assembly has no embedded PDB and no matching .pdb was uploaded."));
        }

        foreach (var (name, bytes) in files.Where(f => HasExtension(f.Name, ".txt")))
        {
            if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(release))
            {
                results.Add(new(name, null, null, "Mapping files need the 'projectId' and 'release' form fields."));
                continue;
            }

            if (ProguardMapping.Parse(Encoding.UTF8.GetString(bytes)).ClassCount == 0)
            {
                results.Add(new(name, null, null, "No class mappings found; is this an R8/ProGuard mapping.txt?"));
                continue;
            }

            toStore.Add(NewFile(SymbolKinds.ProguardMapping, SymbolKinds.MappingKey(projectId, release), name, bytes));
        }

        foreach (var (name, _) in files.Where(f => !IsAssembly(f.Name) && !HasExtension(f.Name, ".pdb") && !HasExtension(f.Name, ".txt")))
            results.Add(new(name, null, null, "Unsupported file type (expected .dll/.exe, .pdb or mapping .txt)."));

        return (toStore, results);
    }

    private static bool HasExtension(string name, string extension)
        => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    private static bool IsAssembly(string name) => HasExtension(name, ".dll") || HasExtension(name, ".exe");

    private static SymbolFile NewFile(string kind, string key, string name, byte[] bytes) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Key = key,
        FileName = name,
        Content = bytes,
        Size = bytes.LongLength,
        UploadedAt = DateTime.UtcNow
    };
}
