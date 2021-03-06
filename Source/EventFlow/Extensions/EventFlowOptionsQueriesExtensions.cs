﻿// The MIT License (MIT)
//
// Copyright (c) 2015 Rasmus Mikkelsen
// https://github.com/rasmus/EventFlow
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EventFlow.Queries;

namespace EventFlow.Extensions
{
    public static class EventFlowOptionsQueriesExtensions
    {
        public static EventFlowOptions AddQueryHandler<TQueryHandler, TQuery, TResult>(
            this EventFlowOptions eventFlowOptions)
            where TQueryHandler : class, IQueryHandler<TQuery, TResult>
            where TQuery : IQuery<TResult>
        {
            return eventFlowOptions
                .RegisterServices(sr => sr.Register<IQueryHandler<TQuery, TResult>, TQueryHandler>());
        }

        public static EventFlowOptions AddQueryHandlers(
            this EventFlowOptions eventFlowOptions,
            params Type[] queryHandlerTypes)
        {
            return eventFlowOptions
                .AddQueryHandlers((IEnumerable<Type>)queryHandlerTypes);
        }

        public static EventFlowOptions AddQueryHandlers(
            this EventFlowOptions eventFlowOptions,
            Assembly fromAssembly)
        {
            var subscribeSynchronousToTypes = fromAssembly
                .GetTypes()
                .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)));
            return eventFlowOptions
                .AddQueryHandlers(subscribeSynchronousToTypes);
        }

        public static EventFlowOptions AddQueryHandlers(
            this EventFlowOptions eventFlowOptions,
            IEnumerable<Type> queryHandlerTypes)
        {
            foreach (var queryHandlerType in queryHandlerTypes)
            {
                var t = queryHandlerType;
                var queryHandlerInterfaces = t
                    .GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
                    .ToList();
                if (!queryHandlerInterfaces.Any())
                {
                    throw new ArgumentException(string.Format(
                        "Type '{0}' is not a ISubscribeSynchronousTo<TEvent>",
                        t.Name));
                }

                eventFlowOptions.RegisterServices(sr =>
                    {
                        foreach (var queryHandlerInterface in queryHandlerInterfaces)
                        {
                            sr.Register(queryHandlerInterface, t);
                        }
                    });
            }

            return eventFlowOptions;
        }
    }
}
