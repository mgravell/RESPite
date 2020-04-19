using System;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Respite.Internal
{
    internal static class ThrowHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ArgumentNull(string paramName)
            => throw new ArgumentNullException(paramName);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ArgumentOutOfRange(string paramName, string? message = null)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentOutOfRangeException(paramName);
            throw new ArgumentOutOfRangeException(paramName, message);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void RespTypeNotImplemented(RespType type, [CallerMemberName] string? caller = null)
            => throw new NotImplementedException($"RESP type not implemented by '{caller}': {type}");

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void NotImplemented([CallerMemberName] string? message = null)
            => throw new NotImplementedException(message);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void UnknownSequenceVariety()
            => throw new NotSupportedException("The ReadOnlySequence variety was not understood");

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void NotSupported(string message)
            => throw new NotSupportedException(message);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void StorageKindNotImplemented(StorageKind storage, [CallerMemberName] string? caller = null)
            => throw new NotImplementedException($"Storage kind not implemented by '{caller}': {storage}");

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Disposed(string objectName)
            => throw new ObjectDisposedException(objectName);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Argument(string message, string paramName)
            => throw new ArgumentException(message, paramName);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Argument(string paramName) => Argument("Invalid value", paramName);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Socket(SocketError socketError)
            => throw new SocketException((int)socketError);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ExpectedNewLine(byte value)
            => throw new InvalidOperationException($"Protocol parsing error; expected newline; got '{(char)value}'");

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Format() => throw new FormatException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Invalid(string message)
            => throw new InvalidOperationException(message);

        internal static void ThrowArgumentOutOfRangeException_OffsetOutOfRange()
            => ArgumentOutOfRange("offset");
        internal static void ThrowArgumentOutOfRangeException(string name)
            => ArgumentOutOfRange(name);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Timeout()
            => throw new TimeoutException();
    }
    internal static class ExceptionArgument
    {
        internal const string count = nameof(count);
    }
}
