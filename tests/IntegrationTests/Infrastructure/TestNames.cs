namespace IntegrationTests.Infrastructure;

internal static class TestNames
{
    public static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 60)];
}
