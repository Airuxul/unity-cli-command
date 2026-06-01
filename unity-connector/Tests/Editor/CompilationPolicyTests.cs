using NUnit.Framework;
using UnityCliConnector;
using UnityCliConnector.Completion;

namespace UnityCliConnector.Tests.Editor
{
    public sealed class CompilationPolicyTests
    {
        private readonly CompilationPolicy _policy = new();

        [Test]
        public void PendingWhileNotCompiling_CompletesImmediately()
        {
            var command = new CommandRecord { Status = CommandStatus.Pending };
            var state = new EditorStateSnapshot { IsCompiling = false };

            Assert.IsTrue(_policy.TryComplete(command, state, out var result, out var error));
            Assert.IsNull(error);
            Assert.IsNotNull(result);
        }

        [Test]
        public void RunningWhileNotCompiling_Completes()
        {
            var command = new CommandRecord { Status = CommandStatus.Running };
            var state = new EditorStateSnapshot { IsCompiling = false };

            Assert.IsTrue(_policy.TryComplete(command, state, out _, out _));
        }

        [Test]
        public void PendingWhileCompiling_Waits()
        {
            var command = new CommandRecord { Status = CommandStatus.Pending };
            var state = new EditorStateSnapshot { IsCompiling = true };

            Assert.IsFalse(_policy.TryComplete(command, state, out _, out _));
            Assert.AreEqual(CommandStatus.Running, command.Status);
        }
    }
}
