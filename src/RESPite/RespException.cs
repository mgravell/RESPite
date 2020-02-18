using System;

namespace Respite
{
    public sealed class RespException : Exception
    {
        public RespException(string message) : base(message) { }
    }
}
