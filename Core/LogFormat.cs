using System;
using System.Globalization;
using System.Text;

namespace SerialProxy.Core
{
    public static class LogFormat
    {
        public const string CsvHeader = "time,direction,manual,length,hex,ascii";

        public static Encoding GetEncoding(string name)
        {
            if (string.Equals(name, "GBK", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return Encoding.GetEncoding(936);
                }
                catch
                {
                    // 系统未安装东亚语言支持时退回 ASCII
                    return Encoding.ASCII;
                }
            }

            if (string.Equals(name, "UTF-8", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "UTF8", StringComparison.OrdinalIgnoreCase))
                return new UTF8Encoding(false);

            return Encoding.ASCII;
        }

        public static string Hex(byte[] data, int maxBytes)
        {
            if (data == null || data.Length == 0) return "(空)";

            int count = maxBytes <= 0 ? data.Length : Math.Min(data.Length, maxBytes);
            var sb = new StringBuilder(count * 3);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            if (count < data.Length)
                sb.Append(" ... (+").Append(data.Length - count).Append(" B)");

            return sb.ToString();
        }

        public static string Ascii(byte[] data, Encoding encoding, int maxBytes)
        {
            if (data == null || data.Length == 0) return "";

            int count = maxBytes <= 0 ? data.Length : Math.Min(data.Length, maxBytes);
            var sb = new StringBuilder(count);

            if (encoding == null || encoding.CodePage == Encoding.ASCII.CodePage)
            {
                for (int i = 0; i < count; i++)
                {
                    byte b = data[i];
                    sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
                }
            }
            else
            {
                string text;
                try
                {
                    text = encoding.GetString(data, 0, count);
                }
                catch
                {
                    text = Encoding.ASCII.GetString(data, 0, count);
                }

                foreach (char c in text)
                    sb.Append(c >= 32 && c != 127 ? c : '.');
            }

            if (count < data.Length)
                sb.Append("...");

            return sb.ToString();
        }

        /// <summary>只格式化数据体，供列表的“数据”列使用。</summary>
        public static string FormatBody(LogEntry entry, string viewMode, Encoding encoding, int maxBytes)
        {
            if (entry == null) return "";

            string body;
            if (string.Equals(viewMode, "HEX", StringComparison.OrdinalIgnoreCase))
            {
                body = Hex(entry.Data, maxBytes);
            }
            else if (string.Equals(viewMode, "ASCII", StringComparison.OrdinalIgnoreCase))
            {
                body = Ascii(entry.Data, encoding, maxBytes);
            }
            else
            {
                body = Hex(entry.Data, maxBytes) + "  | " + Ascii(entry.Data, encoding, maxBytes);
            }

            return entry.IsManual ? "〔手动〕 " + body : body;
        }

        public static string DisplayLine(LogEntry entry, string viewMode, Encoding encoding, int maxBytes)
        {
            if (entry == null) return "";
            return string.Format(CultureInfo.InvariantCulture,
                "{0:HH:mm:ss.fff} [{1,5}] {2}",
                entry.Time,
                entry.Data.Length,
                FormatBody(entry, viewMode, encoding, maxBytes));
        }

        public static string CsvLine(LogEntry entry)
        {
            return string.Join(",",
                entry.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                entry.IsRx ? "RX" : "TX",
                entry.IsManual ? "1" : "0",
                entry.Data.Length.ToString(CultureInfo.InvariantCulture),
                CsvEscape(Hex(entry.Data, 0)),
                CsvEscape(Ascii(entry.Data, Encoding.ASCII, 0)));
        }

        public static string CsvEscape(string text)
        {
            if (text == null) return "";
            text = text.Replace("\r", " ").Replace("\n", " ");
            if (text.IndexOf(',') < 0 && text.IndexOf('"') < 0)
                return text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
