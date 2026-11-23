namespace SonNuage.Mobile.Services;

/// <summary>La redirection OAuth reçue sur la boucle locale (voir <see cref="SpotifyAuth"/>).</summary>
public static class LoopbackRedirect
{
    /// <summary>
    /// Paramètres d'une requête « GET /login?… HTTP/1.1 » ; null pour toute autre requête, ou pour une connexion
    /// ouverte sans rien envoyer (pré-connexion du navigateur).
    /// </summary>
    public static Dictionary<string, string>? Query(string request)
    {
        var parts = request.Split('\n', 2)[0].Split(' ');
        if (parts is not ["GET", var target, ..] || !target.StartsWith("/login?", StringComparison.Ordinal)) return null;
        var query = new Dictionary<string, string>();
        foreach (var pair in target["/login?".Length..].Split('&'))
            if (pair.Split('=', 2) is [var k, var v]) query[k] = Uri.UnescapeDataString(v.Replace('+', ' '));
        return query;
    }

    /// <summary>Au-delà, la ligne de requête est coupée : une redirection de Spotify tient bien en dessous.</summary>
    public const int MaxRequestLine = 8192;

    /// <summary>
    /// La ligne de requête (« GET /login?… HTTP/1.1 »), qui peut arriver en plusieurs morceaux : lu jusqu'à la fin de
    /// ligne, la fin de la connexion ou <see cref="MaxRequestLine"/> octets, sans attendre les en-têtes.
    /// </summary>
    public static async Task<string> ReadRequestLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxRequestLine];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), ct);
            if (read == 0) break;
            var start = length;
            length += read;
            if (Array.IndexOf(buffer, (byte)'\n', start, read) >= 0) break;
        }
        return System.Text.Encoding.ASCII.GetString(buffer, 0, length);
    }
}
