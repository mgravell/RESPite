using System;

namespace Resp
{
    public sealed class RespException : Exception
    {
        public RespException(string message) : base(message) { }
    }
}
