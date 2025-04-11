using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
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

        using ZLibStream decompressedStream = new(stream, CompressionMode.Decompress);
        return LoadInternal(decompressedStream, header);
    }

    public static async ValueTask<LangFile> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[4];
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        // why tf is the file header in little endian while the rest of the file is in big
        uint header = BinaryPrimitives.ReadUInt32LittleEndian(buffer);

        using ZLibStream decompressedStream = new(stream, CompressionMode.Decompress);
        return await LoadInternalAsync(decompressedStream, header, cancellationToken);
    }

    public static LangFile Load(string filePath)
    {
        using FileStream file = File.OpenRead(filePath);
        return Load(file);
    }

    public void Save(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, Header);
        stream.Write(buffer);

        using ZLibStream compressedStream = new(stream, CompressionLevel.SmallestSize);
        SaveInternal(compressedStream);
    }

    public void Save(string filePath)
    {
        using FileStream file = File.OpenRead(filePath);
        Save(file);
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
        Span<byte> buffer = stackalloc byte[4];
        ReadOnlySpan<byte> stringBuffer;

        int entryCount = Entries.Count;
        BinaryPrimitives.WriteInt32BigEndian(buffer, entryCount);
        stream.Write(buffer);

        foreach ((string key, string text) in Entries)
        {
            stringBuffer = Encoding.UTF8.GetBytes(key);
            ushort keyLength = (ushort)stringBuffer.Length;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[..2], keyLength);
            stream.Write(buffer[..2]);
            stream.Write(stringBuffer);

            stringBuffer = Encoding.UTF8.GetBytes(text);
            ushort textLength = (ushort)stringBuffer.Length;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[..2], textLength);
            stream.Write(buffer[..2]);
            stream.Write(stringBuffer);
        }
    }
}