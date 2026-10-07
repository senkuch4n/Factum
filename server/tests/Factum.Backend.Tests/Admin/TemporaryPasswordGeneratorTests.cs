using System.Text.RegularExpressions;
using Factum.Backend.Services.Admin;

namespace Factum.Backend.Tests.Admin;

/// <summary>abm-clientes §6.7 (B23).</summary>
public sealed class TemporaryPasswordGeneratorTests
{
    private readonly TemporaryPasswordGenerator _gen = new();

    [Fact]
    public void DefaultMin_ThreeGroupsOfFour()
    {
        var p = _gen.Generate(10);
        Assert.Matches(new Regex("^[a-z2-9]{4}(-[a-z2-9]{4}){2}$"), p);
        Assert.Equal(14, p.Length);
    }

    [Fact]
    public void OnlyAlphabetChars_NeverAmbiguous()
    {
        for (var i = 0; i < 300; i++)
        {
            var p = _gen.Generate(10);
            foreach (var c in p.Replace("-", ""))
                Assert.Contains(c, TemporaryPasswordGenerator.Alphabet);
            Assert.DoesNotContain('0', p);
            Assert.DoesNotContain('o', p);
            Assert.DoesNotContain('1', p);
            Assert.DoesNotContain('l', p);
            Assert.DoesNotContain('i', p);
        }
        Assert.Equal(31, TemporaryPasswordGenerator.Alphabet.Length);
        Assert.Equal(TemporaryPasswordGenerator.Alphabet.Length, TemporaryPasswordGenerator.Alphabet.Distinct().Count());
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(64)]
    public void LengthIsAtLeastMin(int min)
    {
        var p = _gen.Generate(min);
        Assert.True(p.Length >= min, $"{p.Length} < {min}");
        var groups = TemporaryPasswordGenerator.GroupsFor(min);
        Assert.Equal(5 * groups - 1, p.Length);
        Assert.Matches(new Regex("^[a-z2-9]{4}(-[a-z2-9]{4})*$"), p);
    }

    [Fact]
    public void Max64_ThirteenGroups() => Assert.Equal(13, TemporaryPasswordGenerator.GroupsFor(64));

    [Fact]
    public void ThousandGenerations_NoRepeats()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 1000; i++) Assert.True(set.Add(_gen.Generate(10)));
    }

    [Fact]
    public void NeverContainsSevenDigitRun()
    {
        var run = new Regex("[0-9]{7}");
        for (var i = 0; i < 1000; i++) Assert.DoesNotMatch(run, _gen.Generate(64));
    }
}
