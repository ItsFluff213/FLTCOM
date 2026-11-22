using System;
using System.Globalization;
using FLTCOM.Core.Protocol;

namespace FLTCOM.Server
{
    public sealed class ServerOptions
    {
        /// <param name="port">1 to 65535, or 0 for a port the system picks. The server always binds 127.0.0.1.</param>
        /// <param name="webRoot">Folder of the display's files. It is read into memory once, at Start.</param>
        /// <param name="hello">Sent to every client when it connects.</param>
        /// <param name="log">Called on the server thread with one line at a time.</param>
        public ServerOptions(int port, string webRoot, ServerHello hello, Action<string> log)
        {
            if (port < 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }
            Port = port;
            WebRoot = webRoot ?? throw new ArgumentNullException(nameof(webRoot));
            Hello = hello ?? throw new ArgumentNullException(nameof(hello));
            Log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public int Port { get; }
        public string WebRoot { get; }
        public ServerHello Hello { get; }
        public Action<string> Log { get; }
    }

    public sealed class PortBusyException : Exception
    {
        public PortBusyException(int port)
            : base("port " + port.ToString(CultureInfo.InvariantCulture) + " busy")
        {
            Port = port;
        }

        public int Port { get; }
    }
}
