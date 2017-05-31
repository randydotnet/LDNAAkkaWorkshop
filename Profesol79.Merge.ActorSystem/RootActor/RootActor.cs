﻿//  --------------------------------------------------------------------------------------------------------------------
// <copyright company="profesor79.pl" file="RootActor.cs">
// Copyright (c) 2017 All Right Reserved
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY 
// KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
// PARTICULAR PURPOSE.
// </copyright>
// <summary>
// Created: 2017-05-15, 2:37 PM
// Last changed by: profesor79, 2017-05-26, 8:20 AM 
// </summary>
//   --------------------------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Profesor79.Merge.ActorSystem.RootActor
{
    using System;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;

    using Akka.Actor;
    using Akka.DI.Core;
    using Akka.Routing;

    using Profesor79.Merge.ActorSystem.BaseObjects;
    using Profesor79.Merge.ActorSystem.FileReader;
    using Profesor79.Merge.ActorSystem.FileWriter;
    using Profesor79.Merge.ActorSystem.FlowControl;
    using Profesor79.Merge.ActorSystem.ValidatorActor;
    using Profesor79.Merge.ActorSystem.WebCrawler;
    using Profesor79.Merge.Contracts;
    using Profesor79.Merge.Domain;

    /// <summary>The root actor.</summary>
    public partial class RootActor : BaseActorClass
    {
        /// <summary>The _system configuration.</summary>
        private readonly ISystemConfiguration _systemConfiguration;

        private DateTime _started;

        /// <summary>Initializes a new instance of the <see cref="RootActor"/> class.</summary>
        /// <param name="systemConfiguration">The system Configuration.</param>
        public RootActor(ISystemConfiguration systemConfiguration)
        {
            _systemConfiguration = systemConfiguration;

            Receive<RootActorMessages.AddressBookRequest>(
                a =>
                    {
                        Sender.Tell(new RootActorMessages.AddressBook(_actorDictionary));
                    });
            Receive<RootActorMessages.StartSystem>(
                m =>
                    {
                        _started = DateTime.Now;
                        CreateActors();
                        SendActorBook();
                        _actorDictionary["ValidatorActor"].Tell(new ValidatorMessages.Validate(m.InputFilePath, m.OutputFilePath, _actorDictionary));
                    });

            Receive<RootActorMessages.FatalError>(
                a =>
                    {
                        _log.Error($"shutting down system - fatal error received, description: {a.Description ?? "none provided..."}");
                        StopSystem();
                    });

            Receive<RootActorMessages.HaltSystem>(
                a =>
                    {
                        _log.Info("Halting system");
                        var duration = (DateTime.Now - _started).TotalSeconds;
                        _log.Info("**************************************");
                        _log.Info($"Time consumed: {duration} seconds");
                        _log.Info("**************************************");
                        Context.System.Terminate();
                        Thread.Sleep(3000); // allow to flush log buffer
                    });

            Receive<RootActorMessages.ProcessFinished>(a => { StopSystem(); });
        }

        /// <summary>The create actors could be implemented as a factory
        /// this way gives ability to send actor references.</summary>
        private void CreateActors()
        {
            var actorSuffix = Guid.NewGuid().ToString().Substring(0, 6);
            _actorDictionary.Add("root", Self);
            _actorDictionary.Add("FileValidatorActor", Context.ActorOf(Context.DI().Props<FileValidatorActor>(), $"FileValidatorActor{actorSuffix}"));
            _actorDictionary.Add(
                "FileReaderActor",
                Context.ActorOf(Context.DI().Props<FileReaderActor>().WithDispatcher("my-dispatcher"), $"FileReaderActor{actorSuffix}"));

            _actorDictionary.Add("FileWriterActor", Context.ActorOf(Context.DI().Props<FileWriterActor>(), $"FileWriterActor{actorSuffix}"));
            _actorDictionary.Add("WebCheckerActor", Context.ActorOf(Context.DI().Props<WebCheckerActor>(), $"WebCheckerActor{actorSuffix}"));
            _actorDictionary.Add(
                "FlowControlActor",
                Context.ActorOf(Context.DI().Props<FlowControlActor>().WithDispatcher("my-dispatcher"), $"FlowControlActor{actorSuffix}"));
            _actorDictionary.Add(
                "DataDispatcherActor",
                Context.ActorOf(Context.DI().Props<DataDispatcherActor>(), $"DataDispatcherActor{actorSuffix}"));
            _actorDictionary.Add("ValidatorActor", Context.ActorOf(Context.DI().Props<ValidatorActor>(), $"ValidatorActor{actorSuffix}"));

            _actorDictionary.Add(
                "DataDistributorActor",
                Context.ActorOf(
                    Context.DI()
                        .Props<DataDistributorActor>()
                        .WithRouter(new RoundRobinPool((int)_systemConfiguration.DataDistributorActorCount))
                        .WithDispatcher("my-dispatcher"),
                    $"DataDistributorActor{actorSuffix}"));



            CreateRemoteCrawlerGroup();
        }

        private void CreateRemoteCrawler()
        {
            var hostname = "e435106956aa";
            var remoteAddress = Address.Parse($"akka.tcp://DeployTarget@{hostname}:8090");

            //  system.ActorOf();
            //deploy remotely via code
            var remoteScope = new RemoteScope(remoteAddress);
            var remoteEcho2 =
                Context.ActorOf(
            Props.Create(() => new WebCrawlerActor(new AppSettingsConfiguration(), Self))
                                  .WithRouter(new RoundRobinPool(2)) // new DefaultResizer(1, 2, messagesPerResize: 500)
                             .WithDispatcher("my-dispatcher")
                        .WithDeploy(Deploy.None.WithScope(remoteScope)),
                    "WebCrawlerActor2");

            _actorDictionary.Add("WebCrawlerActor", remoteEcho2);



        }

        private void CreateRemoteCrawlerGroup()
        {
            var hostname = "374110044f24";
            var hostname2 = "25b360699a27";

            var remoteAddress2 = Address.Parse($"akka.tcp://DeployTarget@{hostname2}:8090");
            var remoteScope2 = new RemoteScope(remoteAddress2);
            var remoteCrawler1 =
                Context.ActorOf(
            Props.Create(() => new WebCrawlerActor(new AppSettingsConfiguration(), Self))
            .WithRouter(new RoundRobinPool(2)) // new DefaultResizer(1, 2, messagesPerResize: 500)
                             .WithDispatcher("my-dispatcher")
            .WithDeploy(Deploy.None.WithScope(remoteScope2)), "a");

            var remoteAddress = Address.Parse($"akka.tcp://DeployTarget@{hostname}:8090");

            var remoteScope = new RemoteScope(remoteAddress);
            var remoteCrawler2 =
                Context.ActorOf(
            Props.Create(() => new WebCrawlerActor(new AppSettingsConfiguration(), Self))
            .WithRouter(new RoundRobinPool(2)) // new DefaultResizer(1, 2, messagesPerResize: 500)
                             .WithDispatcher("my-dispatcher")
            .WithDeploy(Deploy.None.WithScope(remoteScope)), "remoteCrawler01");

            var workers = new List<string> { remoteCrawler1.Path.ToString(), remoteCrawler2.Path.ToString() };
            var router = Context.ActorOf(Props.Empty.WithRouter(new RoundRobinGroup(workers)), "some-group");
            _actorDictionary.Add("WebCrawlerActor", router);
        }

        /// <summary>The send actor book.</summary>
        private void SendActorBook()
        {

            for (var i = 0; i < _systemConfiguration.DataDistributorActorCount; i++)
            {
                _actorDictionary["DataDistributorActor"].Tell(new RootActorMessages.AddressBook(_actorDictionary));
            }

            _actorDictionary["FileReaderActor"].Tell(new RootActorMessages.AddressBook(_actorDictionary));
        }

        /// <summary>The stop system.</summary>
        private void StopSystem()
        {
            foreach (var actor in _actorDictionary.Where(o => o.Key != "root"))
            {
                _log.Info($"Shutting actor:{actor.Key}");
                actor.Value.Tell(PoisonPill.Instance);
            }

            Self.Tell(new RootActorMessages.HaltSystem());
        }
    }
}
