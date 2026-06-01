using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityCliConnector.Commands;

namespace UnityCliConnector
{
    public static class CommandDiscovery
    {
        private static List<ICommandHandler> _handlers;
        private static readonly object Gate = new();

        public static IReadOnlyList<ICommandHandler> Handlers
        {
            get
            {
                EnsureLoaded();
                return _handlers;
            }
        }

        public static void Invalidate() => _handlers = null;

        public static ICommandHandler Find(string command) =>
            FindForHost(command, hostKind: null);

        public static ICommandHandler FindForHost(string command, string hostKind)
        {
            EnsureLoaded();
            ICommandHandler fallback = null;
            foreach (var handler in _handlers)
            {
                if (!string.Equals(handler.Name, command, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrEmpty(hostKind))
                    return handler;

                if (CommandAvailability.IsAvailableForHost(handler.Scope, hostKind))
                    return handler;

                fallback ??= handler;
            }

            return fallback;
        }

        private static void EnsureLoaded()
        {
            if (_handlers != null)
                return;

            lock (Gate)
            {
                if (_handlers != null)
                    return;

                var list = new List<ICommandHandler>();
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types.Where(t => t != null).ToArray();
                    }

                    foreach (var type in types)
                    {
                        if (type == null || !type.IsClass || !type.IsCommand())
                            continue;

                        object instance;
                        try
                        {
                            instance = Activator.CreateInstance(type);
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogWarning(
                                $"[unity-connector] Command type '{type.FullName}' cannot be instantiated: {ex.Message}");
                            continue;
                        }

                        if (instance is not CommandBase commandBase)
                        {
                            UnityEngine.Debug.LogWarning(
                                $"[unity-connector] Command type '{type.FullName}' must inherit CommandBase.");
                            continue;
                        }

                        if (instance is not ICommandDescriptorProvider metadata)
                            continue;

                        var descriptor = metadata.Descriptor;
                        if (descriptor == null)
                            continue;

                        var handler = CreateHandler(type, descriptor);
                        if (handler == null)
                        {
                            UnityEngine.Debug.LogWarning(
                                $"[unity-connector] Command '{descriptor.Name}' does not implement supported command interfaces.");
                            continue;
                        }

                        list.Add(handler);
                    }
                }

                _handlers = list
                    .OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static ICommandHandler CreateHandler(Type type, CommandDescriptor descriptor)
        {
            if (typeof(ICommand).IsAssignableFrom(type))
                return new NoParamHandler(descriptor, type);

            var syncInterface = type
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
            if (syncInterface != null)
            {
                var paramType = syncInterface.GetGenericArguments()[0];
                var handlerType = typeof(SyncHandler<>).MakeGenericType(paramType);
                return Activator.CreateInstance(handlerType, descriptor, type) as ICommandHandler;
            }

            return null;
        }

        private abstract class HandlerBase : ICommandHandler
        {
            private readonly CommandDescriptor _descriptor;
            private readonly Type _paramType;
            private string[] _paramDescriptions;

            protected HandlerBase(CommandDescriptor descriptor, Type fallbackParamType = null)
            {
                _descriptor = descriptor;
                _paramType = descriptor.ParamType ?? fallbackParamType;
            }

            public string Name => _descriptor.Name;
            public CommandScope Scope => _descriptor.Scope;
            public string Description => _descriptor.Description ?? "";
            public string Completion =>
                _descriptor is DeferredCommandDescriptor deferred ? deferred.Completion : "";
            public string[] Aliases => _descriptor.Aliases ?? Array.Empty<string>();
            public int DefaultTimeoutMs => _descriptor.DefaultTimeoutMs;
            public bool AllowConnectionRetry => _descriptor.AllowConnectionRetry;

            public string[] ParamDescriptions =>
                _paramDescriptions ??= _paramType == null
                    ? Array.Empty<string>()
                    : CliParamBinder.Describe(_paramType);

            public abstract void ExecuteCommand(CommandContext context, Dictionary<string, object> parameters);

            protected static Exception Unwrap(Exception ex) =>
                ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
        }

        private sealed class NoParamHandler : HandlerBase
        {
            private readonly Type _commandType;

            public NoParamHandler(CommandDescriptor descriptor, Type commandType) : base(descriptor)
            {
                _commandType = commandType;
            }

            public override void ExecuteCommand(CommandContext context, Dictionary<string, object> parameters)
            {
                try
                {
                    var command = (ICommand)CreateCommandInstance(_commandType);
                    BindRuntime(command, context);
                    command.Run();
                }
                catch (Exception ex)
                {
                    throw Unwrap(ex);
                }
            }
        }

        private sealed class SyncHandler<TParams> : HandlerBase
        {
            private readonly Type _commandType;

            public SyncHandler(CommandDescriptor descriptor, Type commandType)
                : base(descriptor, typeof(TParams))
            {
                _commandType = commandType;
            }

            public override void ExecuteCommand(CommandContext context, Dictionary<string, object> parameters)
            {
                var (bound, error) = CliParamBinder.Bind(typeof(TParams), parameters);
                if (error != null)
                    throw new InvalidOperationException(error);

                try
                {
                    var command = (ICommand<TParams>)CreateCommandInstance(_commandType);
                    BindRuntime(command, context);
                    command.Run((TParams)bound);
                }
                catch (Exception ex)
                {
                    throw Unwrap(ex);
                }
            }
        }

        private sealed class RuntimeAdapter : ICommandRuntime
        {
            private readonly CommandContext _context;
            public RuntimeAdapter(CommandContext context) => _context = context;
            public string CommandId => _context?.CommandId;
            public void CompleteSuccess(object result) => _context?.CompleteSuccess(result);
            public void CompleteFail(string error) => _context?.CompleteFail(error);
            public void MarkRunning() => _context?.MarkRunning();
        }

        private static void BindRuntime(object command, CommandContext context)
        {
            if (command is CommandBase baseCommand)
                baseCommand.BindRuntime(new RuntimeAdapter(context));
            else
                throw new InvalidOperationException($"Command '{command?.GetType().FullName}' must inherit CommandBase.");
        }

        private static object CreateCommandInstance(Type type)
        {
            try
            {
                return Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Command type '{type.FullName}' cannot be instantiated: {ex.Message}",
                    ex);
            }
        }
    }
}
