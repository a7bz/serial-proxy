using System;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace SerialProxy.Core
{
    /// <summary>
    /// 双向串口代理：真实串口 &lt;-&gt; 虚拟串口。
    /// 真实口接设备，虚拟口接 com0com 的另一端，上位机连虚拟口另一端。
    /// </summary>
    public sealed class SerialBridge : IDisposable
    {
        private readonly SerialSettings _settings;
        private readonly object _realWriteLock = new object();
        private readonly object _virtualWriteLock = new object();

        private SerialPort _real;
        private SerialPort _virtual;
        private Thread _rxThread;
        private Thread _txThread;
        private volatile bool _running;

        /// <summary>每收到一段数据触发一次（后台线程）。</summary>
        public event Action<LogEntry> EntryReceived;

        /// <summary>转发错误（后台线程）。</summary>
        public event Action<string> Error;

        public SerialBridge(SerialSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            _settings = settings;
        }

        public bool IsRunning
        {
            get { return _running; }
        }

        public void Start(string realPortName, string virtualPortName)
        {
            if (_running) throw new InvalidOperationException("代理已经在运行。");
            if (string.IsNullOrWhiteSpace(realPortName)) throw new ArgumentException("请选择真实串口。");
            if (string.IsNullOrWhiteSpace(virtualPortName)) throw new ArgumentException("请选择虚拟串口。");
            if (string.Equals(realPortName.Trim(), virtualPortName.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("真实串口和虚拟串口不能是同一个端口。");

            _real = CreatePort(realPortName.Trim());
            _virtual = CreatePort(virtualPortName.Trim());

            try
            {
                _real.Open();
            }
            catch
            {
                CloseQuietly(_real);
                _real = null;
                throw;
            }

            try
            {
                _virtual.Open();
            }
            catch
            {
                CloseQuietly(_real);
                CloseQuietly(_virtual);
                _real = null;
                _virtual = null;
                throw;
            }

            _running = true;

            _rxThread = new Thread(() => Pump(_real, _virtual, true, _virtualWriteLock))
            {
                IsBackground = true,
                Name = "SerialProxy.Rx"
            };
            _txThread = new Thread(() => Pump(_virtual, _real, false, _realWriteLock))
            {
                IsBackground = true,
                Name = "SerialProxy.Tx"
            };

            _rxThread.Start();
            _txThread.Start();
        }

        /// <summary>手动发送到设备（真实串口），并记录为 TX。</summary>
        public void SendToDevice(byte[] data)
        {
            if (!_running || _real == null)
                throw new InvalidOperationException("代理未运行。");

            if (data == null || data.Length == 0) return;

            lock (_realWriteLock)
            {
                _real.BaseStream.Write(data, 0, data.Length);
                _real.BaseStream.Flush();
            }

            RaiseEntry(new LogEntry(DateTime.Now, false, true, (byte[])data.Clone()));
        }

        public void Stop()
        {
            _running = false;

            JoinQuietly(_rxThread);
            JoinQuietly(_txThread);

            CloseQuietly(_real);
            CloseQuietly(_virtual);

            _real = null;
            _virtual = null;
            _rxThread = null;
            _txThread = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private SerialPort CreatePort(string name)
        {
            return new SerialPort(name, _settings.BaudRate, _settings.Parity, _settings.DataBits, _settings.StopBits)
            {
                Handshake = _settings.Handshake,
                ReadTimeout = 100,
                WriteTimeout = 1000,
                ReadBufferSize = 65536,
                WriteBufferSize = 16384,
                DtrEnable = _settings.Dtr,
                RtsEnable = _settings.Rts
            };
        }

        private void Pump(SerialPort source, SerialPort target, bool isRx, object targetLock)
        {
            var buffer = new byte[4096];

            while (_running)
            {
                int read;
                try
                {
                    read = source.BaseStream.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    Fail((isRx ? "读取真实串口失败：" : "读取虚拟串口失败：") + ex.Message);
                    break;
                }

                if (read <= 0) continue;

                var data = new byte[read];
                Buffer.BlockCopy(buffer, 0, data, 0, read);

                try
                {
                    lock (targetLock)
                    {
                        target.BaseStream.Write(data, 0, data.Length);
                        target.BaseStream.Flush();
                    }
                }
                catch (TimeoutException)
                {
                    Fail(isRx
                        ? "写入虚拟串口超时：上位机可能没有打开虚拟串口，或没有在读数据。"
                        : "写入真实串口超时：设备可能未就绪。");
                    break;
                }
                catch (Exception ex)
                {
                    Fail((isRx ? "转发到虚拟串口失败：" : "转发到真实串口失败：") + ex.Message);
                    break;
                }

                RaiseEntry(new LogEntry(DateTime.Now, isRx, false, data));
            }
        }

        /// <summary>任一方向出错就整体停止，避免留下半瘫的代理。</summary>
        private void Fail(string message)
        {
            if (!_running) return;

            _running = false;
            RaiseError(message);
        }

        private void RaiseEntry(LogEntry entry)
        {
            var handler = EntryReceived;
            if (handler == null) return;

            try
            {
                handler(entry);
            }
            catch
            {
                // 订阅方异常不能影响转发线程
            }
        }

        private void RaiseError(string message)
        {
            var handler = Error;
            if (handler == null) return;

            try
            {
                handler(message);
            }
            catch
            {
            }
        }

        private static void JoinQuietly(Thread thread)
        {
            if (thread == null) return;
            try
            {
                thread.Join(500);
            }
            catch
            {
            }
        }

        private static void CloseQuietly(SerialPort port)
        {
            if (port == null) return;
            try
            {
                if (port.IsOpen) port.Close();
            }
            catch
            {
            }
            try
            {
                port.Dispose();
            }
            catch
            {
            }
        }
    }
}
