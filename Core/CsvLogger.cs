using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace SerialProxy.Core
{
    /// <summary>把记录异步写入 CSV，不阻塞串口读取线程。</summary>
    public sealed class CsvLogger : IDisposable
    {
        private readonly BlockingCollection<LogEntry> _queue =
            new BlockingCollection<LogEntry>(10000);

        private Thread _thread;
        private StreamWriter _writer;
        private volatile bool _running;
        private long _dropped;

        public string FilePath { get; private set; }
        public long Dropped { get { return Interlocked.Read(ref _dropped); } }

        public void Start(string directory)
        {
            if (_running) return;

            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(
                directory,
                "proxy_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");

            _writer = new StreamWriter(FilePath, false, new UTF8Encoding(true));
            _writer.WriteLine(LogFormat.CsvHeader);
            _running = true;

            _thread = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "CsvLogger"
            };
            _thread.Start();
        }

        public void Enqueue(LogEntry entry)
        {
            if (!_running || entry == null) return;

            try
            {
                if (!_queue.TryAdd(entry))
                    Interlocked.Increment(ref _dropped);
            }
            catch (ObjectDisposedException)
            {
                // 已关闭，忽略
            }
            catch (InvalidOperationException)
            {
                // 队列已完成添加，忽略
            }
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            try { _queue.CompleteAdding(); } catch { }
            if (_thread != null)
            {
                try { _thread.Join(1500); } catch { }
            }

            try
            {
                if (_writer != null)
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
            }
            catch
            {
                // 忽略关闭失败
            }
            finally
            {
                _writer = null;
                _thread = null;
            }
        }

        private void WriteLoop()
        {
            int count = 0;
            try
            {
                foreach (var entry in _queue.GetConsumingEnumerable())
                {
                    _writer.WriteLine(LogFormat.CsvLine(entry));
                    count++;

                    if ((count & 0x3F) == 0)
                        _writer.Flush();
                }
            }
            catch
            {
                // 磁盘满/文件被占用时停止写入，不影响串口转发
            }
            finally
            {
                try
                {
                    if (_writer != null) _writer.Flush();
                }
                catch { }
            }
        }

        public void Dispose()
        {
            Stop();
            _queue.Dispose();
        }
    }
}
