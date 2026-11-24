using MacroPad.Core.Config;
using Xunit;

namespace MacroPad.Tests;

/// <summary>개인 설정 macropad.json 이 없으면 예시 macropad.example.json 을 복사해서 만듦</summary>
public sealed class ConfigLocatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "macropad-loc-" + Guid.NewGuid().ToString("N"));

    public ConfigLocatorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void EnsureExists_CopiesExampleOnlyWhenMissing()
    {
        var path = Path.Combine(_dir, ConfigLocator.FileName);
        Assert.False(ConfigLocator.EnsureExists(path)); // 예시도 없음

        File.WriteAllText(Path.Combine(_dir, ConfigLocator.ExampleFileName), "{ \"actions\": {} }");
        Assert.True(ConfigLocator.EnsureExists(path));
        Assert.Equal("{ \"actions\": {} }", File.ReadAllText(path));

        File.WriteAllText(path, "{ \"mine\": 1 }");
        Assert.True(ConfigLocator.EnsureExists(path)); // 이미 있으면 덮어쓰지 않음
        Assert.Equal("{ \"mine\": 1 }", File.ReadAllText(path));
    }

    [Fact]
    public void RepoExample_IsAValidConfig()
    {
        var config = PadConfig.Load(Path.Combine(AppContext.BaseDirectory, "TestConfigs", "macropad.json"));
        Assert.NotEmpty(config.Layers);
    }
}
