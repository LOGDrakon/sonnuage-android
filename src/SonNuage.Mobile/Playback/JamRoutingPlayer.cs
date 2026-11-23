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
/// <para>
/// En « hôte seulement », la session montre aussi la lecture ou la pause de l'hôte (<see cref="IPlayerRoute.Shown"/>) :
/// le casque et la notification choisissent entre lecture et pause d'après elle, et le lecteur local, lui, ne joue
/// jamais. L'interface garde l'état réel du lecteur local (<see cref="Local"/>).
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

    // ---- Hôte seulement : la session montre la lecture de l'hôte ----

    /// <summary>
    /// Le lecteur de la session du service en cours (posé à sa création, retiré à sa destruction) : l'interface, dans
    /// le même processus et sur le même thread, y lit l'état réel du lecteur local.
    /// </summary>
    public static JamRoutingPlayer? Current { get; set; }

    /// <summary>Ce que joue l'hôte quand le son reste chez lui ; null quand le lecteur local joue lui-même.</summary>
    private PlayerState? Host => route() is { Routes: true, Shown: { } shown } ? shown : null;

    /// <summary>La session montre-t-elle l'hôte plutôt que le lecteur local ?</summary>
    public bool ShowsHost => Host is not null;

    public override bool IsPlaying => Host is { } host ? host.IsPlaying : base.IsPlaying;

    // Prêt dès que l'hôte a un titre : sinon une lecture demandée sur un lecteur local vide relancerait la reprise de
    // la file sauvegardée, ici.
    public override int PlaybackState => Host is { Current: not null } ? PlayerConst.StateReady : base.PlaybackState;

    /// <summary>Le lecteur local tel qu'il est, quoi que montre la session.</summary>
    public (bool PlayWhenReady, bool IsPlaying, int State) Local => (base.PlayWhenReady, base.IsPlaying, base.PlaybackState);

    private (bool PlayWhenReady, bool IsPlaying, int State) Face => (PlayWhenReady, IsPlaying, PlaybackState);

    // ForwardingPlayer ne relaie que les événements du lecteur enveloppé : quand l'hôte change, c'est à nous de prévenir
    // les écouteurs de la session (notification, contrôleurs), avec ce qu'ils ont vu en dernier pour seule référence.
    private readonly List<IPlayerListener> _listeners = [];
    private (bool PlayWhenReady, bool IsPlaying, int State) _seen;
    private readonly Android.OS.Handler _handler = new(player.ApplicationLooper!);
    private Inner? _inner;

    public override void AddListener(IPlayerListener? listener)
    {
        base.AddListener(listener);
        if (listener is null) return;
        if (_listeners.Count == 0) _seen = Face;
        _listeners.Add(listener);
        if (_inner is null)
        {
            _inner = new Inner(this);
            WrappedPlayer!.AddListener(_inner);
        }
    }

    public override void RemoveListener(IPlayerListener? listener)
    {
        base.RemoveListener(listener);
        if (listener is not null) _listeners.Remove(listener);
    }

    /// <summary>L'état du Jam a pu changer (n'importe quel thread) : la session le reverra sur le thread du lecteur.</summary>
    public void Refresh() => _handler.Post(Publish);

    private void Publish()
    {
        var face = Face;
        if (face == _seen || _listeners.Count == 0) return;
        var seen = _seen;
        _seen = face;
        var flags = new FlagSet.Builder();
        foreach (var listener in _listeners.ToList())
        {
            if (face.PlayWhenReady != seen.PlayWhenReady) listener.OnPlayWhenReadyChanged(face.PlayWhenReady, PlayerConst.PlayWhenReadyRemote);
            if (face.State != seen.State) listener.OnPlaybackStateChanged(face.State);
            if (face.IsPlaying != seen.IsPlaying) listener.OnIsPlayingChanged(face.IsPlaying);
        }
        if (face.PlayWhenReady != seen.PlayWhenReady) flags.Add(PlayerConst.EventPlayWhenReadyChanged);
        if (face.State != seen.State) flags.Add(PlayerConst.EventPlaybackStateChanged);
        if (face.IsPlaying != seen.IsPlaying) flags.Add(PlayerConst.EventIsPlayingChanged);
        var events = new PlayerEvents(flags.Build());
        foreach (var listener in _listeners.ToList()) listener.OnEvents(this, events);
    }

    /// <summary>
    /// Les événements du lecteur local arrivent aux écouteurs avec ses propres valeurs : notées comme vues, puis
    /// corrigées juste après si la session montre l'hôte.
    /// </summary>
    private sealed class Inner(JamRoutingPlayer owner) : Java.Lang.Object, IPlayerListener
    {
        public void OnPlayWhenReadyChanged(bool playWhenReady, int reason)
        {
            owner._seen.PlayWhenReady = playWhenReady;
            owner.Refresh();
        }

        public void OnIsPlayingChanged(bool isPlaying)
        {
            owner._seen.IsPlaying = isPlaying;
            owner.Refresh();
        }

        public void OnPlaybackStateChanged(int playbackState)
        {
            owner._seen.State = playbackState;
            owner.Refresh();
        }
    }

    // ---- Commandes ----

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
        get => Host is { } host ? host.IsPlaying : base.PlayWhenReady;
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

    public override void SeekBack()
    {
        if (Target is { } r) SeekHost(r, -SeekBackIncrement / 1000.0);
        else base.SeekBack();
    }

    public override void SeekForward()
    {
        if (Target is { } r) SeekHost(r, SeekForwardIncrement / 1000.0);
        else base.SeekForward();
    }

    /// <summary>Depuis la position de l'hôte : celle qu'on affiche en « hôte seulement », la locale (calée) en mode invité.</summary>
    private void SeekHost(IPlayerRoute route, double deltaSeconds)
    {
        var (position, duration) = route.Shown is { } shown
            ? (shown.PositionSeconds, shown.DurationSeconds)
            : (base.CurrentPosition / 1000.0, base.Duration > 0 ? base.Duration / 1000.0 : 0);
        _ = route.SeekAsync(HostControl.SeekTarget(position, deltaSeconds, duration));
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
