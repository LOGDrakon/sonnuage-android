using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using SonNuage.UI.Services;

namespace SonNuage.Mobile.Playback;

/// <summary>
/// Le lecteur tel que la session Media3 le voit. Dans un Jam (les deux modes), une commande venue d'ailleurs que
/// l'interface (notification, écran verrouillé, casque, Android Auto) va à l'hôte, comme les boutons de l'interface
/// (voir <see cref="LinkedPlayer"/>) : sur le lecteur local, la synchro l'annulerait aussitôt.
/// <para>
/// L'interface passe aussi par la session (<see cref="Services.AndroidPlayer"/>) : ses commandes, déjà routées par
/// <see cref="LinkedPlayer"/> ou envoyées par la synchro elle-même, restent locales. Qui commande se lit au moment de
/// l'appel (<see cref="MediaSession.ControllerForCurrentRequest"/>) : Media3 met les commandes en file et en exécute
/// certaines sans contrôleur du tout (balayer l'appli des récents met en pause), un marqueur posé à l'avance pouvait
/// donc servir à la mauvaise commande. Le reste du service (fondu enchaîné, pré-chargement…) utilise le lecteur
/// enveloppé directement.
/// </para>
/// </summary>
public sealed class JamRoutingPlayer(Media3IPlayer player, Func<IPlayerRoute?> route) : ForwardingPlayer(player)
{
    /// <summary>Clé des connection hints que pose le contrôleur de l'interface.</summary>
    public const string AppControllerHint = "sonnuage.app";

    /// <summary>La session qui expose ce lecteur, posée juste après sa création (elle a besoin du lecteur pour naître).</summary>
    public MediaSession? Session { get; set; }

    /// <summary>Qui appelle, d'après le contrôleur de la commande en cours (à lire sur le thread du lecteur).</summary>
    private HostControl.Caller Caller =>
        Session?.ControllerForCurrentRequest is not { } controller ? HostControl.Caller.Service
        : controller.ConnectionHints?.GetBoolean(AppControllerHint) == true ? HostControl.Caller.App
        : HostControl.Caller.Outside;

    /// <summary>La route du Jam si la commande en cours doit y aller, null pour le lecteur local.</summary>
    private IPlayerRoute? Target => route() is { } r && HostControl.ToHost(Caller, r.Routes) ? r : null;

    // Lecture/pause disent ce qu'elles veulent : l'état du lecteur local (muet en « hôte seulement », en retard sur
    // l'hôte en mode invité) ne doit pas décider s'il faut basculer.
    public override void Play()
    {
        if (Target is { } r) _ = r.SetPlayingAsync(true);
        else base.Play();
    }

    public override void Pause()
    {
        if (Target is { } r) _ = r.SetPlayingAsync(false);
        else base.Pause();
    }

    public override bool PlayWhenReady
    {
        get => base.PlayWhenReady;
        set
        {
            if (Target is { } r) _ = r.SetPlayingAsync(value);
            else base.PlayWhenReady = value;
        }
    }

    public override void Stop()
    {
        if (Target is { } r) _ = r.SetPlayingAsync(false);
        else base.Stop();
    }

    public override void SeekTo(long positionMs)
    {
        if (Target is { } r) _ = r.SeekAsync(positionMs / 1000.0);
        else base.SeekTo(positionMs);
    }

    public override void SeekToNext()
    {
        if (Target is { } r) _ = r.NextAsync();
        else base.SeekToNext();
    }

    public override void SeekToNextMediaItem()
    {
        if (Target is { } r) _ = r.NextAsync();
        else base.SeekToNextMediaItem();
    }

    public override void SeekToPrevious()
    {
        if (Target is { } r) _ = r.PreviousAsync();
        else base.SeekToPrevious();
    }

    public override void SeekToPreviousMediaItem()
    {
        if (Target is { } r) _ = r.PreviousAsync();
        else base.SeekToPreviousMediaItem();
    }
}
