using System.Text;

namespace RESPite;

internal static partial class Constants
{
    public static readonly UTF8Encoding UTF8 = new(false);
    public const int MaxUtf8BytesPerChar = 4;
}
