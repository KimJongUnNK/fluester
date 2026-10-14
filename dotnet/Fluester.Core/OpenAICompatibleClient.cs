using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fluester.Core;

/// <summary>Fehler mit verständlicher Meldung für Leiste und Dialoge.</summary>
public sealed class FluesterException(string message) : Exception(message);

/// <summary>Minimaler Client für OpenAI-kompatible Endpunkte (Groq, OpenAI, Ollama, LM Studio …).</summary>
public sealed class OpenAICompatibleClient(string baseUrl, string? apiKey, TimeSpan? timeout = null)
{
    public string BaseUrl { get; } = baseUrl;
    public string? ApiKey { get; } = apiKey;
    public TimeSpan Timeout { get; } = timeout ?? TimeSpan.FromSeconds(60);

    static readonly HttpClient Http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    public Uri Url(string path)
    {
        var b = (BaseUrl ?? "").Trim().TrimEnd('/');
        if (b.Length == 0 || !Uri.TryCreate(b + path, UriKind.Absolute, out var u))
            throw new FluesterException($"Ungültige Server-Adresse: {BaseUrl}");
        return u;
    }

    // ───────────── Spracherkennung

    public HttpRequestMessage TranscriptionRequest(byte[] wav, string model, string? language, string? prompt)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Url("/audio/transcriptions"));
        if (!string.IsNullOrEmpty(ApiKey)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        var form = new MultipartFormDataContent("fluester-" + Guid.NewGuid().ToString("N"));
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "diktat.wav");
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("0"), "temperature");
        if (!string.IsNullOrEmpty(language) && language != "auto") form.Add(new StringContent(language[..Math.Min(2, language.Length)]), "language");
        if (!string.IsNullOrEmpty(prompt)) form.Add(new StringContent(prompt[..Math.Min(800, prompt.Length)]), "prompt");
        req.Content = form;
        return req;
    }

    public async Task<string> TranscribeAsync(byte[] wav, string model, string? language, string? prompt, CancellationToken ct = default) =>
        ParseTranscription(await SendAsync(TranscriptionRequest(wav, model, language, prompt), ct));

    public static string ParseTranscription(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject o && o["text"] is JsonValue v && v.TryGetValue<string>(out var text)) return text.Trim();
        }
        catch (JsonException) { }
        if (!body.TrimStart().StartsWith('{')) return body.Trim();
        throw new FluesterException("Unerwartete Antwort der Spracherkennung");
    }

    // ───────────── Chat

    public HttpRequestMessage ChatRequest(string model, string system, string user, double temperature = 0.2)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Url("/chat/completions"));
        if (!string.IsNullOrEmpty(ApiKey)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        var body = new JsonObject
        {
            ["model"] = model,
            ["temperature"] = temperature,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user },
            },
        };
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return req;
    }

    public async Task<string> ChatAsync(string model, string system, string user, CancellationToken ct = default) =>
        ParseChat(await SendAsync(ChatRequest(model, system, user), ct));

    public static string ParseChat(string body)
    {
        try
        {
            var content = JsonNode.Parse(body)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
            if (content != null) return content;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { }
        throw new FluesterException("Unerwartete Antwort des Sprachmodells");
    }

    // ───────────── Transport

    async Task<string> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new FluesterException($"Zeitüberschreitung nach {(int)Timeout.TotalSeconds} s");
        }
        catch (HttpRequestException e)
        {
            throw new FluesterException("Server nicht erreichbar: " + e.Message);
        }
        var text = await resp.Content.ReadAsStringAsync(cts.Token);
        if (!resp.IsSuccessStatusCode) throw new FluesterException(Describe((int)resp.StatusCode, text));
        return text;
    }

    public static string Describe(int status, string body)
    {
        string detail = "";
        try
        {
            if (JsonNode.Parse(body) is JsonObject o)
            {
                if (o["error"] is JsonObject e && e["message"] is JsonValue m) detail = m.ToString();
                else if (o["error"] is JsonValue ev) detail = ev.ToString();
                else if (o["message"] is JsonValue mv) detail = mv.ToString();
            }
        }
        catch (JsonException) { }
        string D(string sep) => detail.Length == 0 ? "" : sep;
        return status switch
        {
            401 or 403 => "API-Schlüssel ungültig oder fehlt" + D($" ({detail})"),
            404 => "Modell oder Adresse nicht gefunden" + D($" ({detail})"),
            413 => "Aufnahme zu groß für den Server",
            429 => "Limit erreicht – kurz warten oder anderes Modell wählen" + D($" ({detail})"),
            _ => $"Server-Fehler {status}" + D($": {detail}"),
        };
    }
}
