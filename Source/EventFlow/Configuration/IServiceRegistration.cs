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
using EventFlow.Configuration.Registrations;

namespace EventFlow.Configuration
{
    public interface IServiceRegistration
    {
        void Register<TService, TImplementation>(Lifetime lifetime = Lifetime.AlwaysUnique)
            where TImplementation : class, TService
            where TService : class;

        void Register<TService>(Func<IResolverContext, TService> factory, Lifetime lifetime = Lifetime.AlwaysUnique)
            where TService : class;

        void Register(Type serviceType, Type implementationType, Lifetime lifetime = Lifetime.AlwaysUnique);

        void RegisterGeneric(Type serviceType, Type implementationType, Lifetime lifetime = Lifetime.AlwaysUnique);

        void Decorate<TService>(Func<IResolverContext, TService, TService> factory);

        bool HasRegistrationFor<TService>()
            where TService : class;

        IEnumerable<Type> GetRegisteredServices();
            
        IRootResolver CreateResolver(bool validateRegistrations);
    }
}
