using System.Diagnostics;
using System.Net;

namespace InfoPanel.Media.Services;

public sealed class CoverArtServer : IDisposable
{
    private readonly string _coverArtFilePath;
    private readonly int _requestedPort;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private int _actualPort;
    private bool _disposed;

    public CoverArtServer(string coverArtFilePath, int port)
    {
        _coverArtFilePath = coverArtFilePath;
        _requestedPort = port;
    }

    public string? CoverArtUrl => _listener?.IsListening == true
        ? $"http://localhost:{_actualPort}/cover"
        : null;

    public void Start()
    {
        if (_requestedPort == 0)
        {
            Debug.WriteLine("[Media] Cover art server disabled (port=0).");
            return;
        }

        // Try requested port, then +1, then +2
        int[] portsToTry = [_requestedPort, _requestedPort + 1, _requestedPort + 2];

        foreach (var port in portsToTry)
        {
            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();

                _listener = listener;
                _actualPort = port;
                _cts = new CancellationTokenSource();
                _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));

                Debug.WriteLine($"[Media] Cover art server started on port {port}.");
                return;
            }
            catch (HttpListenerException ex)
            {
                Debug.WriteLine($"[Media] Port {port} unavailable: {ex.Message}");
            }
        }

        Debug.WriteLine("[Media] Cover art server failed to start — all ports unavailable. Falling back to file path mode.");
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct);
                _ = HandleRequestAsync(context);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Media] Cover art server listen error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            if (request.Url?.AbsolutePath != "/cover")
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            if (!File.Exists(_coverArtFilePath))
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            try
            {
                byte[] fileBytes;
                using (var fs = new FileStream(_coverArtFilePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    fileBytes = new byte[fs.Length];
                    await fs.ReadExactlyAsync(fileBytes);
                }

                response.ContentType = "image/png";
                response.ContentLength64 = fileBytes.Length;
                response.Headers.Set("Cache-Control", "no-cache, no-store, must-revalidate");
                response.Headers.Set("Pragma", "no-cache");
                response.Headers.Set("Expires", "0");
                await response.OutputStream.WriteAsync(fileBytes);
                response.Close();
            }
            catch (IOException)
            {
                response.StatusCode = 503;
                response.Headers.Set("Retry-After", "1");
                response.Close();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Media] Cover art server request error: {ex.Message}");
            try { context.Response.Close(); } catch { /* best effort */ }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();

        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch (ObjectDisposedException) { }

        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException) { }

        _cts?.Dispose();
        _listener = null;

        Debug.WriteLine("[Media] Cover art server disposed.");
    }
}
