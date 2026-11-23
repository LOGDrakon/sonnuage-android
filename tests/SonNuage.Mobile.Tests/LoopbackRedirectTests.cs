using SonNuage.Mobile.Services;

namespace SonNuage.Mobile.Tests;

public sealed class LoopbackRedirectTests
{
    [Fact]
    public void The_redirect_gives_its_code_and_state()
    {
        var query = LoopbackRedirect.Query("GET /login?code=AQB%2Dx_y&state=abc HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
        Assert.NotNull(query);
        Assert.Equal("AQB-x_y", query["code"]);
        Assert.Equal("abc", query["state"]);
    }

    [Fact]
    public void A_refusal_has_no_code()
    {
        var query = LoopbackRedirect.Query("GET /login?error=access_denied&state=abc HTTP/1.1\r\n");
        Assert.NotNull(query);
        Assert.False(query.ContainsKey("code"));
    }

    [Theory]
    [InlineData("")]                                      // pré-connexion du navigateur : rien d'envoyé
    [InlineData("GET /favicon.ico HTTP/1.1\r\n")]
    [InlineData("POST /login?code=x HTTP/1.1\r\n")]
    [InlineData("GET /loginx?code=x HTTP/1.1\r\n")]
    public void Anything_else_is_not_the_redirect(string request) => Assert.Null(LoopbackRedirect.Query(request));

    [Fact]
    public async Task A_request_line_sent_in_pieces_is_read_whole()
    {
        var stream = new PiecesStream("GET /login?co", "de=abc&state=s", "t HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
        var query = LoopbackRedirect.Query(await LoopbackRedirect.ReadRequestLineAsync(stream, TestContext.Current.CancellationToken));
        Assert.NotNull(query);
        Assert.Equal("abc", query["code"]);
        Assert.Equal("st", query["state"]);
    }

    [Fact]
    public async Task Reading_stops_at_the_end_of_the_request_line()
    {
        // Le reste (en-têtes) n'est jamais attendu : un navigateur qui garde la connexion ouverte ne bloque rien.
        var stream = new PiecesStream("GET /login?code=x HTTP/1.1\r\n", "Host: 127.0.0.1\r\n");
        await LoopbackRedirect.ReadRequestLineAsync(stream, TestContext.Current.CancellationToken);
        Assert.Equal(1, stream.Reads);
    }

    [Fact]
    public async Task A_connection_closed_without_a_word_reads_nothing() =>
        Assert.Equal("", await LoopbackRedirect.ReadRequestLineAsync(new PiecesStream(), TestContext.Current.CancellationToken));

    [Fact]
    public async Task An_endless_line_is_cut_at_the_limit()
    {
        var piece = new string('a', 1000);
        var stream = new PiecesStream(Enumerable.Repeat(piece, 20).ToArray());
        var line = await LoopbackRedirect.ReadRequestLineAsync(stream, TestContext.Current.CancellationToken);
        Assert.Equal(LoopbackRedirect.MaxRequestLine, line.Length);
    }

    /// <summary>Une connexion qui livre la requête en plusieurs morceaux, un par lecture.</summary>
    private sealed class PiecesStream(params string[] pieces) : Stream
    {
        private int _next;
        private byte[] _pending = [];

        public int Reads { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pending.Length == 0)
            {
                if (_next == pieces.Length) return 0;
                _pending = System.Text.Encoding.ASCII.GetBytes(pieces[_next++]);
                Reads++;
            }
            var n = Math.Min(count, _pending.Length);
            Array.Copy(_pending, 0, buffer, offset, n);
            _pending = _pending[n..];
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
