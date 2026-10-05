namespace Test.Publisher;

[Collection("Event hosts")]
public class PublisherTests(E2E.TestFixture app)
{
    [Theory,
     InlineData("A"),
     InlineData("B"),
     InlineData("C")]
    public async Task Endpoint_Broadcasts_The_Event(string data)
    {
        var res = await app.PublisherClient.GetStringAsync($"/event/{data}", TestContext.Current.CancellationToken);
        Assert.Equal("\"events published!\"", res);

        var receiver = app.Services.GetTestEventReceiver<SomethingHappened>();
        var received = await receiver.WaitForMatchAsync(e => e.Description == data, ct: TestContext.Current.CancellationToken);
        Assert.True(received.Any());
    }
}