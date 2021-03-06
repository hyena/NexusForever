﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using NLog;
using NexusForever.Shared.Network;

namespace NexusForever.StsServer.Network.Message
{
    public delegate void MessageHandlerDelegate(NetworkSession session, IReadable message);

    public static class MessageManager
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private delegate IReadable MessageFactoryDelegate();
        private static ImmutableDictionary<string, MessageFactoryDelegate> clientMessageFactories;

        private static ImmutableDictionary<string, MessageHandlerInfo> clientMessageHandlers;

        public static void Initialise()
        {
            InitialiseMessageFactories();
            InitialiseMessageHandlers();
        }

        private static void InitialiseMessageFactories()
        {
            var messageFactories = new Dictionary<string, MessageFactoryDelegate>();

            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                MessageAttribute attribute = type.GetCustomAttribute<MessageAttribute>();
                if (attribute == null)
                    continue;

                NewExpression @new = Expression.New(type.GetConstructor(Type.EmptyTypes));
                messageFactories.Add(attribute.Uri, Expression.Lambda<MessageFactoryDelegate>(@new).Compile());
            }

            clientMessageFactories = messageFactories.ToImmutableDictionary();
            log.Info($"Initialised {clientMessageFactories.Count} message {(clientMessageFactories.Count == 1 ? "factory" : "factories")}.");
        }

        private static void InitialiseMessageHandlers()
        {
            var messageHandlers = new Dictionary<string, MessageHandlerInfo>();

            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                foreach (MethodInfo method in type.GetMethods())
                {
                    MessageHandlerAttribute attribute = method.GetCustomAttribute<MessageHandlerAttribute>();
                    if (attribute == null)
                        continue;

                    ParameterInfo[] parameterInfo = method.GetParameters();

                    #region Debug
                    Debug.Assert(parameterInfo.Length == 2);
                    Debug.Assert(typeof(NetworkSession).IsAssignableFrom(parameterInfo[0].ParameterType));
                    Debug.Assert(typeof(IReadable).IsAssignableFrom(parameterInfo[1].ParameterType));
                    #endregion

                    ParameterExpression sessionParameter = Expression.Parameter(typeof(NetworkSession));
                    ParameterExpression messageParameter = Expression.Parameter(typeof(IReadable));

                    MethodCallExpression call = Expression.Call(method,
                        Expression.Convert(sessionParameter, parameterInfo[0].ParameterType),
                        Expression.Convert(messageParameter, parameterInfo[1].ParameterType));

                    Expression<MessageHandlerDelegate> lambda =
                        Expression.Lambda<MessageHandlerDelegate>(call, sessionParameter, messageParameter);

                    messageHandlers.Add(attribute.Uri, new MessageHandlerInfo(lambda.Compile(), attribute.State));
                }
            }

            clientMessageHandlers = messageHandlers.ToImmutableDictionary();
            log.Info($"Initialised {clientMessageHandlers.Count} message handler(s).");
        }

        public static IReadable GetMessage(string uri)
        {
            return clientMessageFactories.TryGetValue(uri, out MessageFactoryDelegate factory) ? factory.Invoke() : null;
        }

        public static MessageHandlerInfo GetMessageHandler(string uri)
        {
            return clientMessageHandlers.TryGetValue(uri, out MessageHandlerInfo handler) ? handler : null;
        }
    }
}
