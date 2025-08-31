using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Readers;
using SharpCompress.Common;

namespace TVPPdfConverter.Services.Discovery;

public sealed class PdfDiscoveryService
{
    private readonly DiscoveryOptions _defaults;

    public PdfDiscoveryService(DiscoveryOptions? defaults = null)
    {
        _defaults = defaults ?? new DiscoveryOptions();
    }

    private sealed record Container(string Kind, string Display, Func<CancellationToken, Task<Stream>> Open, int Depth, string? TempPath);

    public async IAsyncEnumerable<string> DiscoverTempPdfFilesFromZipStreamAsync(
        Stream rootZip,
        DiscoveryOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opt = options ?? _defaults;
        var containers = new Stack<Container>();
        var tempContainerFiles = new List<string>();
        if (!rootZip.CanSeek)
        {
            var tmpRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            await using (var fs = File.Create(tmpRoot))
            {
                await rootZip.CopyToAsync(fs, cancellationToken);
            }
            tempContainerFiles.Add(tmpRoot);
            containers.Push(new Container("zip", "root.zip-stream", ct => Task.FromResult<Stream>(File.Open(tmpRoot, FileMode.Open, FileAccess.Read, FileShare.Read)), 0, tmpRoot));
        }
        else
        {
            containers.Push(new Container("zip", "root.zip-stream", ct => Task.FromResult(rootZip), 0, null));
        }

        int entries = 0;
        long totalUncompressed = 0;

        // temp containers created for nested items to delete afterwards

        try
        {
            while (containers.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var container = containers.Pop();

                await using var stream = await container.Open(cancellationToken);
                if (container.Kind == "zip")
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
                    foreach (var entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (string.IsNullOrEmpty(entry.Name))
                            continue; // directory

                        entries++;
                        if (entries > opt.MaxEntries) yield break;

                        var ext = Path.GetExtension(entry.Name).ToLowerInvariant();

                        long uncompressedSize = entry.Length;
                        if (uncompressedSize < 0) uncompressedSize = 0; // metadata may be missing
                        if (uncompressedSize > 0)
                        {
                            // Cap per-entry
                            if (uncompressedSize > opt.MaxEntryUncompressedBytes) continue;
                            // Track total
                            totalUncompressed += uncompressedSize;
                            if (totalUncompressed > opt.MaxUncompressedBytes) yield break;
                        }

                        if (ext == ".pdf")
                        {
                            var tmpPdf = Path.GetTempFileName();
                            await using (var es = entry.Open())
                            await using (var fs = File.Create(tmpPdf))
                            {
                                await CopyBoundedAsync(es, fs, opt.MaxEntryUncompressedBytes, cancellationToken);
                            }
                            yield return tmpPdf;
                        }
                        else if ((ext == ".zip" && opt.AllowZip) || (ext == ".rar" && opt.AllowRar))
                        {
                            if (container.Depth + 1 > opt.MaxDepth) continue;
                            // Materialize inner archive to temp file to ensure seekable stream and isolation
                            var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                            await using (var es = entry.Open())
                            await using (var fs = File.Create(tmp))
                            {
                                await CopyBoundedAsync(es, fs, opt.MaxEntryUncompressedBytes, cancellationToken);
                            }

                            tempContainerFiles.Add(tmp);
                            var kind = ext.TrimStart('.').ToLowerInvariant();
                            containers.Push(new Container(kind, entry.FullName, async ct =>
                            {
                                return File.Open(tmp, FileMode.Open, FileAccess.Read, FileShare.Read);
                            }, container.Depth + 1, tmp));
                        }
                    }
                }
                else if (container.Kind is "rar")
                {
                    // Use SharpCompress Reader for streaming RAR
                    using var reader = ReaderFactory.Open(stream);
                    while (reader.MoveToNextEntry())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = reader.Entry;
                        if (entry.IsDirectory) continue;
                        entries++;
                        if (entries > opt.MaxEntries) yield break;

                        var ext = Path.GetExtension(entry.Key).ToLowerInvariant();
                        var uncompressedSize = entry.Size;

                        if (uncompressedSize > opt.MaxEntryUncompressedBytes) continue;
                        if (uncompressedSize > 0)
                        {
                            totalUncompressed += uncompressedSize;
                            if (totalUncompressed > opt.MaxUncompressedBytes) yield break;
                        }

                        if (ext == ".pdf")
                        {
                            var tmpPdf = Path.GetTempFileName();
                            await using (var fs = File.Create(tmpPdf))
                            {
                                reader.WriteEntryTo(fs);
                            }
                            yield return tmpPdf;
                        }
                        else if ((ext == ".zip" && opt.AllowZip) || (ext == ".rar" && opt.AllowRar))
                        {
                            if (container.Depth + 1 > opt.MaxDepth) continue;

                            // Materialize nested archive to temp file
                            var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                            await using (var fs = File.Create(tmp))
                            {
                                reader.WriteEntryTo(fs);
                            }
                            tempContainerFiles.Add(tmp);
                            var kind = ext.TrimStart('.').ToLowerInvariant();
                            containers.Push(new Container(kind, entry.Key, async ct =>
                            {
                                return File.Open(tmp, FileMode.Open, FileAccess.Read, FileShare.Read);
                            }, container.Depth + 1, tmp));
                        }
                    }
                }
            }
        }
        finally
        {
            // Cleanup temp nested container files
            foreach (var f in tempContainerFiles)
            {
                try { File.Delete(f); } catch { /* ignore */ }
            }
        }
    }

    public async IAsyncEnumerable<DiscoveredPdf> DiscoverPdfsFromPathAsync(
        string rootPath,
        DiscoveryOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opt = options ?? _defaults;
        var dirs = new Stack<(string path, int depth)>();
        var files = new Stack<(string path, int depth)>();

        if (Directory.Exists(rootPath))
        {
            dirs.Push((rootPath, 0));
        }
        else if (File.Exists(rootPath))
        {
            files.Push((rootPath, 0));
        }
        else
        {
            yield break;
        }

        int entries = 0;
        long totalUncompressed = 0;

        while (dirs.Count > 0 || files.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dirs.Count > 0)
            {
                var (dir, depth) = dirs.Pop();
                try
                {
                    foreach (var d in Directory.EnumerateDirectories(dir))
                    {
                        if (depth + 1 <= opt.MaxDepth) dirs.Push((d, depth + 1));
                    }
                    foreach (var f in Directory.EnumerateFiles(dir))
                    {
                        files.Push((f, depth));
                    }
                }
                catch { /* ignore permission issues */ }
            }
            else
            {
                var (path, depth) = files.Pop();
                entries++;
                if (entries > opt.MaxEntries) yield break;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".pdf")
                {
                    yield return new DiscoveredPdf(path, IsTemp: false);
                }
                else if ((ext == ".zip" && opt.AllowZip) || (ext == ".rar" && opt.AllowRar))
                {
                    if (depth + 1 > opt.MaxDepth) continue;
                    await using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

                    IAsyncEnumerable<string> enumerator = ext == ".zip"
                        ? DiscoverTempPdfFilesFromZipStreamAsync(fs, opt, cancellationToken)
                        : DiscoverTempPdfFilesFromRarStreamAsync(fs, opt, cancellationToken);

                    var temps = new List<string>();
                    try
                    {
                        await foreach (var tmp in enumerator)
                        {
                            temps.Add(tmp);
                        }
                    }
                    catch
                    {
                        // ignore invalid/corrupt archives
                    }

                    foreach (var tmp in temps)
                    {
                        yield return new DiscoveredPdf(tmp, IsTemp: true);
                    }
                }
            }
        }
    }

    private async IAsyncEnumerable<string> DiscoverTempPdfFilesFromRarStreamAsync(
        Stream rootRar,
        DiscoveryOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opt = options ?? _defaults;
        var containers = new Stack<Container>();
        var tempContainerFiles = new List<string>();
        if (!rootRar.CanSeek)
        {
            var tmpRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            await using (var fs = File.Create(tmpRoot))
            {
                await rootRar.CopyToAsync(fs, cancellationToken);
            }
            tempContainerFiles.Add(tmpRoot);
            containers.Push(new Container("rar", "root.rar-stream", ct => Task.FromResult<Stream>(File.Open(tmpRoot, FileMode.Open, FileAccess.Read, FileShare.Read)), 0, tmpRoot));
        }
        else
        {
            containers.Push(new Container("rar", "root.rar-stream", ct => Task.FromResult(rootRar), 0, null));
        }

        int entries = 0;
        long totalUncompressed = 0;
        try
        {
            while (containers.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var container = containers.Pop();
                await using var stream = await container.Open(cancellationToken);
                if (container.Kind is "rar")
                {
                    using var reader = ReaderFactory.Open(stream);
                    while (reader.MoveToNextEntry())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = reader.Entry;
                        if (entry.IsDirectory) continue;
                        entries++;
                        if (entries > opt.MaxEntries) yield break;
                        var ext = Path.GetExtension(entry.Key).ToLowerInvariant();
                        var uncompressedSize = entry.Size;
                        if (uncompressedSize > opt.MaxEntryUncompressedBytes) continue;
                        if (uncompressedSize > 0)
                        {
                            totalUncompressed += uncompressedSize;
                            if (totalUncompressed > opt.MaxUncompressedBytes) yield break;
                        }
                        if (ext == ".pdf")
                        {
                            var tmpPdf = Path.GetTempFileName();
                            await using (var fs = File.Create(tmpPdf))
                            {
                                reader.WriteEntryTo(fs);
                            }
                            yield return tmpPdf;
                        }
                        else if ((ext == ".zip" && opt.AllowZip) || (ext == ".rar" && opt.AllowRar))
                        {
                            if (container.Depth + 1 > opt.MaxDepth) continue;
                            var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                            await using (var fs = File.Create(tmp))
                            {
                                reader.WriteEntryTo(fs);
                            }
                            tempContainerFiles.Add(tmp);
                            var kind = ext.TrimStart('.').ToLowerInvariant();
                            containers.Push(new Container(kind, entry.Key, async ct =>
                            {
                                return File.Open(tmp, FileMode.Open, FileAccess.Read, FileShare.Read);
                            }, container.Depth + 1, tmp));
                        }
                    }
                }
            }
        }
        finally
        {
            foreach (var f in tempContainerFiles)
            {
                try { File.Delete(f); } catch { }
            }
        }
    }

    private static async Task CopyBoundedAsync(Stream src, Stream dst, long maxBytes, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long total = 0;
            int read;
            while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                total += read;
                if (total > maxBytes) throw new InvalidDataException("Entry exceeds size limit");
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Stream MakeSeekable(Stream s)
    {
        if (s.CanSeek) return s;
        var ms = new MemoryStream();
        s.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }
}
