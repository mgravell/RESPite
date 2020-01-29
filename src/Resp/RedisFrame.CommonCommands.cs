namespace Resp
{
    partial class RedisFrame
    {
        public static RedisFrame Ping { get; } = RedisSimpleString.Create("PING");
    }
}
