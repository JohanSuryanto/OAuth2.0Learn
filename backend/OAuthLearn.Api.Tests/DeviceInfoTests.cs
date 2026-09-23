using System.Net;
using OAuthLearn.Api.Auth;

namespace OAuthLearn.Api.Tests;

public class DeviceInfoTests
{
    [Theory]
    [InlineData(TestHelpers.ChromeOnWindows, "Chrome on Windows")]
    [InlineData(TestHelpers.FirefoxOnLinux, "Firefox on Linux")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15", "Safari on macOS")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "Safari on iOS")]
    [InlineData("Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36 OPR/120.0.0.0", "Opera on Windows")]
    [InlineData("Mozilla/5.0 (X11; CrOS x86_64 16000.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36", "Chrome on ChromeOS")]
    [InlineData("curl/8.9.1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Describes_user_agents(string? userAgent, string? expected) =>
        Assert.Equal(expected, UserAgentDescriber.Describe(userAgent));

    [Fact]
    public void Description_is_at_most_200_characters()
    {
        var ua = "Mozilla/5.0 (Windows NT 10.0) " + new string('x', 500) + " Chrome/1.0";

        Assert.True(UserAgentDescriber.Describe(ua)!.Length <= UserAgentDescriber.MaxLength);
    }

    [Theory]
    [InlineData("203.0.113.42", "203.0.113.x")]
    [InlineData("127.0.0.1", "127.0.0.x")]
    [InlineData("::ffff:10.1.2.3", "10.1.2.x")]
    [InlineData("2001:db8:85a3:8d3:1319:8a2e:370:7348", "2001:db8:85a3:8d3:…")]
    [InlineData("::1", "0:0:0:0:…")]
    public void Masks_addresses(string address, string expected) =>
        Assert.Equal(expected, IpMasker.Mask(IPAddress.Parse(address)));

    [Fact]
    public void Masks_null_as_null() => Assert.Null(IpMasker.Mask(null));
}
