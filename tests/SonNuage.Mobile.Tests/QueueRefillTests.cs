using SonNuage.Mobile.Playback;

namespace SonNuage.Mobile.Tests;

public class QueueRefillTests
{
    [Fact]
    public void A_queue_follows_the_station_of_the_playing_track() =>
        Assert.Equal("halloween", QueueRefill.Station(current: "halloween", last: null));

    [Fact]
    public void A_track_queued_by_hand_after_a_station_does_not_leave_it() =>
        Assert.Equal("halloween", QueueRefill.Station(current: null, last: "halloween"));

    [Fact]
    public void A_queue_without_station_is_an_ordinary_queue() => Assert.Null(QueueRefill.Station(null, null));

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, false, false)]
    public void A_station_keeps_playing_even_without_infinite_play_but_repeat_stops_both(bool fromStation, bool infinitePlay, bool repeatOff, bool refills) =>
        Assert.Equal(refills, QueueRefill.Applies(fromStation, infinitePlay, repeatOff));

    [Theory]
    [InlineData(false, 3, 3)]
    [InlineData(true, 3, 5)]
    [InlineData(true, 8, 8)]
    public void A_station_keeps_at_least_five_tracks_ahead_like_the_web_player(bool fromStation, int infiniteAhead, int ahead) =>
        Assert.Equal(ahead, QueueRefill.Ahead(fromStation, infiniteAhead));

    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    [Fact]
    public void Youtube_tracks_never_reach_the_server_and_the_current_one_keeps_its_place()
    {
        var (ids, current) = QueueRefill.ServerTracks([A, null, B, C], currentIndex: 2);
        Assert.Equal([A, B, C], ids);
        Assert.Equal(1, current);
    }

    [Fact]
    public void A_youtube_track_playing_seeds_from_the_last_server_track_before_it()
    {
        var (ids, current) = QueueRefill.ServerTracks([A, null, B], currentIndex: 1);
        Assert.Equal([A, B], ids);
        Assert.Equal(0, current);
    }

    [Fact]
    public void A_queue_of_youtube_tracks_only_has_nothing_to_seed()
    {
        var (ids, current) = QueueRefill.ServerTracks([null, null], currentIndex: 1);
        Assert.Empty(ids);
        Assert.Equal(-1, current);
    }
}
