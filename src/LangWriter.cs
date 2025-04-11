using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BrawlhallaLangReader.Internal;

[SkipLocalsInit]
internal sealed class LangWriter : IDisposable
{
    // see LangReader for explanation of this buffer size
    private const int INITIAL_BUFFER_SIZE = 4096;
    private const int BUFFER_GROWTH_FACTOR = 2;

    private Stream _stream;
    private readonly bool _leaveOpen;

    private byte[] _buffer = null!;

    public static void WriteEntries(Stream stream, IEnumerable<(string, string)> entries)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(entries);

        IReadOnlyCollection<(string, string)> list = (entries as IReadOnlyCollection<(string, string)>) ?? [.. entries];
        using LangWriter langWriter = new(stream, true);
        langWriter.WriteEntries(list);
    }

    public static void WriteEntries(Stream stream, IEnumerable<KeyValuePair<string, string>> entries)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(entries);

        IReadOnlyCollection<KeyValuePair<string, string>> list = (entries as IReadOnlyCollection<KeyValuePair<string, string>>) ?? [.. entries];
        using LangWriter langWriter = new(stream, true);
        langWriter.WriteEntries(list);
    }

    public static ValueTask WriteEntriesAsync(Stream stream, IEnumerable<(string, string)> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(entries);

        if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled(cancellationToken);

        IReadOnlyCollection<(string, string)> list = (entries as IReadOnlyCollection<(string, string)>) ?? [.. entries];
        using LangWriter langWriter = new(stream, true);
        return langWriter.WriteEntriesAsync(list, cancellationToken);
    }

    public static ValueTask WriteEntriesAsync(Stream stream, IEnumerable<KeyValuePair<string, string>> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(entries);

        if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled(cancellationToken);

        IReadOnlyCollection<KeyValuePair<string, string>> list = (entries as IReadOnlyCollection<KeyValuePair<string, string>>) ?? [.. entries];
        using LangWriter langWriter = new(stream, true);
        return langWriter.WriteEntriesAsync(list, cancellationToken);
    }

    private LangWriter(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    private void WriteEntries(IReadOnlyCollection<(string, string)> entries)
    {
        EnsureNotDisposed();

        InitializeBuffer();
        WriteEntryCount(entries.Count);
        foreach ((string key, string text) in entries)
            WriteEntry(key, text);
    }

    private void WriteEntries(IReadOnlyCollection<KeyValuePair<string, string>> entries)
    {
        EnsureNotDisposed();

        InitializeBuffer();
        WriteEntryCount(entries.Count);
        foreach ((string key, string text) in entries)
            WriteEntry(key, text);
    }

    private async ValueTask WriteEntriesAsync(IReadOnlyCollection<(string, string)> entries, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        InitializeBuffer();
        await WriteEntryCountAsync(entries.Count, cancellationToken);
        foreach ((string key, string text) in entries)
            await WriteEntryAsync(key, text, cancellationToken);
    }

    private async ValueTask WriteEntriesAsync(IReadOnlyCollection<KeyValuePair<string, string>> entries, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        InitializeBuffer();
        await WriteEntryCountAsync(entries.Count, cancellationToken);
        foreach ((string key, string text) in entries)
            await WriteEntryAsync(key, text, cancellationToken);
    }

    private void WriteEntryCount(int entryCount)
    {
        Debug.Assert(_stream is not null);
        Debug.Assert(_buffer is not null);

        BinaryPrimitives.WriteInt32BigEndian(_buffer.AsSpan(0, 4), entryCount);
        _stream.Write(_buffer.AsSpan(0, 4));
    }

    private ValueTask WriteEntryCountAsync(int entryCount, CancellationToken cancellationToken = default)
    {
        Debug.Assert(_stream is not null);
        Debug.Assert(_buffer is not null);

        if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled(cancellationToken);

        BinaryPrimitives.WriteInt32BigEndian(_buffer.AsSpan(0, 4), entryCount);
        return _stream.WriteAsync(_buffer.AsMemory(0, 4), cancellationToken);
    }

    private void WriteEntry(string key, string text)
    {
        WriteString(key);
        WriteString(text);
    }

    private async ValueTask WriteEntryAsync(string key, string text, CancellationToken cancellationToken = default)
    {
        await WriteStringAsync(key, cancellationToken);
        await WriteStringAsync(text, cancellationToken);
    }

    private void WriteString(string str)
    {
        Debug.Assert(_stream is not null);
        Debug.Assert(_buffer is not null);

        int byteCount = Encoding.UTF8.GetByteCount(str);
        if (byteCount > ushort.MaxValue)
            throw new OverflowException("Given string's length exceeds uint16 max");

        BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(0, 2), (ushort)byteCount);
        _stream.Write(_buffer.AsSpan(0, 2));

        ResizeBufferToFit((ushort)byteCount);
        Encoding.UTF8.GetBytes(str, _buffer);
        _stream.Write(_buffer.AsSpan(0, byteCount));
    }

    private async ValueTask WriteStringAsync(string str, CancellationToken cancellationToken = default)
    {
        Debug.Assert(_stream is not null);
        Debug.Assert(_buffer is not null);

        int byteCount = Encoding.UTF8.GetByteCount(str);
        if (byteCount > ushort.MaxValue)
            throw new OverflowException("Given string's length exceeds uint16 max");

        BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(0, 2), (ushort)byteCount);
        await _stream.WriteAsync(_buffer.AsMemory(0, 2), cancellationToken);

        ResizeBufferToFit((ushort)byteCount);
        Encoding.UTF8.GetBytes(str, _buffer);
        await _stream.WriteAsync(_buffer.AsMemory(0, byteCount), cancellationToken);
    }

    private void Dispose(bool disposing)
    {
        if (_stream is not null)
        {
            try
            {
                if (disposing && !_leaveOpen)
                    _stream.Dispose();
            }
            finally
            {
                _stream = null!;
                _buffer = null!;
            }
        }
    }

    ~LangWriter()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void InitializeBuffer()
    {
        _buffer ??= GC.AllocateUninitializedArray<byte>(INITIAL_BUFFER_SIZE);
    }

    // it's possible to avoid doing this by properly decoding the string like in BinaryReader.
    // but that's annoying to implement. and the strings we're dealing with are never that long.
    private void ResizeBufferToFit(ushort length)
    {
        Debug.Assert(_buffer is not null);

        if (_buffer.Length >= length) return;

        int newLength = _buffer.Length;
        while (length > newLength)
            newLength *= BUFFER_GROWTH_FACTOR;
        _buffer = GC.AllocateUninitializedArray<byte>(newLength);
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_stream is null, this);
    }
}