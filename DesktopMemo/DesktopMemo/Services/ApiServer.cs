using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DesktopMemo.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DesktopMemo.Services
{
    public class ApiServer : IDisposable
    {
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private readonly int _port;
        private readonly SynchronizationContext _uiContext;

        public ApiServer(int port, SynchronizationContext uiContext)
        {
            _port = port;
            _uiContext = uiContext;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");

            try
            {
                _listener.Start();
                Task.Run(() => ListenLoop(_cts.Token));
                System.Diagnostics.Debug.WriteLine($"API server started on port {_port}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"API server start error: {ex.Message}");
            }
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Listen error: {ex.Message}");
                    await Task.Delay(100);
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // CORS headers
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                SendResponse(response, 200, "");
                return;
            }

            try
            {
                var path = request.Url.AbsolutePath.TrimEnd('/');
                var method = request.HttpMethod.ToUpper();

                if (path == "/api/health" && method == "GET")
                {
                    SendJson(response, 200, new { status = "ok", port = _port });
                }
                else if (path == "/api/notes" && method == "GET")
                {
                    var notes = RunOnUI(() => NoteStore.Instance.GetAllNotes());
                    SendJson(response, 200, notes);
                }
                else if (path == "/api/notes" && method == "POST")
                {
                    var body = ReadBody(request);
                    var noteData = JsonConvert.DeserializeObject<Note>(body);
                    if (noteData == null)
                    {
                        SendError(response, 400, "Invalid request body");
                        return;
                    }

                    var pos = AutoPositioner.FindAvailablePosition(
                        noteData.Width > 0 ? noteData.Width : 280,
                        noteData.Height > 0 ? noteData.Height : 320);
                    if (noteData.X == 0) noteData.X = pos.X;
                    if (noteData.Y == 0) noteData.Y = pos.Y;

                    var created = RunOnUI(() => NoteStore.Instance.AddNote(noteData));
                    SendJson(response, 201, created);
                }
                else if (path.StartsWith("/api/notes/") && method == "GET")
                {
                    var id = path.Substring("/api/notes/".Length);
                    var note = RunOnUI(() => NoteStore.Instance.GetNote(id));
                    if (note == null)
                        SendError(response, 404, "Note not found");
                    else
                        SendJson(response, 200, note);
                }
                else if (path.StartsWith("/api/notes/") && method == "PUT")
                {
                    var id = path.Substring("/api/notes/".Length);
                    var body = ReadBody(request);

                    // Validate JSON
                    try { JObject.Parse(body); }
                    catch { SendError(response, 400, "Invalid JSON"); return; }

                    var updated = RunOnUI(() => NoteStore.Instance.UpdateNote(id, body));
                    if (updated == null)
                        SendError(response, 404, "Note not found");
                    else
                        SendJson(response, 200, updated);
                }
                else if (path.StartsWith("/api/notes/") && method == "DELETE")
                {
                    var id = path.Substring("/api/notes/".Length);
                    var deleted = RunOnUI(() => NoteStore.Instance.DeleteNote(id));
                    if (!deleted)
                        SendError(response, 404, "Note not found");
                    else
                        SendJson(response, 200, new { message = "Deleted" });
                }
                else
                {
                    SendError(response, 404, "Not found");
                }
            }
            catch (JsonException)
            {
                SendError(response, 400, "Invalid JSON");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Request error: {ex.Message}");
                SendError(response, 500, "Internal server error");
            }
        }

        private T RunOnUI<T>(Func<T> action)
        {
            T result = default(T);
            var mre = new ManualResetEventSlim(false);
            _uiContext.Post(_ =>
            {
                try { result = action(); }
                catch { }
                mre.Set();
            }, null);
            mre.Wait(5000);
            return result;
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }

        private static void SendJson(HttpListenerResponse response, int statusCode, object data)
        {
            var json = JsonConvert.SerializeObject(data, new JsonSerializerSettings
            {
                DateFormatString = "yyyy-MM-ddTHH:mm:ssZ",
                Formatting = Formatting.Indented
            });
            SendResponse(response, statusCode, json, "application/json");
        }

        private static void SendError(HttpListenerResponse response, int statusCode, string message)
        {
            var json = JsonConvert.SerializeObject(new { error = message });
            SendResponse(response, statusCode, json, "application/json");
        }

        private static void SendResponse(HttpListenerResponse response, int statusCode, string body,
            string contentType = "text/plain")
        {
            response.StatusCode = statusCode;
            response.ContentType = contentType + "; charset=utf-8";
            var buffer = Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
            _cts?.Dispose();
        }
    }
}
