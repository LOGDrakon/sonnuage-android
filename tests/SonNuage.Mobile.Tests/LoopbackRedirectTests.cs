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
}
