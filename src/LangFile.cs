using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

namespace BrawlhallaLangReader;

public sealed class LangFile
{
    public required uint Header { get; set; }
    public required Dictionary<string, string> Entries { get; set; }

    public static LangFile Load(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        // why tf is the file header in little endian while the rest of the file is in big
        uint header = BinaryPrimitives.ReadUInt32LittleEndian(buffer);

        using ZLibStream decompressedStream = new(stream, CompressionMode.Decompress);
        return LoadInternal(decompressedStream, header);
    }

    public void Save(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, Header);
        stream.Write(buffer);

        using ZLibStream compressedStream = new(stream, CompressionLevel.SmallestSize);
        SaveInternal(compressedStream);
    }

    [SkipLocalsInit]
    private static LangFile LoadInternal(Stream stream, uint header)
    {
        Span<byte> buffer = stackalloc byte[1024];
        Span<byte> stringBuffer = buffer; // starts small, sharing the buffer, and resizes if needed

        stream.ReadExactly(buffer[..4]);
        int entryCount = BinaryPrimitives.ReadInt32BigEndian(buffer[..4]);

        Dictionary<string, string> entries = [];
        for (int i = 0; i < entryCount; ++i)
        {
            // key
            stream.ReadExactly(buffer[..2]);
            ushort keyLength = BinaryPrimitives.ReadUInt16BigEndian(buffer[..2]);
            if (keyLength > stringBuffer.Length)
                stringBuffer = GC.AllocateUninitializedArray<byte>(keyLength);
            stream.ReadExactly(stringBuffer[..keyLength]);
            string key = Encoding.UTF8.GetString(stringBuffer[..keyLength]);
            // text
            stream.ReadExactly(buffer[..2]);
            ushort textLength = BinaryPrimitives.ReadUInt16BigEndian(buffer[..2]);
            if (textLength > stringBuffer.Length)
                stringBuffer = GC.AllocateUninitializedArray<byte>(textLength);
            stream.ReadExactly(stringBuffer[..textLength]);
            string text = Encoding.UTF8.GetString(stringBuffer[..textLength]);

            entries[key] = text;
        }

        return new()
        {
            Header = header,
            Entries = entries,
        };
    }

    [SkipLocalsInit]
    private void SaveInternal(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        byte[] stringBuffer;

        int entryCount = Entries.Count;
        BinaryPrimitives.WriteInt32BigEndian(buffer[..4], entryCount);
        stream.Write(buffer[..4]);

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