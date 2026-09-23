using System.Text.Json;
using OAuthLearn.Api.Auth;

namespace OAuthLearn.Api.Tests;

public class GoogleAuthEventsTests
{
    [Theory]
    [InlineData("""{"email_verified":true}""", true)]
    [InlineData("""{"verified_email":true}""", true)]
    [InlineData("""{"email_verified":"true"}""", true)]
    [InlineData("""{"email_verified":false}""", false)]
    [InlineData("""{"verified_email":false}""", false)]
    [InlineData("""{}""", false)]
    [InlineData("""{"email_verified":"yes"}""", false)]
    [InlineData("""{"email_verified":1}""", false)]
    [InlineData("""[]""", false)]
    public void IsEmailVerified_only_accepts_an_explicit_true(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, GoogleAuthEvents.IsEmailVerified(document.RootElement));
    }
}
