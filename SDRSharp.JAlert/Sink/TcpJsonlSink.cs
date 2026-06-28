// TCP JSONL sink. Listens on a port and fans out every received line to each
// connected client via per-client queues drained by background threads, so a
// slow client only loses lines (drop-oldest at kMaxQueuedLines) and never blocks
// the decoder. Ported from src/sink/tcp_sink.{h,cpp}.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SDRSharp.JAlert.Sink
{
    public struct TcpSinkSnapshot
    {
        public SinkPhase Phase;
        public int Port;
        public int ClientCount;
        public long BytesSent;
        public string ErrorMessage;
    }

    public sealed class TcpJsonlSink : IJsonlSink
    {
        private const int MaxQueuedLines = 4096;

        private sealed class Client
        {
            private readonly Socket _sock;
            private readonly TcpJsonlSink _owner;
            private readonly object _qGate = new object();
            private readonly Queue<byte[]> _queue = new Queue<byte[]>();
            private volatile bool _closed;
            private Thread _writer;

            public Client(Socket sock, TcpJsonlSink owner)
            {
                _sock = sock;
                _owner = owner;
            }

            public bool IsClosed => _closed;

            public void Start()
            {
                _writer = new Thread(WriteLoop) { IsBackground = true, Name = "JAlert-TcpClient" };
                _writer.Start();
            }

            public void Enqueue(byte[] line)
            {
                if (_closed) return;
                lock (_qGate)
                {
                    while (_queue.Count >= MaxQueuedLines) _queue.Dequeue();
                    _queue.Enqueue(line);
                    Monitor.Pulse(_qGate);
                }
            }

            private void WriteLoop()
            {
                while (!_closed)
                {
                    byte[] chunk;
                    lock (_qGate)
                    {
                        while (!_closed && _queue.Count == 0) Monitor.Wait(_qGate);
                        if (_closed) return;
                        chunk = _queue.Dequeue();
                    }
                    int offset = 0;
                    int remaining = chunk.Length;
                    while (remaining > 0)
                    {
                        int n;
                        try { n = _sock.Send(chunk, offset, remaining, SocketFlags.None); }
                        catch { n = 0; }
                        if (n <= 0) { _closed = true; return; }
                        offset += n;
                        remaining -= n;
                        _owner.OnBytesSent(n);
                    }
                }
            }

            public void Shutdown()
            {
                if (_closed)
                {
                    JoinWriter();
                    return;
                }
                _closed = true;
                lock (_qGate) Monitor.PulseAll(_qGate);
                try { _sock.Shutdown(SocketShutdown.Both); } catch { }
                try { _sock.Close(); } catch { }
                JoinWriter();
            }

            private void JoinWriter()
            {
                if (_writer != null && _writer.IsAlive &&
                    Thread.CurrentThread.ManagedThreadId != _writer.ManagedThreadId)
                {
                    _writer.Join();
                }
            }
        }

        private readonly object _gate = new object();
        private readonly int _port;
        private TcpListener _listener;
        private Thread _accepter;
        private readonly List<Client> _clients = new List<Client>();
        private string _status = "";
        private string _error = "";
        private long _bytesSent;
        private volatile bool _stop;

        public TcpJsonlSink(int port)
        {
            _port = port;
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start(8);
                _status = "listening on 0.0.0.0:" + port;
                _accepter = new Thread(AcceptLoop) { IsBackground = true, Name = "JAlert-TcpAccept" };
                _accepter.Start();
            }
            catch (Exception ex)
            {
                _error = ex.Message;
                _status = "error: " + _error;
                _listener = null;
            }
        }

        public int Port => _port;

        public string Status
        {
            get { lock (_gate) return _status + "; clients=" + _clients.Count; }
        }

        public TcpSinkSnapshot Snapshot()
        {
            lock (_gate)
            {
                TcpSinkSnapshot s = new TcpSinkSnapshot
                {
                    Port = _port,
                    ClientCount = _clients.Count,
                    BytesSent = _bytesSent,
                    ErrorMessage = _error,
                };
                if (!string.IsNullOrEmpty(_error)) s.Phase = SinkPhase.Error;
                else if (_listener != null) s.Phase = SinkPhase.Active;
                else s.Phase = SinkPhase.Off;
                return s;
            }
        }

        private void AcceptLoop()
        {
            while (!_stop)
            {
                Socket cfd;
                try { cfd = _listener.AcceptSocket(); }
                catch { return; }
                if (_stop) { try { cfd.Close(); } catch { } return; }
                try { cfd.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true); } catch { }

                Client client = new Client(cfd, this);
                lock (_gate) _clients.Add(client);
                client.Start();
            }
        }

        public void WriteLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            byte[] shared = Encoding.UTF8.GetBytes(line + "\n");

            lock (_gate)
            {
                for (int i = _clients.Count - 1; i >= 0; --i)
                {
                    if (_clients[i].IsClosed)
                    {
                        _clients[i].Shutdown();
                        _clients.RemoveAt(i);
                    }
                    else
                    {
                        _clients[i].Enqueue(shared);
                    }
                }
            }
        }

        public void Stop()
        {
            if (_stop) return;
            _stop = true;
            List<Client> taken;
            lock (_gate)
            {
                _status = "off";
                try { _listener?.Stop(); } catch { }
                _listener = null;
                taken = new List<Client>(_clients);
                _clients.Clear();
            }
            foreach (Client c in taken) c.Shutdown();
            if (_accepter != null && _accepter.IsAlive) _accepter.Join();
        }

        private void OnBytesSent(long n)
        {
            lock (_gate) _bytesSent += n;
        }
    }
}
