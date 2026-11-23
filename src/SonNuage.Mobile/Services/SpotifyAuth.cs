using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SonNuage.Contracts;
using SonNuage.UI.Services;

namespace SonNuage.Mobile.Services;

/// <summary>
/// Connexion à Spotify sur le téléphone (OAuth PKCE, redirection en boucle locale), comme l'appli de bureau Spotify :
/// ouvre la page de connexion dans un onglet système, récupère le code sur <c>http://127.0.0.1:&lt;port&gt;/login</c>, puis
/// l'échange contre des jetons. Les jetons sont renvoyés à l'appli, qui les confie au serveur. Aucun secret client
/// n'est nécessaire (PKCE).
/// </summary>
public sealed class SpotifyAuth : IMusicServiceAuth
{
    private const string Scopes = "streaming user-read-playback-state user-read-currently-playing";

    public bool CanConnect(string service) => service == Contracts.Jam.Spotify;

    public async Task<MusicServiceTokenRequest?> ConnectAsync(string service, CancellationToken ct)
    {
        if (service != Contracts.Jam.Spotify) return null;

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        // Lie la redirection à cette demande : une autre appli du téléphone ne peut pas y glisser son propre code.
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://127.0.0.1:{port}/login";

        var authorizeUrl = "https://accounts.spotify.com/authorize?response_type=code" +
            $"&client_id={SpotifyClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            "&code_challenge_method=S256" +
            $"&code_challenge={challenge}" +
            $"&state={state}" +
            $"&scope={Uri.EscapeDataString(Scopes)}";

        // Fenêtre d'attente : l'utilisateur se connecte dans l'onglet, puis revient. Au-delà, on abandonne.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        // Revenu dans l'appli sans redirection (onglet fermé, connexion abandonnée) : on n'attend plus que le temps
        // qu'elle arrive, sinon le bouton resterait occupé cinq minutes.
        var left = false;
        void OnActivity(object? sender, ActivityStateChangedEventArgs e)
        {
            if (e.State is ActivityState.Paused or ActivityState.Stopped) left = true;
            else if (e.State == ActivityState.Resumed && left) timeout.CancelAfter(ReturnGrace);
        }
        Platform.ActivityStateChanged += OnActivity;
        string? code;
        try
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() => Browser.Default.OpenAsync(new Uri(authorizeUrl), BrowserLaunchMode.SystemPreferred));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Aucun navigateur, ou onglet refusé : une erreur que l'interface affiche, pas un plantage.
                throw new HttpRequestException("Impossible d'ouvrir la page de connexion Spotify.", ex);
            }
            code = await WaitForCodeAsync(listener, state, timeout.Token);
        }
        finally
        {
            Platform.ActivityStateChanged -= OnActivity;
        }
        listener.Stop();
        if (code is null) return null;

        return await ExchangeAsync(code, verifier, redirectUri, ct);
    }

    /// <summary>Après le retour dans l'appli, le temps laissé à la redirection pour arriver.</summary>
    private static readonly TimeSpan ReturnGrace = TimeSpan.FromSeconds(3);

    /// <summary>Une connexion muette (pré-connexion du navigateur) n'est attendue que ce temps-là.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Attend la redirection de l'onglet sur la boucle locale et en extrait le paramètre <c>code</c>. Une connexion qui
    /// n'est pas cette redirection (pré-connexion du navigateur, favicon, autre appli), muette ou coupée en route, est
    /// ignorée : on attend la suivante.
    /// </summary>
    private static async Task<string?> WaitForCodeAsync(TcpListener listener, string state, CancellationToken ct)
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            using (client)
            {
                try
                {
                    if (await AnswerAsync(client, state, ct) is { } redirect) return redirect.Code;
                }
                catch (Exception ex) when (ex is IOException or SocketException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
                {
                    // Connexion coupée ou restée muette : la suivante peut être la bonne.
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
        }
    }

    private sealed record Redirect(string? Code);

    /// <summary>La redirection attendue (son code, null si l'utilisateur a refusé), ou null pour toute autre connexion.</summary>
    private static async Task<Redirect?> AnswerAsync(TcpClient client, string state, CancellationToken ct)
    {
        using var stream = client.GetStream();
        using var silent = CancellationTokenSource.CreateLinkedTokenSource(ct);
        silent.CancelAfter(ReadTimeout);
        if (LoopbackRedirect.Query(await LoopbackRedirect.ReadRequestLineAsync(stream, silent.Token)) is not { } query) return null;
        if (query.GetValueOrDefault("state") != state)
        {
            await RespondAsync(stream, "<h2>Lien inattendu</h2><p>Relancez la connexion depuis SonNuage.</p>", silent.Token);
            return null;
        }
        var code = query.GetValueOrDefault("code");
        try
        {
            await RespondAsync(stream, code is null
                ? "<h2>Connexion annulée</h2><p>Vous pouvez revenir à SonNuage.</p>"
                : "<h2>Spotify connecté ✓</h2><p>Revenez à SonNuage, c'est prêt.</p>", silent.Token);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            // La page de confirmation n'est qu'une politesse : le code est là.
        }
        return new Redirect(code);
    }

    private static async Task RespondAsync(NetworkStream stream, string body, CancellationToken ct)
    {
        var html = $"<!doctype html><meta charset=utf-8><title>SonNuage</title><body style=\"font-family:sans-serif;text-align:center;padding:3rem\">{body}</body>";
        var response = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(html)}\r\nConnection: close\r\n\r\n{html}";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct);
    }

    /// <summary>Échange le code contre des jetons (PKCE), et lit le nom affiché du compte.</summary>
    private static async Task<MusicServiceTokenRequest?> ExchangeAsync(string code, string verifier, string redirectUri, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = SpotifyClientId,
            ["code_verifier"] = verifier,
        });
        HttpResponseMessage res;
        try
        {
            res = await http.PostAsync("https://accounts.spotify.com/api/token", body, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Délai dépassé : une erreur réseau comme une autre, que l'interface affiche.
            throw new HttpRequestException("Spotify ne répond pas.", ex);
        }
        using var _ = res;
        if (!res.IsSuccessStatusCode) return null;
        string access;
        string? refresh;
        int expires;
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsByteArrayAsync(ct));
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var at) || at.GetString() is not { Length: > 0 } token) return null;
            access = token;
            refresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            expires = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Page de portail captif, proxy… : une réponse qui n'est pas celle de Spotify.
            throw new HttpRequestException("Réponse inattendue de Spotify.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException("Spotify ne répond pas.", ex);
        }

        string? displayName = null;
        try
        {
            using var me = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
            me.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
            using var meRes = await http.SendAsync(me, ct);
            if (meRes.IsSuccessStatusCode)
            {
                using var meDoc = JsonDocument.Parse(await meRes.Content.ReadAsByteArrayAsync(ct));
                if (meDoc.RootElement.TryGetProperty("display_name", out var dn)) displayName = dn.GetString();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            // Le nom est facultatif.
        }

        return new MusicServiceTokenRequest(Contracts.Jam.Spotify, access, refresh, expires, displayName);
    }

    /// <summary>Client id public de l'appli de bureau Spotify, seul reconnu par les endpoints de Jam.</summary>
    private const string SpotifyClientId = "65b708073fc0480ea92a077233ca87bd";

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
