using System.Globalization;
using System.Text;

namespace BuildingBlocks.Messaging;

public static class MessageHeaders
{
    /// <summary>How many times the consumer has already scheduled this message for retry.</summary>
    public const string RetryCount = "x-retry-count";

    public const string ExceptionType = "x-exception-type";

    public const string ExceptionMessage = "x-exception-message";

    public const string FailedQueue = "x-failed-queue";

    /// <summary>
    /// Reads a header as a string. AMQP string headers arrive as <c>byte[]</c> on the consumer
    /// side, so both representations are handled.
    /// </summary>
    public static string? GetString(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            null => null,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    public static int GetInt32(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value))
        {
            return 0;
        }

        return value switch
        {
            int i => i,
            long l => checked((int)l),
            short s => s,
            byte b => b,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), CultureInfo.InvariantCulture, out var parsed) => parsed,
            string text when int.TryParse(text, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0,
        };
    }
}
