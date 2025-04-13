using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BrawlhallaLangReader.Internal;

namespace BrawlhallaLangReader;

[SkipLocalsInit]
public sealed class LangFile
{
    public required uint Header { get; set; }
    public required Dictionary<string, string> Entries { get; set; }

    public LangFile() { }

    public LangFile(uint header, Dictionary<string, string> entries)
    {
        Header = header;
        Entries = entries;
    }

    public static LangFile Load(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        // why tf is the file header in little endian while the rest of the file is in big
        uint header = BinaryPrimitives.ReadUInt32LittleEndian(buffer);

        using ZLibStream decompressedStream = new(stream, CompressionMode.Decompress, true);
        return LoadInternal(decompressedStream, header);
    }

    public static async ValueTask<LangFile> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[4];
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        // why tf is the file header in little endian while the rest of the file is in big
        uint header = BinaryPrimitives.ReadUInt32LittleEndian(buffer);

        using ZLibStream decompressedStream = new(stream, CompressionMode.Decompress, true);
        return await LoadInternalAsync(decompressedStream, header, cancellationToken);
    }

    public static LangFile Load(string filePath)
    {
        using FileStream file = File.OpenRead(filePath);
        return Load(file);
    }

    public static ValueTask<LangFile> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<LangFile>(cancellationToken);
        return Core();
        async ValueTask<LangFile> Core()
        {
            using FileStream file = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await LoadAsync(file, cancellationToken);
        }
    }

    public void Save(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, Header);
        stream.Write(buffer);

        using ZLibStream compressedStream = new(stream, CompressionLevel.SmallestSize, true);
        SaveInternal(compressedStream);
    }

    public async ValueTask SaveAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, Header);
        await stream.WriteAsync(buffer, cancellationToken);

        using ZLibStream compressedStream = new(stream, CompressionLevel.SmallestSize, true);
        await SaveInternalAsync(compressedStream, cancellationToken);
    }

    public void Save(string filePath)
    {
        using FileStream file = new(filePath, FileMode.Create, FileAccess.Write);
        Save(file);
    }

    public ValueTask SaveAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);
        return Core();
        async ValueTask Core()
        {
            using FileStream file = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await SaveAsync(file, cancellationToken);
        }
    }

    private static LangFile LoadInternal(Stream stream, uint header)
    {
        Dictionary<string, string> entries = [];
        using (LangReader langReader = new(stream, true))
        {
            foreach ((string key, string text) in langReader.ReadEntries())
                entries[key] = text;
        }

        return new()
        {
            Header = header,
            Entries = entries,
        };
    }

    private static async ValueTask<LangFile> LoadInternalAsync(Stream stream, uint header, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> entries = [];
        using (LangReader langReader = new(stream, true))
        {
            await foreach ((string key, string text) in langReader.ReadEntriesAsync(cancellationToken))
                entries[key] = text;
        }

        return new()
        {
            Header = header,
            Entries = entries,
        };
    }

    private void SaveInternal(Stream stream)
    {
        LangWriter.WriteEntries(stream, Entries);
    }

    private ValueTask SaveInternalAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        return LangWriter.WriteEntriesAsync(stream, Entries, cancellationToken);
    }
}