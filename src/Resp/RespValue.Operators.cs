namespace Resp
{
    partial struct RespValue
    {
        public static RespValue True => s_true;
        public static RespValue False => s_false;

        private static readonly RespValue
            s_true = Create(RespType.Boolean, "t"),
            s_false = Create(RespType.Boolean, "f");

        public static implicit operator RespValue(string value) => Create(RespType.BlobString, value);

        public static implicit operator RespValue(bool value) => value ? s_true : s_false;

        public static implicit operator RespValue(int value) => Create(RespType.Number, (long)value);
        public static implicit operator RespValue(long value) => Create(RespType.Number, value);
    }
}
