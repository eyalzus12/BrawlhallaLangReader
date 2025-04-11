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
internal sealed class LangReader : IDisposable
{
    /*
    Max string size (by bytes) for each language, patch 9.05:
    English - 1722
    ChineseTraditional - 1920
    ChineseSimplified - 1806
    French - 2781
    German - 2229
    Italian - 2044
    Japanese - 2547
    Portuguese - 2019
    Russian - 3359
    Spanish - 2038
    Korean - 2362
    Turkish - 1929
    Spanish - 2038

    So a buffer size of 4096 will never have to resize.
    If we only need to read english, 2048 is enough.
    But an extra 2KiB of memory is not a big deal.
    */

    private const int INITIAL_BUFFER_SIZE = 4096;
    private const int BUFFER_GROWTH_FACTOR = 2;

    private Stream _stream;
    private readonly bool _leaveOpen;

    private int _entriesRead = 0;
    private int _entryCount = -1;
    private byte[] _buffer = null!;

    public LangReader(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    public IEnumerable<(string, string)> ReadEntries()
    {
        EnsureNotDisposed();
        ReadEntryCount();
        while (_entriesRead < _entryCount)
            yield return ReadEntryCore();
    }

    public async IAsyncEnumerable<(string, string)> ReadEntriesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        await ReadEntryCountAsync(cancellationToken);
        while (_entriesRead < _entryCount)
            yield return await ReadEntryCoreAsync(cancellationToken);
    }

    public (string, string)? ReadEntry()
    {
        EnsureNotDisposed();
        ReadEntryCount();
        if (_entriesRead >= _entryCount) return null;
        return ReadEntryCore();
    }

    public async ValueTask<(string, string)?> ReadEntryAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        await ReadEntryCountAsync(cancellationToken);
        if (_entriesRead >= _entryCount) return null;
        return await ReadEntryCoreAsync(cancellationToken);
    }

    private (string, string) ReadEntryCore()
    {
        Debug.Assert(_buffer is not null);
        Debug.Assert(_stream is not null);
        Debug.Assert(_entriesRead < _entryCount);

        string key = ReadString();
        string text = ReadString();
        _entriesRead++;

        return (key, text);
    }

    private async ValueTask<(string, string)> ReadEntryCoreAsync(CancellationToken cancellationToken = default)
    {
        Debug.Assert(_buffer is not null);
        Debug.Assert(_stream is not null);
        Debug.Assert(_entriesRead < _entryCount);

        string key = await ReadStringAsync(cancellationToken);
        string text = await ReadStringAsync(cancellationToken);
        _entriesRead++;

        return (key, text);
    }

    private string ReadString()
    {
        Debug.Assert(_buffer is not null);
        Debug.Assert(_stream is not null);

        _stream.ReadExactly(_buffer.AsSpan(0, 2));
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(_buffer.AsSpan(0, 2));

        ResizeBufferToFit(length);
        _stream.ReadExactly(_buffer.AsSpan(0, length));
        return Encoding.UTF8.GetString(_buffer.AsSpan(0, length));
    }

    private async ValueTask<string> ReadStringAsync(CancellationToken cancellationToken = default)
    {
        Debug.Assert(_buffer is not null);
        Debug.Assert(_stream is not null);

        await _stream.ReadExactlyAsync(_buffer.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(_buffer.AsSpan(0, 2));

        ResizeBufferToFit(length);
        await _stream.ReadExactlyAsync(_buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(_buffer.AsSpan(0, length));
    }

    private void ReadEntryCount()
    {
        Debug.Assert(_stream is not null);

        if (_entryCount == -1)
        {
            InitializeBuffer();
            _stream.ReadExactly(_buffer.AsSpan(0, 4));
            _entryCount = BinaryPrimitives.ReadInt32BigEndian(_buffer.AsSpan(0, 4));
        }
    }

    private async ValueTask ReadEntryCountAsync(CancellationToken cancellationToken = default)
    {
        Debug.Assert(_stream is not null);

        if (_entryCount == -1)
        {
            InitializeBuffer();
            await _stream.ReadExactlyAsync(_buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            _entryCount = BinaryPrimitives.ReadInt32BigEndian(_buffer.AsSpan(0, 4));
        }
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

    ~LangReader()
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