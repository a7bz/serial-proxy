using System;
using System.Globalization;
using System.Text;

namespace SerialProxy.Core
{
    public static class HexUtil
    {
        /// <summary>解析 "01 03 0A", "0x01,0x03", "01030A" 等格式。</summary>
        public static bool TryParse(string text, out byte[] data, out string error)
        {
            data = null;
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "请输入要发送的数据。";
                return false;
            }

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (char.IsWhiteSpace(c) || c == ',' || c == ';' || c == '-' || c == ':' || c == '_')
                    continue;

                if (c == '0' && i + 1 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X'))
                {
                    i++;
                    continue;
                }

                if (c == '\\' && i + 1 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X'))
                {
                    i++;
                    continue;
                }

                if (!Uri.IsHexDigit(c))
                {
                    error = string.Format("包含非十六进制字符：'{0}'", c);
                    return false;
                }

                sb.Append(c);
            }

            if (sb.Length == 0)
            {
                error = "没有有效的十六进制字符。";
                return false;
            }

            if ((sb.Length & 1) != 0)
            {
                error = "十六进制字符数必须为偶数。";
                return false;
            }

            data = new byte[sb.Length / 2];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = byte.Parse(
                    sb.ToString(i * 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture);
            }

            return true;
        }
    }
}
