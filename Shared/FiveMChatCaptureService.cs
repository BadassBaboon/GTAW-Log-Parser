using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace GTAWParser.Shared
{
    public enum FiveMChatCaptureState
    {
        WaitingForFiveM,
        WaitingForChat,
        Capturing
    }

    /// <summary>
    /// Captures the visible GTAW chat from FiveM's local NUI DevTools endpoint.
    /// This is a localhost-only, read-only connection while FiveM is running.
    /// </summary>
    public static class FiveMChatCaptureService
    {
        public const string DevToolsTargetsUrl = "http://127.0.0.1:13172/json";
        public const string RootUiUrl = "nui://game/ui/root.html";
        public const string ClientFrameUrl = "https://cfx-nui-client/web/index.html";
        public const int PollIntervalMilliseconds = 500;

        private static readonly object SyncRoot = new object();
        public static readonly string SessionDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GTAW-Log-Parser-FiveM");
        public static readonly string SessionFilePath = Path.Combine(SessionDirectory, "current-session.txt");

        private static readonly NuiChatReader Reader = new NuiChatReader();
        private static CancellationTokenSource? _workerCts;
        private static Task? _workerTask;
        private static bool _wasFiveMRunning;
        private static DateTime _sessionStartedAt;
        private static List<string> _previousVisibleLines = new List<string>();
        private static readonly Regex TimestampPrefix = new Regex(@"^\[(?<time>\d{1,2}:\d{2}:\d{2})\]\s+", RegexOptions.Compiled);
        private static FiveMChatCaptureState _captureState = FiveMChatCaptureState.WaitingForFiveM;

        public static event Action<FiveMChatCaptureState>? StateChanged;
        public static event Action<string>? LineReceived;

        public static DateTime SessionStartedAt => _sessionStartedAt == DateTime.MinValue ? DateTime.Now : _sessionStartedAt;

        public static FiveMChatCaptureState CaptureState
        {
            get => _captureState;
            private set
            {
                if (_captureState != value)
                {
                    _captureState = value;
                    StateChanged?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// Starts the background FiveM chat capture worker.
        /// </summary>
        public static void Initialize()
        {
            lock (SyncRoot)
            {
                if (_workerTask != null && !_workerTask.IsCompleted)
                    return;

                Directory.CreateDirectory(SessionDirectory);
                _workerCts = new CancellationTokenSource();
                _workerTask = Task.Run(() => CaptureWorkerAsync(_workerCts.Token));
                Log.Information("FiveMChatCaptureService initialized");
            }
        }

        /// <summary>
        /// Stops the background FiveM chat capture worker and closes connections.
        /// </summary>
        public static void Stop()
        {
            lock (SyncRoot)
            {
                try
                {
                    _workerCts?.Cancel();
                    _workerCts?.Dispose();
                    _workerCts = null;
                    Reader.Close();
                    Log.Information("FiveMChatCaptureService stopped");
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Error while stopping FiveMChatCaptureService");
                }
            }
        }

        /// <summary>
        /// Reads the chat captured in the current session file.
        /// </summary>
        public static string ReadCapturedChat(bool removeTimestamps = false)
        {
            try
            {
                string chat;
                lock (SyncRoot)
                {
                    if (!File.Exists(SessionFilePath))
                        return string.Empty;

                    using (FileStream stream = new FileStream(SessionFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        chat = reader.ReadToEnd();
                    }
                }

                if (removeTimestamps)
                {
                    chat = TimestampRegex.Replace(chat, string.Empty);
                }

                return chat;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error reading captured chat from {Path}", SessionFilePath);
                return string.Empty;
            }
        }

        /// <summary>
        /// Captures a direct, on-demand snapshot of visible chat lines from FiveM NUI.
        /// Useful for the Mini Parser or one-off reads without continuous background capture.
        /// </summary>
        public static async Task<List<string>> GetVisibleChatLinesAsync(CancellationToken token = default)
        {
            return await Reader.GetChatLinesAsync(token).ConfigureAwait(false);
        }

        private static async Task CaptureWorkerAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    bool fiveMRunning = FiveMDetector.IsFiveMRunning();
                    if (!fiveMRunning)
                    {
                        CaptureState = FiveMChatCaptureState.WaitingForFiveM;
                        if (_wasFiveMRunning)
                        {
                            lock (SyncRoot)
                            {
                                Reader.Close();
                                _previousVisibleLines.Clear();
                            }
                        }

                        _wasFiveMRunning = false;
                        await Task.Delay(1000, token).ConfigureAwait(false);
                        continue;
                    }

                    if (!_wasFiveMRunning)
                    {
                        lock (SyncRoot)
                        {
                            _sessionStartedAt = DateTime.MinValue;
                            _previousVisibleLines.Clear();

                            if (File.Exists(SessionFilePath) && new FileInfo(SessionFilePath).Length > 0)
                            {
                                string previousSessionFile = Path.Combine(SessionDirectory, "previous-session.txt");
                                try
                                {
                                    File.Copy(SessionFilePath, previousSessionFile, true);
                                }
                                catch
                                {
                                    // Non-critical fallback
                                }
                            }

                            File.WriteAllText(SessionFilePath, string.Empty, new UTF8Encoding(false));
                        }
                        _wasFiveMRunning = true;
                        CaptureState = FiveMChatCaptureState.WaitingForChat;
                    }

                    List<string> visibleLines = await Reader.GetChatLinesAsync(token).ConfigureAwait(false);
                    lock (SyncRoot)
                    {
                        AppendNewLines(visibleLines);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    CaptureState = FiveMChatCaptureState.WaitingForChat;
                    lock (SyncRoot)
                    {
                        Reader.Close();
                    }
                    Log.Debug(ex, "FiveM chat capture poll caught an exception (HUD may be reloading)");
                }

                try
                {
                    await Task.Delay(PollIntervalMilliseconds, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private static void AppendNewLines(IList<string> visibleLines)
        {
            List<string> current = visibleLines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList();

            if (current.Count == 0)
                return;

            CaptureState = FiveMChatCaptureState.Capturing;

            int overlap = FindOverlap(_previousVisibleLines, current);
            List<string> newLines = current.Skip(overlap).ToList();
            if (newLines.Count == 0)
            {
                _previousVisibleLines = current;
                return;
            }

            DateTime capturedAt = DateTime.Now;
            DateTime sessionTimestamp = GetTimestamp(newLines[0], capturedAt);
            bool startOfSession = !File.Exists(SessionFilePath) || new FileInfo(SessionFilePath).Length == 0;

            using (FileStream stream = new FileStream(SessionFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                if (startOfSession)
                {
                    _sessionStartedAt = sessionTimestamp;
                    writer.WriteLine(CreateSessionHeader(sessionTimestamp));
                }

                foreach (string line in newLines)
                {
                    string formatted = AddTimestamp(line, capturedAt);
                    writer.WriteLine(formatted);
                    LineReceived?.Invoke(formatted);
                }
            }

            _previousVisibleLines = current;
        }

        public static void AppendLinesToSession(IEnumerable<string> lines)
        {
            if (lines == null) return;
            DateTime capturedAt = DateTime.Now;
            bool startOfSession = !File.Exists(SessionFilePath) || new FileInfo(SessionFilePath).Length == 0;

            using (FileStream stream = new FileStream(SessionFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                if (startOfSession)
                {
                    writer.WriteLine(CreateSessionHeader(capturedAt));
                }

                foreach (string line in lines)
                {
                    string formatted = AddTimestamp(line, capturedAt);
                    writer.WriteLine(formatted);
                    LineReceived?.Invoke(formatted);
                }
            }
        }

        public static string CreateSessionHeader(DateTime timestamp)
        {
            string date = timestamp.ToString("dd/MMM/yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
            return string.Format(CultureInfo.InvariantCulture, "[DATE: {0} | TIME: {1}]", date, timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        }

        public static string AddTimestamp(string line, DateTime capturedAt)
        {
            if (TimestampPrefix.IsMatch(line))
                return line;

            return string.Format(CultureInfo.InvariantCulture, "[{0}] {1}", capturedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture), line);
        }

        public static DateTime GetTimestamp(string line, DateTime fallback)
        {
            Match match = TimestampPrefix.Match(line);
            if (!match.Success || !DateTime.TryParseExact(match.Groups["time"].Value, "H:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                return fallback;

            return fallback.Date.Add(parsed.TimeOfDay);
        }

        /// <summary>
        /// Finds the overlap length between the end of <paramref name="oldLines"/> and the beginning of <paramref name="newLines"/>.
        /// </summary>
        public static int FindOverlap(IList<string> oldLines, IList<string> newLines)
        {
            if (oldLines == null || newLines == null || oldLines.Count == 0 || newLines.Count == 0)
                return 0;

            int max = Math.Min(oldLines.Count, newLines.Count);
            for (int length = max; length > 0; length--)
            {
                bool matches = true;
                for (int i = 0; i < length; i++)
                {
                    if (!string.Equals(oldLines[oldLines.Count - length + i], newLines[i], StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    return length;
            }

            return 0;
        }

        private static readonly Regex TimestampRegex = new Regex(@"\[\d{1,2}:\d{1,2}:\d{1,2}\] ", RegexOptions.Compiled);

        private sealed class NuiChatReader
        {
            private static readonly HttpClientHandler HttpHandler = new HttpClientHandler { UseProxy = false };
            private static readonly HttpClient HttpClient = new HttpClient(HttpHandler) { Timeout = TimeSpan.FromSeconds(2) };

            private ClientWebSocket? _socket;
            private int _contextId;
            private int _requestId;

            public async Task<List<string>> GetChatLinesAsync(CancellationToken token = default)
            {
                await EnsureConnectedAsync(token).ConfigureAwait(false);

                const string expression = "JSON.stringify(Array.from(document.querySelectorAll('.chat__messages > li'), el => { const text = (el.innerText || '').replace(/\\s+/g, ' ').trim(); if (!text) return ''; const nodes = [el].concat(Array.from(el.querySelectorAll('*'))); let timestamp = ''; for (const node of nodes) { for (const attribute of Array.from(node.attributes || [])) { const match = String(attribute.value).match(/\\b\\d{1,2}:\\d{2}:\\d{2}\\b/); if (match) { timestamp = match[0]; break; } } if (!timestamp) { const match = String(getComputedStyle(node, '::before').content || '').match(/\\b\\d{1,2}:\\d{2}:\\d{2}\\b/); if (match) timestamp = match[0]; } if (timestamp) break; } return (timestamp ? '[' + timestamp + '] ' : '') + text; }).filter(Boolean))";

                using JsonDocument result = await SendCdpRequestAsync("Runtime.evaluate", new
                {
                    expression = expression,
                    contextId = _contextId,
                    returnByValue = true
                }, token).ConfigureAwait(false);

                if (result.RootElement.TryGetProperty("result", out JsonElement resultObj) &&
                    resultObj.TryGetProperty("value", out JsonElement valElem) &&
                    valElem.ValueKind == JsonValueKind.String)
                {
                    string jsonString = valElem.GetString() ?? "[]";
                    List<string>? lines = JsonSerializer.Deserialize<List<string>>(jsonString);
                    return lines?.Where(l => !string.IsNullOrWhiteSpace(l)).ToList() ?? new List<string>();
                }

                return new List<string>();
            }

            public void Close()
            {
                if (_socket != null)
                {
                    try { _socket.Abort(); } catch { }
                    _socket.Dispose();
                    _socket = null;
                }

                _contextId = 0;
                _requestId = 0;
            }

            private async Task EnsureConnectedAsync(CancellationToken token)
            {
                if (_socket != null && _socket.State == WebSocketState.Open && _contextId != 0)
                    return;

                Close();

                string socketUrl = await GetRootWebSocketUrlAsync(token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(socketUrl))
                    throw new IOException("FiveM NUI DevTools is unavailable.");

                _socket = new ClientWebSocket();
                _socket.Options.Proxy = null;

                using (CancellationTokenSource connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeout.Token))
                {
                    await _socket.ConnectAsync(new Uri(socketUrl), linked.Token).ConfigureAwait(false);
                }

                using JsonDocument frameTreeDoc = await SendCdpRequestAsync("Page.getFrameTree", new { }, token).ConfigureAwait(false);
                string? frameId = FindClientFrameId(frameTreeDoc.RootElement);
                if (string.IsNullOrEmpty(frameId))
                    throw new IOException("GTAW HUD is not ready.");

                using JsonDocument worldDoc = await SendCdpRequestAsync("Page.createIsolatedWorld", new
                {
                    frameId = frameId,
                    worldName = "gtaw-log-parser-reader",
                    grantUniveralAccess = true
                }, token).ConfigureAwait(false);

                if (worldDoc.RootElement.TryGetProperty("executionContextId", out JsonElement ctxElem) &&
                    ctxElem.TryGetInt32(out int ctxId))
                {
                    _contextId = ctxId;
                }
                else
                {
                    throw new IOException("GTAW HUD execution context is unavailable.");
                }
            }

            private static async Task<string> GetRootWebSocketUrlAsync(CancellationToken token)
            {
                string json = await HttpClient.GetStringAsync(DevToolsTargetsUrl, token).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement target in doc.RootElement.EnumerateArray())
                    {
                        if (target.TryGetProperty("url", out JsonElement urlProp) &&
                            urlProp.GetString() == RootUiUrl &&
                            target.TryGetProperty("webSocketDebuggerUrl", out JsonElement wsProp))
                        {
                            return wsProp.GetString() ?? string.Empty;
                        }
                    }
                }

                throw new IOException("FiveM root UI was not found.");
            }

            private async Task<JsonDocument> SendCdpRequestAsync(string method, object parameters, CancellationToken token)
            {
                if (_socket == null || _socket.State != WebSocketState.Open)
                    throw new IOException("WebSocket is not connected.");

                int id = Interlocked.Increment(ref _requestId);
                string requestJson = JsonSerializer.Serialize(new
                {
                    id = id,
                    method = method,
                    @params = parameters
                });

                byte[] data = Encoding.UTF8.GetBytes(requestJson);
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token))
                {
                    await _socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, linked.Token).ConfigureAwait(false);

                    while (true)
                    {
                        JsonDocument responseDoc = await ReceiveCdpMessageAsync(linked.Token).ConfigureAwait(false);
                        if (responseDoc.RootElement.TryGetProperty("id", out JsonElement idElem) && idElem.GetInt32() == id)
                        {
                            if (responseDoc.RootElement.TryGetProperty("error", out _))
                            {
                                responseDoc.Dispose();
                                throw new IOException($"FiveM CDP error for method {method}.");
                            }

                            if (responseDoc.RootElement.TryGetProperty("result", out JsonElement resultElem))
                            {
                                // Clone result to return independent document
                                return JsonDocument.Parse(resultElem.GetRawText());
                            }

                            return JsonDocument.Parse("{}");
                        }

                        responseDoc.Dispose();
                    }
                }
            }

            private async Task<JsonDocument> ReceiveCdpMessageAsync(CancellationToken token)
            {
                if (_socket == null)
                    throw new IOException("WebSocket is null.");

                byte[] buffer = new byte[8192];
                using MemoryStream ms = new MemoryStream();

                while (true)
                {
                    WebSocketReceiveResult result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new IOException("FiveM NUI DevTools connection closed.");

                    ms.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                        break;
                }

                ms.Position = 0;
                return await JsonDocument.ParseAsync(ms, default, token).ConfigureAwait(false);
            }

            private static string? FindClientFrameId(JsonElement root)
            {
                if (root.TryGetProperty("frameTree", out JsonElement frameTree))
                {
                    return SearchFrame(frameTree);
                }
                return SearchFrame(root);
            }

            private static string? SearchFrame(JsonElement frameTree)
            {
                if (frameTree.TryGetProperty("frame", out JsonElement frame))
                {
                    if (frame.TryGetProperty("url", out JsonElement urlElem) &&
                        urlElem.GetString()?.StartsWith(ClientFrameUrl, StringComparison.OrdinalIgnoreCase) == true &&
                        frame.TryGetProperty("id", out JsonElement idElem))
                    {
                        return idElem.GetString();
                    }
                }

                if (frameTree.TryGetProperty("childFrames", out JsonElement childFrames) &&
                    childFrames.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement child in childFrames.EnumerateArray())
                    {
                        string? found = SearchFrame(child);
                        if (!string.IsNullOrEmpty(found))
                            return found;
                    }
                }

                return null;
            }
        }
    }
}
