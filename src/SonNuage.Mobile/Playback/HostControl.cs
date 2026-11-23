namespace SonNuage.Mobile.Playback;

/// <summary>
/// Règles de <see cref="JamRoutingPlayer"/> sans type Android : testées dans tests/SonNuage.Mobile.Tests.
/// </summary>
public static class HostControl
{
    /// <summary>Qui appelle le lecteur de la session.</summary>
    public enum Caller
    {
        /// <summary>
        /// Aucun contrôleur : le service ou Media3 lui-même (balayer l'appli des récents met tout en pause, par exemple).
        /// </summary>
        Service,

        /// <summary>L'interface : ses commandes sont déjà routées par LinkedPlayer, ou viennent de la synchro.</summary>
        App,

        /// <summary>Notification, écran verrouillé, casque, Android Auto.</summary>
        Outside,
    }

    /// <summary>La commande part-elle chez l'hôte du Jam plutôt qu'au lecteur local ?</summary>
    public static bool ToHost(Caller caller, bool routes) => routes && caller == Caller.Outside;
}
