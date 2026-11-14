using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MissionLab.Core
{
    public sealed class HttpRequest
    {
        public string Method = "GET", Path = "/";
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] Body = new byte[0];
        public string BodyText => Encoding.UTF8.GetString(Body);
    }

    public sealed class HttpResponse
    {
        public int Status = 200;
        public string ContentType = "application/json; charset=utf-8";
        public byte[] Body = new byte[0];
        public static HttpResponse Json(string json, int status = 200) => new HttpResponse { Status = status, Body = Encoding.UTF8.GetBytes(json) };
        public static HttpResponse Error(int status, string message) => Json("{\"error\":\"" + message.Replace("\\", "\\\\").Replace("\"", "'") + "\"}", status);
        public static HttpResponse File(string path, string contentType) =>
            System.IO.File.Exists(path) ? new HttpResponse { ContentType = contentType, Body = System.IO.File.ReadAllBytes(path) } : Error(404, "not found");
    }

    /// <summary>
    /// A very small web server for this computer only (it listens on 127.0.0.1, so nothing on
    /// the network can reach it). One request per connection; the handler runs on a worker thread.
    /// </summary>
    public sealed class MiniHttp : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<HttpRequest, HttpResponse> _handler;
        private Thread _thread;
        private volatile bool _running;
        public int Port { get; }

        public MiniHttp(int port, Func<HttpRequest, HttpResponse> handler)
        {
            Port = port; _handler = handler;
            _listener = new TcpListener(IPAddress.Loopback, port);
        }

        public void Start()
        {
            _listener.Start(16);
            _running = true;
            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "MissionLab web" };
            _thread.Start();
        }

        public void Dispose()
        {
            _running = false;
            try { _listener.Stop(); } catch { }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { if (!_running) return; Thread.Sleep(50); continue; }
                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 5000; client.SendTimeout = 15000; client.NoDelay = true;
                    NetworkStream s = client.GetStream();
                    HttpResponse res;
                    try
                    {
                        HttpRequest req = Read(s);
                        res = req == null ? HttpResponse.Error(400, "bad request") : (_handler(req) ?? HttpResponse.Error(500, "no answer"));
                    }
                    catch (Exception e) { res = HttpResponse.Error(500, e.GetType().Name + ": " + e.Message); }
                    string reason = res.Status == 200 ? "OK" : res.Status == 404 ? "Not Found" : res.Status == 400 ? "Bad Request" : res.Status == 403 ? "Forbidden" : res.Status == 503 ? "Service Unavailable" : "Error";
                    byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + res.Status + " " + reason + "\r\nContent-Type: " + res.ContentType + "\r\nContent-Length: " + res.Body.Length +
                                                          "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
                    s.Write(head, 0, head.Length);
                    s.Write(res.Body, 0, res.Body.Length);
                    s.Flush();
                }
            }
            catch { /* the browser went away */ }
        }

        private static HttpRequest Read(Stream s)
        {
            // Headers end at the first empty line.
            var buf = new MemoryStream();
            var one = new byte[1];
            int state = 0;
            while (state < 4)
            {
                if (s.Read(one, 0, 1) != 1 || buf.Length > 32768) return null;
                buf.WriteByte(one[0]);
                state = one[0] == (state % 2 == 0 ? (byte)'\r' : (byte)'\n') ? state + 1 : (one[0] == (byte)'\r' ? 1 : 0);
            }
            string[] lines = Encoding.ASCII.GetString(buf.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return null;
            string[] first = lines[0].Split(' ');
            if (first.Length < 2) return null;
            var req = new HttpRequest { Method = first[0].ToUpperInvariant() };
            int q = first[1].IndexOf('?');
            req.Path = q >= 0 ? first[1].Substring(0, q) : first[1];
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c > 0) req.Headers[lines[i].Substring(0, c).Trim()] = lines[i].Substring(c + 1).Trim();
            }
            if (req.Headers.TryGetValue("Content-Length", out string lenText) && int.TryParse(lenText, out int len) && len > 0)
            {
                if (len > 8 * 1024 * 1024) return null;
                req.Body = new byte[len];
                int got = 0;
                while (got < len) { int n = s.Read(req.Body, got, len - got); if (n <= 0) return null; got += n; }
            }
            return req;
        }
    }
}
