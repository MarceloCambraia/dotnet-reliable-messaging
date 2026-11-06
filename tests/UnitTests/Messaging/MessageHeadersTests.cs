using System.Text;
using BuildingBlocks.Messaging;

namespace UnitTests.Messaging;

public sealed class MessageHeadersTests
{
    [Fact]
    public void GetInt32_reads_values_in_every_representation_the_broker_may_use()
    {
        var headers = new Dictionary<string, object?>
        {
            ["int"] = 3,
            ["long"] = 4L,
            ["bytes"] = Encoding.UTF8.GetBytes("5"),
            ["string"] = "6",
            ["garbage"] = "x",
        };

        MessageHeaders.GetInt32(headers, "int").Should().Be(3);
        MessageHeaders.GetInt32(headers, "long").Should().Be(4);
        MessageHeaders.GetInt32(headers, "bytes").Should().Be(5);
        MessageHeaders.GetInt32(headers, "string").Should().Be(6);
        MessageHeaders.GetInt32(headers, "garbage").Should().Be(0);
        MessageHeaders.GetInt32(headers, "missing").Should().Be(0);
        MessageHeaders.GetInt32(null, "missing").Should().Be(0);
    }

    [Fact]
    public void GetString_decodes_utf8_byte_arrays()
    {
        var headers = new Dictionary<string, object?> { ["k"] = Encoding.UTF8.GetBytes("value") };

        MessageHeaders.GetString(headers, "k").Should().Be("value");
        MessageHeaders.GetString(headers, "missing").Should().BeNull();
    }
}
