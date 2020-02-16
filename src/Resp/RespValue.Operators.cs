using System.Buffers;
using System.Numerics;

namespace Resp
{
    partial struct RespValue
    {
        public static RespValue True => s_True;
        public static RespValue False => s_False;

        private static readonly RespValue
            s_True = Create(RespType.Boolean, "t"),
            s_False = Create(RespType.Boolean, "f"),
            s_PositiveInfinity = Create(RespType.Double, "+inf"),
            s_NegativeInfinity = Create(RespType.Double, "-inf");

        public static implicit operator RespValue(string value) => Create(RespType.BlobString, value);

        public static implicit operator RespValue(bool value) => value ? s_True : s_False;

        public static implicit operator RespValue(int value) => Create(RespType.Number, (long)value);
        public static implicit operator RespValue(long value) => Create(RespType.Number, value);

        public static implicit operator RespValue(double value)
        {
            if (double.IsPositiveInfinity(value)) return s_PositiveInfinity;
            if (double.IsNegativeInfinity(value)) return s_NegativeInfinity;
            return Create(RespType.Double, value);
        }

        public static explicit operator string(in RespValue value) => value.ToString();
    }
}
