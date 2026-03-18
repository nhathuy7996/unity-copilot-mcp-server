// UnityBridgeServer.cs
// Runs a WebSocket server on a background thread inside the Unity Editor.
// Incoming JSON commands are queued and dispatched on the main thread via
// EditorApplication.update so all UnityEditor API calls stay safe.
//
// Install location: Assets/Editor/UnityBridge/UnityBridgeServer.cs

#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityCopilot
{
    /// <summary>
    /// Initialises the WebSocket bridge server automatically when Unity enters
    /// Editor mode.  The server listens on 127.0.0.1:6400 (configurable via
    /// Edit > Preferences > Unity Copilot).
    /// </summary>
    [InitializeOnLoad]
    public static class UnityBridgeServer
    {
        // ── Configuration ─────────────────────────────────────────────
        public static int Port = 6400;

        // ── State ─────────────────────────────────────────────────────
        private static TcpListener _listener;
        private static Thread _serverThread;
        private static volatile bool _running;
        private static readonly List<ClientSession> _clients = new List<ClientSession>();
        private static readonly object _clientsLock = new object();

        // Messages pending execution on the Unity main thread
        private static readonly ConcurrentQueue<PendingMessage> _mainThreadQueue =
            new ConcurrentQueue<PendingMessage>();

        // ── Startup ───────────────────────────────────────────────────
        static UnityBridgeServer()
        {
            EditorApplication.update += ProcessMainThreadQueue;
            EditorApplication.quitting += Stop;
            Start();
        }

        private static void Start()
        {
            if (_running) { return; }
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start();
                _running = true;
                _serverThread = new Thread(AcceptLoop) { IsBackground = true, Name = "UnityCopilot-WS" };
                _serverThread.Start();
                Debug.Log($"[UnityCopilot] Bridge listening on ws://127.0.0.1:{Port}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityCopilot] Failed to start bridge: {ex.Message}");
            }
        }

        private static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* ignore */ }

            lock (_clientsLock)
            {
                foreach (var c in _clients) { c.Close(); }
                _clients.Clear();
            }
            Debug.Log("[UnityCopilot] Bridge stopped.");
        }

        [MenuItem("Unity Copilot/Restart Bridge Server")]
        public static void Restart()
        {
            Stop();
            // Small delay to ensure port is released before rebinding
            EditorApplication.delayCall += () =>
            {
                Start();
                Debug.Log("[UnityCopilot] Bridge restarted.");
            };
        }

        /// <summary>Push a JSON message to every connected client (main thread or background).</summary>
        public static void Broadcast(string json)
        {
            List<ClientSession> snapshot;
            lock (_clientsLock) { snapshot = new List<ClientSession>(_clients); }
            foreach (var session in snapshot)
            {
                try
                {
                    if (session.IsOpen) { SendFrame(session.Stream, json); }
                }
                catch { /* client may have just disconnected */ }
            }
        }

        // ── Accept loop (background thread) ───────────────────────────
        private static void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient tcp = _listener.AcceptTcpClient();
                    Thread clientThread = new Thread(() => HandleClient(tcp)) { IsBackground = true };
                    clientThread.Start();
                }
                catch (SocketException) { /* listener stopped */ }
                catch (Exception ex)
                {
                    if (_running) { Debug.LogWarning($"[UnityCopilot] Accept error: {ex.Message}"); }
                }
            }
        }

        // ── Per-client handler (background thread) ─────────────────────
        private static void HandleClient(TcpClient tcp)
        {
            var session = new ClientSession(tcp);
            lock (_clientsLock) { _clients.Add(session); }

            try
            {
                NetworkStream stream = tcp.GetStream();

                // ── WebSocket HTTP upgrade handshake ──────────────────
                if (!PerformHandshake(stream)) { return; }

                // ── Read frames loop ──────────────────────────────────
                while (_running && tcp.Connected)
                {
                    string message = ReadFrame(stream);
                    if (message == null) { break; } // connection closed

                    _mainThreadQueue.Enqueue(new PendingMessage { Session = session, Json = message });
                }
            }
            catch (Exception ex)
            {
                if (_running) { Debug.LogWarning($"[UnityCopilot] Client error: {ex.Message}"); }
            }
            finally
            {
                session.Close();
                lock (_clientsLock) { _clients.Remove(session); }
            }
        }

        // ── RFC 6455 handshake ─────────────────────────────────────────
        private static bool PerformHandshake(NetworkStream stream)
        {
            // Read HTTP upgrade request (may arrive in multiple reads on slow connections)
            var upgradeBuffer = new System.Text.StringBuilder();
            byte[] tmp = new byte[1024];
            do
            {
                int n = stream.Read(tmp, 0, tmp.Length);
                if (n <= 0) { return false; }
                upgradeBuffer.Append(Encoding.UTF8.GetString(tmp, 0, n));
            } while (stream.DataAvailable);
            string request = upgradeBuffer.ToString();

            string key = null;
            foreach (string line in request.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    key = line.Substring("Sec-WebSocket-Key:".Length).Trim();
                    break;
                }
            }

            if (key == null) { return false; }

            string acceptKey;
            using (var sha1 = SHA1.Create())
            {
                acceptKey = Convert.ToBase64String(
                    sha1.ComputeHash(
                        Encoding.UTF8.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            }

            string response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            stream.Write(responseBytes, 0, responseBytes.Length);
            return true;
        }

        // ── Read a complete WebSocket message (RFC 6455) ───────────────
        // Transparently handles fragmented frames, PING→PONG, and PONG ignore.
        // Returns null only when the connection should be closed.
        private static string ReadFrame(NetworkStream stream)
        {
            using (var msgBuffer = new MemoryStream())
            {
                while (true)
                {
                    int b0 = stream.ReadByte();
                    int b1 = stream.ReadByte();
                    if (b0 < 0 || b1 < 0) { return null; } // stream ended

                    bool isFin     = (b0 & 0x80) != 0;
                    int  opcode    = b0 & 0x0F;
                    bool masked    = (b1 & 0x80) != 0;
                    long payloadLen = b1 & 0x7F;

                    if (payloadLen == 126)
                    {
                        byte[] ext = new byte[2]; ReadExact(stream, ext, 2);
                        payloadLen = (ext[0] << 8) | ext[1];
                    }
                    else if (payloadLen == 127)
                    {
                        byte[] ext = new byte[8]; ReadExact(stream, ext, 8);
                        payloadLen = 0;
                        for (int i = 0; i < 8; i++) { payloadLen = (payloadLen << 8) | ext[i]; }
                    }

                    byte[] maskKey = new byte[4];
                    if (masked) { ReadExact(stream, maskKey, 4); }

                    byte[] payload = new byte[payloadLen];
                    if (payloadLen > 0) { ReadExact(stream, payload, (int)payloadLen); }

                    if (masked)
                    {
                        for (int i = 0; i < payload.Length; i++) { payload[i] ^= maskKey[i % 4]; }
                    }

                    switch (opcode)
                    {
                        case 0x8:                              // Close — signal disconnect
                            return null;
                        case 0x9:                              // Ping — reply with Pong, keep reading
                            SendPong(stream, payload);
                            continue;
                        case 0xA:                              // Pong — ignore, keep reading
                            continue;
                    }

                    // opcode 0x0 = continuation, 0x1 = text, 0x2 = binary
                    msgBuffer.Write(payload, 0, payload.Length);
                    if (isFin) { break; } // last (or only) fragment — message complete
                }

                return Encoding.UTF8.GetString(msgBuffer.ToArray());
            }
        }

        // ── Read exactly `count` bytes, blocking until satisfied ───────
        private static void ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = stream.Read(buffer, total, count - total);
                if (n <= 0) { throw new EndOfStreamException("WebSocket stream closed unexpectedly."); }
                total += n;
            }
        }

        // ── Send a WebSocket PONG control frame ────────────────────────
        private static void SendPong(NetworkStream stream, byte[] pingPayload)
        {
            int   len   = Math.Min(pingPayload.Length, 125); // control frames ≤ 125 bytes
            var   frame = new byte[2 + len];
            frame[0] = 0x8A;          // FIN=1, opcode=Pong
            frame[1] = (byte)len;
            Buffer.BlockCopy(pingPayload, 0, frame, 2, len);
            try { lock (stream) { stream.Write(frame, 0, frame.Length); } } catch { /* ignore */ }
        }

        // ── Send a WebSocket text frame ────────────────────────────────
        public static void SendFrame(NetworkStream stream, string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            long len = payload.Length;
            List<byte> frame = new List<byte>();
            frame.Add(0x81); // FIN=1, opcode=text

            if (len <= 125)
            {
                frame.Add((byte)len);
            }
            else if (len <= 65535)
            {
                frame.Add(126);
                frame.Add((byte)(len >> 8));
                frame.Add((byte)(len & 0xFF));
            }
            else
            {
                frame.Add(127);
                for (int i = 7; i >= 0; i--) { frame.Add((byte)((len >> (i * 8)) & 0xFF)); }
            }

            frame.AddRange(payload);
            byte[] frameBytes = frame.ToArray();
            lock (stream) { stream.Write(frameBytes, 0, frameBytes.Length); }
        }

        // ── Main-thread queue processing ───────────────────────────────
        private static void ProcessMainThreadQueue()
        {
            while (_mainThreadQueue.TryDequeue(out PendingMessage msg))
            {
                try
                {
                    string responseJson = UnityCommandHandler.Handle(msg.Json);
                    if (responseJson != null && msg.Session.IsOpen)
                    {
                        SendFrame(msg.Session.Stream, responseJson);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UnityCopilot] Handler error: {ex}");
                    // Attempt to reply with error
                    try
                    {
                        string errorReply = $"{{\"id\":\"\",\"success\":false,\"message\":\"{EscapeJson(ex.Message)}\"}}";
                        if (msg.Session.IsOpen) { SendFrame(msg.Session.Stream, errorReply); }
                    }
                    catch { /* ignore */ }
                }
            }
        }

        private static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") ?? "";

        // ── Inner types ────────────────────────────────────────────────
        private class PendingMessage
        {
            public ClientSession Session;
            public string Json;
        }

        public class ClientSession
        {
            private readonly TcpClient _tcp;
            public NetworkStream Stream { get; }
            public bool IsOpen => _tcp.Connected;

            public ClientSession(TcpClient tcp)
            {
                _tcp = tcp;
                Stream = tcp.GetStream();
            }

            public void Close()
            {
                try { _tcp.Close(); } catch { /* ignore */ }
            }
        }
    }
}
#endif
