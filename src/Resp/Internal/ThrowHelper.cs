using System;
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
    }
}
