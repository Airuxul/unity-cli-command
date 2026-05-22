using NUnit.Framework;

namespace UnityCliConnector.Tests
{
    public class CommandDiscoveryTests
    {
        [Test]
        public void DiscoversBuiltinPing()
        {
            CommandDiscovery.Invalidate();
            var handler = CommandDiscovery.Find("ping");
            Assert.IsNotNull(handler);
        }
    }
}
