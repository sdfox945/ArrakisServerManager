using System.Text.Json;
using Arrakis.Core;

namespace Arrakis.Infrastructure;

public sealed class CustomSettingStore : ICustomSettingStore
{
    public const int Limit=1_048_576;
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true,MaxDepth=24};
    private readonly string path;
    private string? loadedHash;
    private bool loaded,recovered;
    public CustomSettingStore(string root,bool demo)=>path=Path.Combine(root,"CustomSettings",demo?"Demo":"Live","definitions.json");
    public string Status {get;private set;}="";
    public bool ReadOnly {get;private set;}
    public static CustomCatalogDocument Parse(byte[] bytes)
    {
        if(bytes.Length>Limit)throw new InvalidOperationException("Metadata file exceeds 1 MiB.");
        using var envelope=JsonDocument.Parse(bytes,new(){MaxDepth=24});
        if(envelope.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("Metadata must be a versioned object.");
        var properties=envelope.RootElement.EnumerateObject().ToArray();
        if(properties.Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=properties.Length)throw new InvalidOperationException("Duplicate metadata document fields.");
        var version=properties.FirstOrDefault(p=>p.Name.Equals("SchemaVersion",StringComparison.OrdinalIgnoreCase));
        if(version.Value.ValueKind!=JsonValueKind.Number||!version.Value.TryGetInt32(out var schema))throw new InvalidOperationException("Missing metadata schema version.");
        if(schema is not (0 or 1))throw new NotSupportedException("This metadata schema needs a newer Arrakis version; the catalog is read-only and preserved.");
        var document=JsonSerializer.Deserialize<CustomCatalogDocument>(bytes,Json)??throw new InvalidOperationException("Empty metadata catalog.");
        if(document.SchemaVersion is not (0 or 1))throw new NotSupportedException("This metadata schema needs a newer Arrakis version; the catalog is read-only and preserved.");
        if(document.Definitions==null||document.Definitions.Length>1000)throw new InvalidOperationException("Catalog limit is 1,000 definitions.");
        foreach(var definition in document.Definitions){if(definition==null)throw new InvalidOperationException("Invalid metadata entry.");definition.Validate();}
        if(document.Definitions.Select(d=>d.Identity).Distinct().Count()!=document.Definitions.Length)throw new InvalidOperationException("Duplicate metadata identities.");
        return document with{SchemaVersion=1};
    }
    private static byte[] Read(string file)
    {
        SafePath(file);
        using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>Limit)throw new InvalidOperationException("Metadata file exceeds 1 MiB.");
        using var result=new MemoryStream();stream.CopyTo(result);return result.ToArray();
    }
    public IReadOnlyList<CustomSettingDefinition> Load()
    {
        loaded=true;recovered=false;ReadOnly=false;Status="Local metadata only; no active INI values are stored.";
        if(!File.Exists(path)){loadedHash=null;return [];}
        try{var bytes=Read(path);loadedHash=ConfigurationIntegrity.Hash(bytes);return Parse(bytes).Definitions;}
        catch(NotSupportedException e){ReadOnly=true;Status=e.Message;return [];}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or NullReferenceException)
        {
            try
            {
                loadedHash=File.Exists(path)&&new FileInfo(path).Length<=Limit?ConfigurationIntegrity.Hash(Read(path)):null;
                var definitions=Parse(Read(path+".previous")).Definitions;recovered=true;Status="Recovered previous metadata in memory. The damaged file will be retained on the next explicit metadata save.";return definitions;
            }
            catch(Exception backup)when(backup is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or NullReferenceException or NotSupportedException){ReadOnly=true;Status="Metadata could not be read; files are preserved. Move aside/recover the local catalog or import after resolving it. INI editing remains available.";return [];}
        }
    }
    public void Save(IReadOnlyList<CustomSettingDefinition> definitions)
    {
        if(!loaded)Load();if(ReadOnly)throw new InvalidOperationException(Status);
        SafePath(path);SafePath(path+".previous");SafePath(path+".lock");
        var bytes=Serialize(definitions);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // A sidecar kernel handle serializes writers; original hash also detects external edits since Load.
        using var mutex=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var current=File.Exists(path)?ConfigurationIntegrity.Hash(Read(path)):null;
        if(current!=loadedHash)throw new InvalidOperationException("Metadata changed outside this editor. Reload metadata before retrying; nothing was overwritten.");
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}
            if(File.Exists(path))
            {
                if(recovered)File.Copy(path,path+".damaged-"+Guid.NewGuid().ToString("N"));
                File.Replace(temp,path,path+".previous");
            }
            else File.Move(temp,path,false);
            loadedHash=ConfigurationIntegrity.Hash(bytes);recovered=false;Status="Custom metadata saved locally; server values are unchanged.";
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static byte[] Serialize(IReadOnlyList<CustomSettingDefinition> definitions)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new CustomCatalogDocument(1,definitions.ToArray()),Json);Parse(bytes);return bytes;
    }
    public static IReadOnlyList<CustomSettingDefinition> Import(string file)=>Parse(Read(file)).Definitions;
    public static void Export(string file,IReadOnlyList<CustomSettingDefinition> definitions)
    {
        SafePath(file);
        var bytes=Serialize(definitions);using var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None);stream.Write(bytes);stream.Flush(true);
    }
    private static void SafePath(string file)
    {
        if(File.Exists(file)&&File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))throw new IOException("Linked metadata files are not supported.");
        for(var directory=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(file))!);directory!=null;directory=directory.Parent)if(directory.Exists&&directory.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("Linked metadata directories are not supported.");
    }
}
