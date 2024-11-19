using System.Buffers;
using System.IO.Pipelines;

namespace RESPite.Transports;

internal sealed class OutboundPipeBufferTransport : IByteTransport
{
    private static readonly PipeOptions __pipeOptions = new(useSynchronizationContext: false);

    private readonly IAsyncByteTransport _tail;
    private readonly Pipe _pipe;

    public OutboundPipeBufferTransport(IAsyncByteTransport tail)
    {
        _tail = tail;
        _pipe = new Pipe(__pipeOptions);
        _ = Task.Run(PushAsync);
    }

    private async Task PushAsync()
    {
        try
        {
            ReadResult readResult;
            do
            {
                readResult = await _pipe.Reader.ReadAsync().ConfigureAwait(false);
                var buffer = readResult.Buffer;
                if (!buffer.IsEmpty)
                {
                    await _tail.WriteAsync(buffer).ConfigureAwait(false);
                }
                _pipe.Reader.AdvanceTo(buffer.End, buffer.End);
            }
            while (!readResult.IsCompleted);

            _pipe.Reader.Complete();
        }
        catch (Exception ex)
        {
            _pipe.Reader.Complete(ex);
        }
    }

    public void Advance(long consumed) => _tail.Advance(consumed);
    public ValueTask DisposeAsync()
    {
        return _tail.DisposeAsync();
    }
    public void Dispose()
    {
        if (_tail is ISyncByteTransport sync)
        {
            sync.Dispose();
        }
        else
        {
            var pending = _tail.DisposeAsync();
            if (pending.IsCompleted)
            {
                pending.GetAwaiter().GetResult();
            }
            else
            {
                pending.AsTask().Wait();
            }
        }
    }

    public ReadOnlySequence<byte> GetBuffer() => _tail.GetBuffer();
    public ValueTask<bool> TryReadAsync(int hint, CancellationToken token = default) => _tail.TryReadAsync(hint, token);

    public ValueTask WriteAsync(in ReadOnlySequence<byte> buffer, CancellationToken token = default)
    {
        var pendingFlush = WriteAll(in buffer, _pipe.Writer, token);
        if (!pendingFlush.IsCompletedSuccessfully) return Awaited(pendingFlush);

        Check(pendingFlush.GetAwaiter().GetResult());
        return default;

        static async ValueTask Awaited(ValueTask<FlushResult> flush) => await flush.ConfigureAwait(false);
    }

    private static ValueTask<FlushResult> WriteAll(in ReadOnlySequence<byte> buffer, PipeWriter writer, CancellationToken token)
    {
        if (buffer.IsEmpty)
        {
            return default;
        }
        if (buffer.IsSingleSegment)
        {
            writer.Write(buffer.First.Span);
        }
        else
        {
            foreach (var chunk in buffer)
            {
                writer.Write(chunk.Span);
            }
        }
        return writer.FlushAsync(token);
    }

    bool ISyncByteTransport.TryRead(int hint)
    {
        if (_tail is ISyncByteTransport sync) return sync.TryRead(hint);

        var pending = _tail.TryReadAsync(hint);
        if (pending.IsCompleted)
        {
            return pending.GetAwaiter().GetResult();
        }
        var t = pending.AsTask();
        t.Wait();
        return t.Result;
    }

    void ISyncByteTransport.Write(in ReadOnlySequence<byte> buffer)
    {
        var pendingFlush = WriteAll(in buffer, _pipe.Writer, CancellationToken.None);
        if (pendingFlush.IsCompleted)
        {
            Check(pendingFlush.GetAwaiter().GetResult());
        }
        else
        {
            var t = pendingFlush.AsTask();
            t.Wait();
            Check(t.Result);
        }
    }

    private static void Check(FlushResult result)
    {
        if (result.IsCompleted) Throw();
        static void Throw() => throw new EndOfStreamException("The reader has completed; no response will be coming.");
    }
}
