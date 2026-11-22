using FLTCOM.Core.Protocol;
using Xunit;

namespace FLTCOM.Server.Tests
{
    public class InboxTests
    {
        private static uint Nonce(ClientMessage message) => message.Match(hello => 0u, ping => ping.Nonce, brief => 0u, approve => 0u, scrub => 0u, resume => 0u, answer => 0u, groupEdit => 0u);

        [Fact]
        public void MessagesComeOutInTheOrderTheyWentIn()
        {
            var inbox = new Inbox();
            var share = new InboxShare(budget: 100);
            Assert.True(inbox.TryPost(new Ping(1), share, 10));
            Assert.True(inbox.TryPost(new Ping(2), share, 10));

            Assert.True(inbox.TryTake(out ClientMessage first));
            Assert.True(inbox.TryTake(out ClientMessage second));
            Assert.Equal(new uint[] { 1, 2 }, new[] { Nonce(first), Nonce(second) });
            Assert.False(inbox.TryTake(out _));
        }

        [Fact]
        public void AFullShareRefusesUntilOneOfItsMessagesIsTaken()
        {
            var inbox = new Inbox();
            var share = new InboxShare(budget: 20);
            Assert.True(inbox.TryPost(new Ping(1), share, 10));
            Assert.True(inbox.TryPost(new Ping(2), share, 10));

            Assert.False(inbox.TryPost(new Ping(3), share, 1));
            Assert.True(inbox.TryTake(out _));
            Assert.True(inbox.TryPost(new Ping(4), share, 10));
            Assert.False(inbox.TryPost(new Ping(5), share, 1));
        }

        [Fact]
        public void OneClientsFullShareDoesNotRefuseAnother()
        {
            var inbox = new Inbox();
            var flooder = new InboxShare(budget: 20);
            var other = new InboxShare(budget: 20);
            Assert.True(inbox.TryPost(new Ping(1), flooder, 20));
            Assert.False(inbox.TryPost(new Ping(2), flooder, 1));

            Assert.True(inbox.TryPost(new Ping(3), other, 20));
        }
    }
}
