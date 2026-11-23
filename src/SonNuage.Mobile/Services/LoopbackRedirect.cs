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
}
