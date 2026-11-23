namespace SonNuage.Mobile.Playback;

/// <summary>
/// Ce qui complète la file quand elle s'épuise : la radio thématique d'où elle vient, sinon la lecture infinie
/// (morceaux proches des derniers joués). Sans type Android : testé dans tests/SonNuage.Mobile.Tests.
/// </summary>
public static class QueueRefill
{
    /// <summary>Morceaux demandés à une radio thématique quand Android Auto la lance (l'appli en demande autant).</summary>
    public const int StationFirstBatch = 25;

    /// <summary>Avance minimale d'une radio thématique, même si la lecture infinie en garde moins (comme le web).</summary>
    public const int StationAhead = 5;

    /// <summary>
    /// Radio que suit la file : celle de la piste en cours, sinon celle de la dernière piste. Un titre ajouté à la main
    /// (« à suivre » ou en fin de file) ne fait donc pas quitter la radio. Null : file ordinaire.
    /// </summary>
    public static string? Station(string? current, string? last) => current ?? last;

    /// <summary>
    /// Faut-il compléter la file ? Une radio ne s'arrête pas, même lecture infinie désactivée ; la répétition (de la
    /// file ou du titre) coupe tout, comme sur le web.
    /// </summary>
    public static bool Applies(bool fromStation, bool infinitePlay, bool repeatOff) => repeatOff && (fromStation || infinitePlay);

    /// <summary>Morceaux à garder après celui qui joue.</summary>
    public static int Ahead(bool fromStation, int infiniteAhead) => fromStation ? Math.Max(infiniteAhead, StationAhead) : infiniteAhead;

    /// <summary>
    /// Les morceaux du serveur dans la file, seuls à pouvoir lui servir de graines ou d'exclusions (null : titre YouTube
    /// direct d'un Jam, dont l'identifiant n'existe pas côté serveur), et la place parmi eux du dernier joué jusqu'à
    /// celui en cours compris (-1 : aucun).
    /// </summary>
    public static (List<Guid> Ids, int CurrentIndex) ServerTracks(IReadOnlyList<Guid?> queue, int currentIndex)
    {
        var ids = new List<Guid>(queue.Count);
        var current = -1;
        for (var i = 0; i < queue.Count; i++)
        {
            if (queue[i] is not { } id) continue;
            ids.Add(id);
            if (i <= currentIndex) current = ids.Count - 1;
        }
        return (ids, current);
    }
}
