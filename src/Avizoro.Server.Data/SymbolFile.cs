namespace Avizoro.Server.Data;

/// <summary>
/// An uploaded debug-symbol file used to symbolicate stack traces at read time:
/// a portable PDB (keyed by the module MVID of the assembly it belongs to), an
/// assembly with an embedded PDB, or an Android R8/ProGuard mapping (keyed by
/// project + release).
/// </summary>
public class SymbolFile
{
    public Guid Id { get; set; }

    /// <summary>One of <see cref="SymbolKinds"/>.</summary>
    public string Kind { get; set; } = SymbolKinds.PortablePdb;

    /// <summary>Lookup key: the MVID ("N" format) for PDBs, "projectId|release" for mappings.</summary>
    public string Key { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];

    public long Size { get; set; }

    public DateTime UploadedAt { get; set; }
}

public static class SymbolKinds
{
    public const string PortablePdb = "PortablePdb";
    public const string EmbeddedPdbAssembly = "EmbeddedPdbAssembly";
    public const string ProguardMapping = "ProguardMapping";

    public static string MappingKey(string projectId, string release) => $"{projectId}|{release}";
}
