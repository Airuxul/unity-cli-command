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

        [Test]
        public void DiscoversBuiltinConsole()
        {
            CommandDiscovery.Invalidate();
            var handler = CommandDiscovery.Find("editor.console");
            Assert.IsNotNull(handler);
            Assert.IsFalse(handler.IsJob);
            CollectionAssert.Contains(handler.Aliases, "console");
        }

        [Test]
        public void DiscoversCompileAsJob()
        {
            CommandDiscovery.Invalidate();
            var handler = CommandDiscovery.Find("compile");
            Assert.IsNotNull(handler);
            Assert.IsTrue(handler.IsJob);
            Assert.AreEqual(CommandJobCatalog.CompletionCompilation, handler.Completion);
        }
    }
}
