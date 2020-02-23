using Respite;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Respite.Redis
{
    public class RedisConnection : IDisposable
    {
        private readonly RespConnection _connection;

        public RedisConnection(RespConnection connection)
            => _connection = connection;

        public void Dispose() => _connection.Dispose();

#pragma warning disable IDE0060 // Remove unused parameter
        public static async ValueTask<RedisConnection> ConnectAsync(EndPoint endpoint, CancellationToken cancellationToken = default)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            SetRecommendedClientOptions(socket);
            await socket.ConnectAsync(endpoint).ConfigureAwait(false);
            return new RedisConnection(RespConnection.Create(socket));
        }

        public async ValueTask<object> CallAsync(string command, params object[] args)
        {
            using var lease = Lifetime.RentMemory<RespValue>(args.Length + 1);
            var cmd = Populate(command, args, lease.Value, out var cancel);
            await _connection.SendAsync(cmd, cancel).ConfigureAwait(false);
            using var response = await _connection.ReceiveAsync(cancel).ConfigureAwait(false);
            return ToObject(response.Value);
        }


        static RespValue Populate(string command, object[] args, Memory<RespValue> lease, out CancellationToken cancellationToken)
        {
            cancellationToken = default;
            int index = 0;
            var buffer = lease.Span;
            buffer[index++] = command;
            for (int i = 0; i < args.Length; i++)
            {
                var val = args[i];
                if (val is CancellationToken cancel)
                {
                    if (cancel.CanBeCanceled) cancellationToken = cancel;
                }
                else
                {
                    buffer[index++] = ToBlobString(val);
                }
            }
            return RespValue.CreateAggregate(RespType.Array,
                index == buffer.Length ? lease : lease.Slice(0, index));
        }

        static RespValue ToBlobString(object value)
         => value switch
            {
                null => RespValue.Create(RespType.BlobString, (string)null),
                string s => RespValue.Create(RespType.BlobString, s),
                int i => RespValue.Create(RespType.BlobString, (long)i),
                long l => RespValue.Create(RespType.BlobString, l),
                double d => RespValue.Create(RespType.BlobString, d),
                float f => RespValue.Create(RespType.BlobString, (double)f),
                byte[] blob => RespValue.Create(RespType.BlobString, new ReadOnlySequence<byte>(blob)),
                _ => throw new ArgumentException(nameof(value)),
            };

        static object ToObject(in RespValue value)
        {
            switch (value.Type)
            {
                case RespType.BlobString:
                case RespType.SimpleString:
                case RespType.VerbatimString:
                    return value.ToString();
                case RespType.Null:
                    return null;
                case RespType.SimpleError:
                case RespType.BlobError:
                    value.ThrowIfError();
                    return default;
                case RespType.Number:
                    return value.ToInt64();
                case RespType.BigNumber:
                    return BigInteger.Parse(value.ToString(), NumberFormatInfo.InvariantInfo);
                case RespType.Boolean:
                    return value.ToBoolean();
                case RespType.Array:
                    return ToArray(value.SubItems);
                case RespType.Set:
                    return ToSet(value.SubItems);
                case RespType.Map:
                    return ToDictionary(value.SubItems);
                default:
                    throw new NotImplementedException(value.Type.ToString());
            }

            static object[] ToArray(in ReadOnlyBlock<RespValue> values)
            {
                if (values.IsEmpty) return Array.Empty<object>();
                var arr = new object[values.Count];
                int index = 0;
                foreach (var value in values)
                    arr[index++] = ToObject(in value);
                return arr;
            }

            static ISet<object> ToSet(in ReadOnlyBlock<RespValue> values)
            {
                var set = new HashSet<object>(values.Count);
                foreach (var value in values)
                    set.Add(value);
                return set;
            }

            static IDictionary<object, object> ToDictionary(in ReadOnlyBlock<RespValue> values)
            {
                var map = new Dictionary<object, object>(values.Count / 2);
                var iter = values.GetEnumerator();
                while (iter.MoveNext())
                {
                    var key = ToObject(iter.Current);
                    if (!iter.MoveNext()) throw new InvalidOperationException();
                    map.Add(key, ToObject(iter.Current));
                }
                return map;
            }
        }

        

        private static void SetRecommendedClientOptions(Socket socket)
        {
            if (socket.AddressFamily == AddressFamily.Unix) return;

            try { socket.NoDelay = true; } catch { }

            try { SetFastLoopbackOption(socket); } catch { }
        }

        internal static void SetFastLoopbackOption(Socket socket)
        {
            // SIO_LOOPBACK_FAST_PATH (https://msdn.microsoft.com/en-us/library/windows/desktop/jj841212%28v=vs.85%29.aspx)
            // Speeds up localhost operations significantly. OK to apply to a socket that will not be hooked up to localhost,
            // or will be subject to WFP filtering.
            const int SIO_LOOPBACK_FAST_PATH = -1744830448;

            // windows only
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // Win8/Server2012+ only
                var osVersion = Environment.OSVersion.Version;
                if (osVersion.Major > 6 || (osVersion.Major == 6 && osVersion.Minor >= 2))
                {
                    byte[] optionInValue = BitConverter.GetBytes(1);
                    socket.IOControl(SIO_LOOPBACK_FAST_PATH, optionInValue, null);
                }
            }
        }
    }
}
