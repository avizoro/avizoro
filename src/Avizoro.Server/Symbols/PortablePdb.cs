using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Avizoro.Server.Symbols;

/// <summary>Where a frame's IL offset lands in source.</summary>
public sealed record SourceLocation(string File, int Line, int Column);

/// <summary>
/// A loaded portable PDB that can map (method token, IL offset) to a source line.
/// Built either from a standalone .pdb or from an assembly with an embedded PDB.
/// </summary>
public sealed class PortablePdb : IDisposable
{
    private readonly MetadataReaderProvider _provider;
    private readonly MetadataReader _reader;

    private PortablePdb(MetadataReaderProvider provider)
    {
        _provider = provider;
        _reader = provider.GetMetadataReader();
    }

    public static PortablePdb FromPdb(byte[] pdb)
        => new(MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb, writable: false)));

    /// <summary>Opens the PDB embedded in an assembly, or null when it has none.</summary>
    public static PortablePdb? FromAssemblyWithEmbeddedPdb(byte[] assembly)
    {
        using var pe = new PEReader(new MemoryStream(assembly, writable: false));
        var entry = pe.ReadDebugDirectory().FirstOrDefault(e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
        return entry.DataSize == 0 ? null : new PortablePdb(pe.ReadEmbeddedPortablePdbDebugDirectoryData(entry));
    }

    /// <summary>The PDB id GUID (matches the assembly's CodeView debug entry).</summary>
    public Guid PdbId => new BlobContentId(_reader.DebugMetadataHeader!.Id).Guid;

    /// <summary>Source location of the last visible sequence point at or before <paramref name="ilOffset"/>.</summary>
    public SourceLocation? Resolve(int metadataToken, int ilOffset)
    {
        // Only method definitions (table 0x06) have debug info.
        if ((metadataToken >> 24) != 0x06)
            return null;

        var rowNumber = metadataToken & 0x00FFFFFF;
        if (rowNumber == 0 || rowNumber > _reader.MethodDebugInformation.Count)
            return null;

        var handle = MetadataTokens.MethodDefinitionHandle(rowNumber);
        var info = _reader.GetMethodDebugInformation(handle.ToDebugInformationHandle());

        SequencePoint? best = null;
        foreach (var point in info.GetSequencePoints())
        {
            if (point.IsHidden)
                continue;
            if (point.Offset > ilOffset)
                break;
            best = point;
        }

        if (best is not { } sp)
            return null;

        var document = _reader.GetDocument(sp.Document);
        return new SourceLocation(_reader.GetString(document.Name), sp.StartLine, sp.StartColumn);
    }

    public void Dispose() => _provider.Dispose();
}

/// <summary>Identity read from an uploaded assembly: its MVID, and the PDB it expects.</summary>
public sealed record AssemblyIdentity(Guid Mvid, Guid? ExpectedPdbId, bool HasEmbeddedPdb)
{
    public static AssemblyIdentity? TryRead(byte[] assembly)
    {
        try
        {
            using var pe = new PEReader(new MemoryStream(assembly, writable: false));
            if (!pe.HasMetadata)
                return null;

            var md = pe.GetMetadataReader();
            var mvid = md.GetGuid(md.GetModuleDefinition().Mvid);

            var debug = pe.ReadDebugDirectory();
            Guid? pdbId = null;
            var codeView = debug.FirstOrDefault(e => e.Type == DebugDirectoryEntryType.CodeView);
            if (codeView.DataSize > 0)
                pdbId = pe.ReadCodeViewDebugDirectoryData(codeView).Guid;

            var embedded = debug.Any(e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
            return new AssemblyIdentity(mvid, pdbId, embedded);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }
}
