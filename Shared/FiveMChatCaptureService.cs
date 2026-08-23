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
        private static readonly List<CapturedChatLine> _sessionRichLines = new List<CapturedChatLine>();
        private static readonly Regex TimestampPrefix = new Regex(@"^\[(?<time>\d{1,2}:\d{2}:\d{2})\]\s+", RegexOptions.Compiled);
        private static FiveMChatCaptureState _captureState = FiveMChatCaptureState.WaitingForFiveM;

        public static event Action<FiveMChatCaptureState>? StateChanged;
        public static event Action<string>? LineReceived;
        public static event Action<CapturedChatLine>? CapturedLineReceived;

        public static DateTime SessionStartedAt => _sessionStartedAt == DateTime.MinValue ? DateTime.Now : _sessionStartedAt;

        public static IReadOnlyList<CapturedChatLine> SessionRichLines
        {
            get
            {
                lock (SyncRoot) return _sessionRichLines.ToList();
            }
        }

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

        /// <summary>
        /// Captures a direct, on-demand snapshot of visible chat lines with full color and span data from FiveM NUI.
        /// </summary>
        public static async Task<List<CapturedChatLine>> GetCapturedChatLinesAsync(CancellationToken token = default)
        {
            return await Reader.GetCapturedChatLinesAsync(token).ConfigureAwait(false);
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

                            if (File.Exists(SessionFilePath))
                            {
                                string backupFile = Path.Combine(SessionDirectory, "previous-session.txt");
                                try
                                {
                                    File.Copy(SessionFilePath, backupFile, true);
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning(ex, "Failed to copy current-session.txt to previous-session.txt before clearing");
                                }
                            }

                            File.WriteAllText(SessionFilePath, string.Empty, new UTF8Encoding(false));
                            lock (SyncRoot)
                            {
                                _sessionRichLines.Clear();
                            }
                        }
                        _wasFiveMRunning = true;
                        CaptureState = FiveMChatCaptureState.WaitingForChat;
                    }

                    List<CapturedChatLine> visibleLines = await Reader.GetCapturedChatLinesAsync(token).ConfigureAwait(false);
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

        private static void AppendNewLines(IList<CapturedChatLine> visibleLines)
        {
            List<CapturedChatLine> current = visibleLines
                .Where(line => !string.IsNullOrWhiteSpace(line.Text))
                .ToList();

            if (current.Count == 0)
                return;

            CaptureState = FiveMChatCaptureState.Capturing;

            List<string> currentTexts = current.Select(line => line.Text.Trim()).ToList();
            int overlap = FindOverlap(_previousVisibleLines, currentTexts);
            List<CapturedChatLine> newLines = current.Skip(overlap).ToList();
            if (newLines.Count == 0)
            {
                _previousVisibleLines = currentTexts;
                return;
            }

            DateTime capturedAt = DateTime.Now;
            DateTime sessionTimestamp = GetTimestamp(newLines[0].Text, capturedAt);
            bool startOfSession = !File.Exists(SessionFilePath) || new FileInfo(SessionFilePath).Length == 0;

            using (FileStream stream = new FileStream(SessionFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                if (startOfSession)
                {
                    _sessionStartedAt = sessionTimestamp;
                    writer.WriteLine(CreateSessionHeader(sessionTimestamp));
                }

                foreach (CapturedChatLine line in newLines)
                {
                    string formatted = AddTimestamp(line.Text, capturedAt);
                    line.Text = formatted;
                    _sessionRichLines.Add(line);
                    writer.WriteLine(formatted);
                    LineReceived?.Invoke(formatted);
                    CapturedLineReceived?.Invoke(line);
                }
            }

            _previousVisibleLines = currentTexts;
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
                    CapturedLineReceived?.Invoke(new CapturedChatLine(formatted));
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

            public async Task<List<CapturedChatLine>> GetCapturedChatLinesAsync(CancellationToken token = default)
            {
                await EnsureConnectedAsync(token).ConfigureAwait(false);

                const string expression = @"(() => {
                    const namedColors = {
                        'red': '#FF0000', 'darkred': '#8B0000', 'crimson': '#DC143C',
                        'green': '#31CB31', 'darkgreen': '#006400', 'lime': '#31CB31', 'limegreen': '#32CD32',
                        'blue': '#1E90FF', 'darkblue': '#00008B', 'navy': '#000080', 'dodgerblue': '#1E90FF',
                        'deepskyblue': '#00BFFF', 'skyblue': '#87CEEB', 'yellow': '#FFFF00', 'gold': '#FFD700',
                        'goldenrod': '#DAA520', 'white': '#FFFFFF', 'black': '#000000', 'orange': '#FFA500',
                        'darkorange': '#FF8C00', 'coral': '#FF7F50', 'purple': '#C2A2DA', 'darkpurple': '#800080',
                        'magenta': '#FF00FF', 'fuchsia': '#FF00FF', 'pink': '#FF69B4', 'hotpink': '#FF69B4',
                        'deepink': '#FF1493', 'gray': '#A6ACAF', 'grey': '#A6ACAF', 'darkgray': '#666666',
                        'darkgrey': '#666666', 'lightgray': '#D3D3D3', 'lightgrey': '#D3D3D3', 'silver': '#C0C0C0',
                        'teal': '#48C9B0', 'cyan': '#00FFFF', 'aqua': '#00FFFF', 'olive': '#808000',
                        'maroon': '#800000', 'brown': '#A52A2A', 'wheat': '#F5DEB3', 'khaki': '#F0E68C'
                    };

                    const tildeMap = {
                        '~r~': '#FF0000', '~g~': '#31CB31', '~b~': '#1E90FF', '~y~': '#FFFF00',
                        '~p~': '#C2A2DA', '~q~': '#FF69B4', '~o~': '#FFA500', '~c~': '#A6ACAF',
                        '~m~': '#666666', '~u~': '#000000', '~w~': '#FFFFFF', '~s~': '#FFFFFF', '~h~': '#FFFFFF'
                    };

                    const fivemColorMap = {
                        '^0': '#FFFFFF', '^1': '#FF0000', '^2': '#31CB31', '^3': '#FFFF00',
                        '^4': '#1E90FF', '^5': '#48C9B0', '^6': '#C2A2DA', '^7': '#FFFFFF',
                        '^8': '#8B0000', '^9': '#FF69B4'
                    };

                    const hexCache = new Map();
                    function parseHex(c) {
                        if (!c) return '';
                        if (hexCache.has(c)) return hexCache.get(c);

                        let res = '';
                        const s = String(c).trim().toLowerCase();
                        if (namedColors[s]) {
                            res = namedColors[s];
                        } else if (s === 'transparent' || s === 'inherit' || s === 'initial' || s === 'unset') {
                            res = '';
                        } else if (s.startsWith('#')) {
                            if (s.length === 4) {
                                res = ('#' + s[1] + s[1] + s[2] + s[2] + s[3] + s[3]).toUpperCase();
                            } else if (s.length >= 7) {
                                res = s.substring(0, 7).toUpperCase();
                            }
                        } else {
                            const rgb = s.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
                            if (rgb) {
                                const r = Math.min(255, parseInt(rgb[1], 10)).toString(16).padStart(2, '0');
                                const g = Math.min(255, parseInt(rgb[2], 10)).toString(16).padStart(2, '0');
                                const b = Math.min(255, parseInt(rgb[3], 10)).toString(16).padStart(2, '0');
                                res = ('#' + r + g + b).toUpperCase();
                            }
                        }

                        hexCache.set(c, res);
                        return res;
                    }

                    function isNonDefaultColor(c) {
                        return c && c !== '#FFFFFF' && c !== '#DCDCDC' && c !== '#F0F0F0' && c !== '#000000';
                    }

                    const computedColorCache = new WeakMap();
                    function getNodeColor(node, rootEl) {
                        let p = node.nodeType === 1 ? node : node.parentElement;
                        let targetElem = p;

                        // 1. Fast check for inline attributes / style without triggering style recalculation
                        while (p && p !== rootEl.parentElement) {
                            if (p.getAttribute) {
                                const attrColor = p.getAttribute('color');
                                if (attrColor) {
                                    const c = parseHex(attrColor);
                                    if (c) return c;
                                }
                                const dataColor = p.getAttribute('data-color');
                                if (dataColor) {
                                    const c = parseHex(dataColor);
                                    if (c) return c;
                                }
                                const styleAttr = p.getAttribute('style');
                                if (styleAttr && styleAttr.indexOf('color') !== -1) {
                                    const m = styleAttr.match(/color\s*:\s*([^;]+)/i);
                                    if (m) {
                                        const c = parseHex(m[1]);
                                        if (c) return c;
                                    }
                                }
                            }
                            if (p.style && p.style.color) {
                                const c = parseHex(p.style.color);
                                if (c) return c;
                            }
                            p = p.parentElement;
                        }

                        // 2. Computed style lookup (cached per element, inherits cascade from ancestors)
                        if (targetElem) {
                            if (computedColorCache.has(targetElem)) {
                                return computedColorCache.get(targetElem);
                            }
                            try {
                                const comp = window.getComputedStyle(targetElem);
                                if (comp && comp.color) {
                                    const c = parseHex(comp.color);
                                    if (c) {
                                        computedColorCache.set(targetElem, c);
                                        return c;
                                    }
                                }
                            } catch (e) {}
                        }

                        return '#FFFFFF';
                    }

                    function parseTextCodes(rawText, baseColor) {
                        if (!rawText) return [];
                        const pattern = /(~[rgbypqocmuwsh]~)|(\{!?(?:#)?([0-9a-fA-F]{6})\})|(\^([0-9]))/g;
                        let lastIndex = 0;
                        let currentColor = baseColor || '#FFFFFF';
                        const subSpans = [];
                        let match;

                        while ((match = pattern.exec(rawText)) !== null) {
                            if (match.index > lastIndex) {
                                const segment = rawText.substring(lastIndex, match.index);
                                if (segment) subSpans.push({ t: segment, c: currentColor });
                            }
                            if (match[1]) {
                                currentColor = tildeMap[match[1].toLowerCase()] || currentColor;
                            } else if (match[3]) {
                                currentColor = '#' + match[3].toUpperCase();
                            } else if (match[5]) {
                                currentColor = fivemColorMap['^' + match[5]] || currentColor;
                            }
                            lastIndex = pattern.lastIndex;
                        }

                        if (lastIndex < rawText.length) {
                            const segment = rawText.substring(lastIndex);
                            if (segment) subSpans.push({ t: segment, c: currentColor });
                        }

                        return subSpans.length > 0 ? subSpans : [{ t: rawText, c: baseColor || '#FFFFFF' }];
                    }

                    const selector = '.chat__messages > li, .chat-messages > li, .chat-messages > div, #chat-messages > li, #chat-messages > div, .chat__message, .chat-message, #messages > div, #messages > li';
                    let items = Array.from(document.querySelectorAll(selector));
                    if (items.length === 0) {
                        items = Array.from(document.querySelectorAll('.chat__messages li, .chat-messages li, #chat-messages li, #messages li'));
                    }
                    const results = [];

                    for (const el of items) {
                        const fullText = (el.innerText || '').replace(/\r?\n/g, ' ').replace(/\s+/g, ' ').trim();
                        if (!fullText) continue;

                        let timestamp = '';
                        if (el.getAttribute) {
                            const tsAttr = el.getAttribute('data-timestamp') || el.getAttribute('timestamp');
                            if (tsAttr) {
                                const m = String(tsAttr).match(/\b\d{1,2}:\d{2}:\d{2}\b/);
                                if (m) timestamp = m[0];
                            }
                        }

                        const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT, null, false);
                        const rawSpans = [];
                        let curr = walker.nextNode();
                        while (curr) {
                            const val = curr.nodeValue;
                            if (val && val.length > 0) {
                                const color = getNodeColor(curr, el);
                                const parsedSubSpans = parseTextCodes(val, color);
                                for (let i = 0; i < parsedSubSpans.length; i++) {
                                    rawSpans.push(parsedSubSpans[i]);
                                }
                            }
                            curr = walker.nextNode();
                        }

                        const mergedSpans = [];
                        for (let i = 0; i < rawSpans.length; i++) {
                            const s = rawSpans[i];
                            if (mergedSpans.length > 0 && mergedSpans[mergedSpans.length - 1].c === s.c) {
                                mergedSpans[mergedSpans.length - 1].t += s.t;
                            } else {
                                mergedSpans.push({ t: s.t, c: s.c });
                            }
                        }

                        let dominantColor = '#FFFFFF';
                        for (let i = 0; i < mergedSpans.length; i++) {
                            if (isNonDefaultColor(mergedSpans[i].c)) {
                                dominantColor = mergedSpans[i].c;
                                break;
                            }
                        }

                        const lineWithTs = (timestamp && !fullText.startsWith('[' + timestamp + ']'))
                            ? ('[' + timestamp + '] ' + fullText)
                            : fullText;

                        results.push({
                            t: lineWithTs,
                            c: dominantColor,
                            s: mergedSpans
                        });
                    }

                    return JSON.stringify(results);
                })();";

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
                    List<CapturedChatLine>? lines = JsonSerializer.Deserialize<List<CapturedChatLine>>(jsonString);
                    return lines?.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList() ?? new List<CapturedChatLine>();
                }

                return new List<CapturedChatLine>();
            }

            public async Task<List<string>> GetChatLinesAsync(CancellationToken token = default)
            {
                var captured = await GetCapturedChatLinesAsync(token).ConfigureAwait(false);
                return captured.Select(c => c.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
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
                        frame.TryGetProperty("id", out JsonElement idElem))
                    {
                        string? url = urlElem.GetString();
                        if (!string.IsNullOrEmpty(url) &&
                            (url.StartsWith(ClientFrameUrl, StringComparison.OrdinalIgnoreCase) ||
                             url.IndexOf("cfx-nui-client", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             url.IndexOf("cfx-nui-chat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             url.IndexOf("chat/html", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             url.IndexOf("chat/web", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            return idElem.GetString();
                        }
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
