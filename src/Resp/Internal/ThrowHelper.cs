﻿using System;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Resp.Internal
{
    internal static class ThrowHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ArgumentNull(string paramName)
            => throw new ArgumentNullException(paramName);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ArgumentOutOfRange(string paramName)
            => throw new ArgumentOutOfRangeException(paramName);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void FrameTypeNotImplemented(RespType type)
            => throw new NotImplementedException($"Frame type not implemented: {type}");

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void NotImplemented([CallerMemberName] string message = null)
            => throw new NotImplementedException(message);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void UnknownSequenceVariety()
            => throw new NotSupportedException("The ReadOnlySequence variety was not understood");

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void FrameStorageKindNotImplemented(FrameStorageKind storage)
            => throw new NotImplementedException($"Frame strorage kind not implemented: {storage}");

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
    }
}
