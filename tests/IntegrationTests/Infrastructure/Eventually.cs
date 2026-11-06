namespace IntegrationTests.Infrastructure;

/// <summary>Polls an asynchronous condition; messaging tests assert on eventual outcomes.</summary>
internal static class Eventually
{
    public static async Task<T> GetAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> until,
        TimeSpan? timeout = null,
        string? because = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        var last = await probe();
        while (!until(last))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Condition not met in time{(because is null ? string.Empty : $": {because}")}. Last value: {last}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
            last = await probe();
        }

        return last;
    }
}
