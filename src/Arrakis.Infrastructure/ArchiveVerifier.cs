using System.Formats.Tar;
using System.IO.Compression;

namespace Arrakis.Infrastructure;

public static class ArchiveVerifier
{
    public static async Task VerifyLogArchiveAsync(string path,CancellationToken token)
    {
        try
        {
            await using var file=File.OpenRead(path);await using var gzip=new GZipStream(file,CompressionMode.Decompress);using var tar=new TarReader(gzip,true);var hasContent=false;
            while(await tar.GetNextEntryAsync(false,token) is {} entry)if(entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile && entry.Length>0)hasContent=true;
            await gzip.CopyToAsync(Stream.Null,token);if(!hasContent)throw new InvalidDataException("Official export archive contains no nonempty log files.");
        }
        catch(EndOfStreamException e){throw new InvalidDataException("Official export archive is empty, truncated or incomplete.",e);}
    }
}
