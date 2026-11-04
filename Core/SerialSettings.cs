using System.IO.Ports;

namespace SerialProxy.Core
{
    public sealed class SerialSettings
    {
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public Parity Parity { get; set; }
        public StopBits StopBits { get; set; }
        public Handshake Handshake { get; set; }
        public bool Dtr { get; set; }
        public bool Rts { get; set; }

        public SerialSettings()
        {
            BaudRate = 9600;
            DataBits = 8;
            Parity = Parity.None;
            StopBits = StopBits.One;
            Handshake = Handshake.None;
        }
    }
}
