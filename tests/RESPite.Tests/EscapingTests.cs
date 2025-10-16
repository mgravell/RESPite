using System.Text;

namespace RESPite;

public class EscapingTests
{
    [Theory]
    [InlineData("abc", "*1\r\n$3\r\nabc\r\n")]
    [InlineData("abc def", "*2\r\n$3\r\nabc\r\n$3\r\ndef\r\n")]
    [InlineData("abc  def", "*2\r\n$3\r\nabc\r\n$3\r\ndef\r\n")]
    [InlineData(" abc def ", "*2\r\n$3\r\nabc\r\n$3\r\ndef\r\n")]
    [InlineData(" 'abc def' ", "*1\r\n$7\r\nabc def\r\n")]
    [InlineData(""" "abc def" """, "*1\r\n$7\r\nabc def\r\n")]
    [InlineData(""" "abc def""", "*1\r\n$7\r\nabc def\r\n")]
    [InlineData(""" 'abc def""", "*1\r\n$7\r\nabc def\r\n")]
    [InlineData(""" 'abc\' def""", "*1\r\n$8\r\nabc\' def\r\n")]
    [InlineData(""" 'abc\\ def""", "*1\r\n$8\r\nabc\\ def\r\n")]
    [InlineData(""" 'abc\" def""", "*1\r\n$9\r\nabc\\\" def\r\n")]
    [InlineData("""abc 'def ghi' "mno pqr" stu """, "*4\r\n$3\r\nabc\r\n$7\r\ndef ghi\r\n$7\r\nmno pqr\r\n$3\r\nstu\r\n")]
    [InlineData("""abc "def\tghi" jkl""", "*3\r\n$3\r\nabc\r\n$7\r\ndef\tghi\r\n$3\r\njkl\r\n")]
    [InlineData("""abc "def\x67hi""", "*2\r\n$3\r\nabc\r\n$6\r\ndefghi\r\n")]
    [InlineData("""abc "def\x6Xhi""", "*2\r\n$3\r\nabc\r\n$9\r\ndef\\x6Xhi\r\n")]
    public void ParseToResp(string input, string expected)
    {
        using var lease = CommandParser.ParseResp(input.AsSpan());
        var segment = lease.ArraySegment;
        var actual = Encoding.UTF8.GetString(segment.Array ?? [], segment.Offset, segment.Count);
        Assert.Equal(expected, actual);
    }
}
