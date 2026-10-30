using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Clothesline.Core;

public static class TextContent
{
    public const int MaxCharacters = 100_000;
    public const int MaxBytes = 1024 * 1024;
    public static readonly UTF8Encoding Encoding = new(false, true);
    public static byte[] Encode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(T("文字不能为空。"));
        if (text.Length > MaxCharacters || text.Contains('\0')) throw new ArgumentException(T("文字最多支持 100000 个字符，且不能包含空字符。"));
        return Encoding.GetBytes(text);
    }
    public static string Hash(string text) => "Text:" + Convert.ToHexString(SHA256.HashData(Encode(text)));
    public static string Title(string text)
    {
        var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim() ?? "";
        var starts = StringInfo.ParseCombiningCharacters(line);
        return starts.Length > 40 ? line[..starts[40]] + "…" : line;
    }
    public static string Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length > MaxBytes) throw new IOException(T("文字文件过大。"));
        using var reader = new StreamReader(file, Encoding, true);
        var buffer = new char[MaxCharacters + 1]; var count = reader.ReadBlock(buffer, 0, buffer.Length);
        var text = new string(buffer, 0, count); Encode(text); return text;
    }
}
