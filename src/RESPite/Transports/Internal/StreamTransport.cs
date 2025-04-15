using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using RESPite.Internal;
using RESPite.Internal.Buffers;
using RESPite.Transports;

namespace RESPite.Gateways.Internal;

internal sealed class StreamTransport : IByteTransport
{
    private readonly Stream _source, _target;
    private readonly bool _closeStreams;
    private readonly Action<string>? _debugLog;
    private readonly bool _autoFlush;
    private BufferCore<byte> _buffer;

    internal StreamTransport(Stream duplex, bool closeStreams, bool autoFlush = false, Action<string>? debugLog = null) : this(duplex, duplex, closeStreams, autoFlush, debugLog)
    {
    }
    internal StreamTransport(Stream source, Stream target, bool closeStreams, bool autoFlush = false, Action<string>? debugLog = null)
    {
        _debugLog = debugLog;
        _autoFlush = autoFlush;
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (!source.CanRead) throw new ArgumentException("Source must allow read", nameof(source));
        if (!target.CanWrite) throw new ArgumentException("Target must allow read", nameof(target));
        _buffer = new(new SlabManager<byte>());
        _source = source;
        _target = target;
        _closeStreams = closeStreams;
    }

    ReadOnlySequence<byte> IByteTransportBase.GetBuffer() => _buffer.GetBuffer();

#if NETCOREAPP3_1_OR_GREATER
    public ValueTask DisposeAsync()
    {
        _buffer.Dispose();
        _buffer.SlabManager.Dispose();
        if (_closeStreams)
        {
            var pending = _source.DisposeAsync();
            if (ReferenceEquals(_source, _target)) return pending;
            if (!pending.IsCompletedSuccessfully) return Awaited(pending, _target);
            pending.GetAwaiter().GetResult();
            return _target.DisposeAsync();
        }
        return default;

        static async ValueTask Awaited(ValueTask pending, Stream target)
        {
            await pending.ConfigureAwait(false);
            await target.DisposeAsync();
        }
    }
#else
    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
#endif

    public void Dispose()
    {
        _buffer.Dispose();
        _buffer.SlabManager.Dispose();
        if (_closeStreams)
        {
            _source.Dispose();
            if (!ReferenceEquals(_source, _target)) _target.Dispose();
        }
    }

    public ValueTask<bool> TryReadAsync(int hint, CancellationToken cancellationToken)
    {
        var readBuffer = _buffer.GetWritableTail();
        Debug.Assert(!readBuffer.IsEmpty, "should have space");

        _debugLog?.Invoke($"[RawReadAsync] reading up to {readBuffer.Length} bytes (async)...");
        var pending = _source.ReadAsync(readBuffer, cancellationToken);
        if (!pending.IsCompletedSuccessfully) return Awaited(this, pending);

        // synchronous happy case
        var bytes = pending.GetAwaiter().GetResult();
        _debugLog?.Invoke($"[RawReadAsync] read complete (sync); {bytes} bytes");
        if (bytes > 0)
        {
            _buffer.Commit(bytes);
            return new(true);
        }
        return default;

#if NET6_0_OR_GREATER
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        static async ValueTask<bool> Awaited(StreamTransport @this, ValueTask<int> pending)
        {
            try
            {
                var bytes = await pending.ConfigureAwait(false);
                @this._debugLog?.Invoke($"[RawReadAsync] read complete (async); {bytes} bytes");
                if (bytes > 0)
                {
                    @this._buffer.Commit(bytes);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                @this._debugLog?.Invoke($"[RawReadAsync] read failure: {ex.Message}");
                throw;
            }
        }
    }

    public bool TryRead(int hint)
    {
        var readBuffer = _buffer.GetWritableTail();
        Debug.Assert(!readBuffer.IsEmpty, "should have space");
        var bytes = _source.Read(readBuffer);

        if (bytes > 0)
        {
            _buffer.Commit(bytes);
            return true;
        }
        return false;
    }

    public void Advance(long bytes) => _buffer.Advance(bytes);

    void ISyncByteTransport.Write(in ReadOnlySequence<byte> buffer)
    {
        if (buffer.IsSingleSegment)
        {
            _target.Write(buffer.First);
            if (_autoFlush) ((ISyncByteTransport)this).Flush();
        }
        else
        {
            WriteMultiSegment(this, buffer);

            static void WriteMultiSegment(StreamTransport @this, in ReadOnlySequence<byte> buffer)
            {
                foreach (var segment in buffer)
                {
                    @this._target.Write(segment);
                }
                if (@this._autoFlush) ((ISyncByteTransport)@this).Flush();
            }
        }
    }

    ValueTask IAsyncByteTransport.WriteAsync(in ReadOnlySequence<byte> buffer, CancellationToken token)
    {
        if (buffer.IsSingleSegment)
        {
            if (_debugLog is null & !_autoFlush)
            {
                return _target.WriteAsync(buffer.First, token);
            }
            else
            {
                return WriteSingleSegment(this, buffer.First, token);
            }

            static async ValueTask WriteSingleSegment(StreamTransport @this, ReadOnlyMemory<byte> buffer, CancellationToken token)
            {
                @this._debugLog?.Invoke($"[RawSendAsync] writing {buffer.Length} bytes...");
                try
                {
                    var pending = @this._target.WriteAsync(buffer, token);
                    var sync = pending.IsCompleted;
                    await pending.ConfigureAwait(false);
                    @this._debugLog?.Invoke($"[RawSendAsync] write complete ({(sync ? "sync" : "async")})");

                    if (@this._autoFlush) await ((IAsyncByteTransport)@this).FlushAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    @this._debugLog?.Invoke($"[RawSendAsync] write error: {ex.Message}");
                    throw;
                }
            }
        }
        else
        {
            return WriteMultiSegment(this, buffer, token);

            static async ValueTask WriteMultiSegment(StreamTransport @this, ReadOnlySequence<byte> buffer, CancellationToken token)
            {
                try
                {
                    foreach (var segment in buffer)
                    {
                        @this._debugLog?.Invoke($"[RawSendAsync] writing (multi) {buffer.Length} bytes...");
                        await @this._target.WriteAsync(segment, token).ConfigureAwait(false);
                    }
                    @this._debugLog?.Invoke($"[RawSendAsync] write (multi) complete");
                }
                catch (Exception ex)
                {
                    @this._debugLog?.Invoke($"[RawSendAsync] write error: {ex.Message}");
                    throw;
                }
                if (@this._autoFlush) await ((IAsyncByteTransport)@this).FlushAsync(token).ConfigureAwait(false);
            }
        }
    }

    void ISyncByteTransport.Flush()
    {
        if (_debugLog is null)
        {
            _target.Flush();
        }
        else
        {
            _debugLog.Invoke($"[RawFlush] flushing (sync)...");
            try
            {
                _target.Flush();
                _debugLog.Invoke($"[RawFlush] flushed");
            }
            catch (Exception ex)
            {
                _debugLog.Invoke($"[RawFlush] flush error: {ex.Message}");
                throw;
            }
        }
    }

    ValueTask IAsyncByteTransport.FlushAsync(CancellationToken token)
    {
        if (_debugLog is null)
        {
            return new(_target.FlushAsync(token));
        }
        else
        {
            return FlushLogged(this, token);
        }
        static async ValueTask FlushLogged(StreamTransport @this, CancellationToken token)
        {
            @this._debugLog!.Invoke($"[RawFlush] flushing (async)...");
            try
            {
                await @this._target.FlushAsync(token).ConfigureAwait(false);
                @this._debugLog.Invoke($"[RawFlush] flushed");
            }
            catch (Exception ex)
            {
                @this._debugLog.Invoke($"[RawFlush] flush error: {ex.Message}");
                throw;
            }
        }
    }
}
