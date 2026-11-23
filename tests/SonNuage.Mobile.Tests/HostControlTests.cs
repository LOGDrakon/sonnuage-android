using SonNuage.Mobile.Playback;
using static SonNuage.Mobile.Playback.HostControl;

namespace SonNuage.Mobile.Tests;

public sealed class HostControlTests
{
    [Fact]
    public void A_command_from_the_notification_or_the_headset_goes_to_the_host() =>
        Assert.True(HostControl.ToHost(Caller.Outside, routes: true));

    [Fact]
    public void The_app_commands_stay_local_they_are_already_routed() =>
        Assert.False(HostControl.ToHost(Caller.App, routes: true));

    [Fact]
    public void A_call_without_controller_stays_local() =>
        // Le service lui-même : balayer l'appli des récents met tout en pause, jamais chez l'hôte.
        Assert.False(HostControl.ToHost(Caller.Service, routes: true));

    [Fact]
    public void Outside_a_jam_everything_stays_local() =>
        Assert.False(HostControl.ToHost(Caller.Outside, routes: false));
}
