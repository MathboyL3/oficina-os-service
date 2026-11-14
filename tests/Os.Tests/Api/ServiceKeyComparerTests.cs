using FluentAssertions;
using Os.Api.Security;

namespace Os.Tests.Api;

public class ServiceKeyComparerTests
{
    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "abd", false)]
    [InlineData("abc", "abcd", false)]
    [InlineData("abc", "", false)]
    [InlineData("abc", null, false)]
    [InlineData("", "", false)]
    [InlineData(null, null, false)]
    [InlineData("", "abc", false)]
    public void IsValid(string? esperada, string? informada, bool resultado)
        => ServiceKeyComparer.IsValid(esperada, informada).Should().Be(resultado);
}