using System.Text;
using BuildingBlocks.Messaging;

namespace UnitTests.Messaging;

public sealed class MessageEnvelopeTests
{
    private sealed record SamplePayload(Guid Id, string Name, decimal Amount);

    [Fact]
    public void Envelope_round_trips_through_the_wire_format()
    {
        var payload = new SamplePayload(Guid.NewGuid(), "widget", 12.34m);
        var envelope = MessageEnvelope.Create(Guid.NewGuid(), "sample.v1", "corr-1", DateTimeOffset.UnixEpoch, payload);

        var restored = EnvelopeSerializer.Deserialize(EnvelopeSerializer.Serialize(envelope));

        restored.MessageId.Should().Be(envelope.MessageId);
        restored.Type.Should().Be("sample.v1");
        restored.CorrelationId.Should().Be("corr-1");
        restored.OccurredAt.Should().Be(DateTimeOffset.UnixEpoch);
        restored.GetPayload<SamplePayload>().Should().Be(payload);
    }

    [Fact]
    public void Wire_format_uses_camel_case_and_embeds_payload_as_json_object()
    {
        var envelope = MessageEnvelope.Create(Guid.Empty, "t", "c", DateTimeOffset.UnixEpoch, new { Value = 1 });

        var json = Encoding.UTF8.GetString(EnvelopeSerializer.Serialize(envelope));

        json.Should().Contain("\"messageId\"").And.Contain("\"payload\":{\"value\":1}");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"messageId\":\"00000000-0000-0000-0000-000000000000\",\"type\":\"t\"}")]
    public void Deserialize_rejects_bodies_that_are_not_valid_envelopes(string body)
    {
        var act = () => EnvelopeSerializer.Deserialize(Encoding.UTF8.GetBytes(body));

        act.Should().Throw<InvalidMessageException>();
    }

    [Fact]
    public void GetPayload_with_incompatible_shape_throws_invalid_message()
    {
        var envelope = MessageEnvelope.Create(Guid.NewGuid(), "t", "c", DateTimeOffset.UnixEpoch, new { Id = "not-a-guid" });

        var act = () => envelope.GetPayload<SamplePayload>();

        act.Should().Throw<InvalidMessageException>();
    }
}
