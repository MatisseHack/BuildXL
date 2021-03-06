﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using Microsoft.Practices.TransientFaultHandling;
using CLAP;
using BuildXL.Cache.ContentStore.Sessions;
using Grpc.Core;
using ContentStore.Grpc;
using System.Linq;

namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        /// Attempt to connect to a grpc port and send 'Hello'
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Send 'Hello' to another CASaaS")]
        internal void Hello(
            string hash,
            [Required, Description("Machine to send Hello request to")] string host,
            [Description("GRPC port on the target machine"), DefaultValue(ServiceConfiguration.GrpcDisabledPort)] int grpcPort,
            [Description("Name of the memory mapped file used to share GRPC port. 'CASaaS GRPC port' if not specified.")] string grpcPortFileName)
        {
            Initialize();

            if (grpcPort == 0)
            {
                grpcPort = Helpers.GetGrpcPortFromFile(_logger, grpcPortFileName);
            }

            var _channel = new Channel(host, grpcPort, ChannelCredentials.Insecure);
            var _client = new ContentServer.ContentServerClient(_channel);
            var helloResponse = _client.Hello(new HelloRequest(), new CallOptions(deadline: DateTime.UtcNow + TimeSpan.FromSeconds(2)));

            _logger.Always($"Hello response {(helloResponse.Success ? "succeeded" : "failed")}: {helloResponse.ToString()}");
        }
    }
}
