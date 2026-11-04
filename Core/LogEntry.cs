using System;

namespace SerialProxy.Core
{
    /// <summary>一条串口数据记录。Data 已经复制，调用方可以复用读取缓冲区。</summary>
    public sealed class LogEntry
    {
        public DateTime Time { get; private set; }
        public bool IsRx { get; private set; }
        public bool IsManual { get; private set; }
        public byte[] Data { get; private set; }

        public LogEntry(DateTime time, bool isRx, bool isManual, byte[] data)
        {
            Time = time;
            IsRx = isRx;
            IsManual = isManual;
            Data = data ?? new byte[0];
        }
    }
}
