using System.Collections.Generic;
using NUnit.Framework;

namespace UnityCliConnector.Tests
{
    public class CommandJobCatalogTests
    {
        [Test]
        public void Compile_IsJob()
        {
            Assert.AreEqual(
                CommandJobCatalog.CompletionCompilation,
                CommandJobCatalog.GetCompletionKind("compile", null));
        }

        [Test]
        public void Refresh_WithCompile_IsJob()
        {
            var p = new Dictionary<string, object> { { "compile", true } };
            Assert.AreEqual(
                CommandJobCatalog.CompletionCompilation,
                CommandJobCatalog.GetCompletionKind("refresh", p));
        }

        [Test]
        public void Refresh_WithStringCompile_IsJob()
        {
            var p = new Dictionary<string, object> { { "compile", "true" } };
            Assert.AreEqual(
                CommandJobCatalog.CompletionCompilation,
                CommandJobCatalog.GetCompletionKind("refresh", p));
        }

        [Test]
        public void Ping_IsNotJob()
        {
            Assert.IsNull(CommandJobCatalog.GetCompletionKind("ping", null));
        }

        [Test]
        public void Console_IsNotJob()
        {
            Assert.IsNull(CommandJobCatalog.GetCompletionKind("editor.console", null));
        }
    }
}
