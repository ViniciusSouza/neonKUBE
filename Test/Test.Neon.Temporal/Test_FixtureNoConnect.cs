﻿//-----------------------------------------------------------------------------
// FILE:        Test_FixtureNoConnect.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Data;
using Neon.IO;
using Neon.Tasks;
using Neon.Temporal;
using Neon.Temporal.Internal;
using Neon.Xunit;
using Neon.Xunit.Temporal;

using Newtonsoft.Json;
using Xunit;

namespace TestTemporal
{
    /// <summary>
    /// These tests prevent the <see cref="TemporalFixture"/> from establishing a client
    /// connection and then explicitly establishes a connection to run some tests.
    /// </summary>
    [Trait(TestTrait.Category, TestTrait.Buggy)]
    [Trait(TestTrait.Category, TestTrait.Incomplete)]
    [Trait(TestTrait.Category, TestArea.NeonTemporal)]
    [Collection(TestCollection.NonParallel)]
    [CollectionDefinition(TestCollection.NonParallel, DisableParallelization = true)]
    public partial class Test_FixtureNoConnect : IClassFixture<TemporalFixture>, IDisposable
    {
        private TemporalFixture  fixture;
        private TemporalClient   client;

        public Test_FixtureNoConnect(TemporalFixture fixture)
        {
            TestHelper.ResetDocker(this.GetType());

            var settings = new TemporalSettings()
            {
                Namespace              = TemporalFixture.Namespace,
                ProxyLogLevel          = TemporalTestHelper.ProxyLogLevel,
                CreateNamespace        = true,
                Debug                  = TemporalTestHelper.Debug,
                DebugPrelaunched       = TemporalTestHelper.DebugPrelaunched,
                DebugDisableHeartbeats = TemporalTestHelper.DebugDisableHeartbeats,
                ClientIdentity         = TemporalTestHelper.ClientIdentity
            };

            if (fixture.Start(settings, composeFile: TemporalTestHelper.TemporalStackDefinition, reconnect: true, keepRunning: TemporalTestHelper.KeepTemporalServerOpen, noClient: true) == TestFixtureStatus.Started)
            {
                this.fixture = fixture;
                this.client  = fixture.Client = TemporalClient.ConnectAsync(fixture.Settings).Result;

                // Create a worker and register the workflow and activity 
                // implementations to let Temporal know we're open for business.

                var worker = client.NewWorkerAsync().Result;

                worker.RegisterAssemblyAsync(Assembly.GetExecutingAssembly()).WaitWithoutAggregate();
                worker.StartAsync().WaitWithoutAggregate();
            }
            else
            {
                this.fixture = fixture;
                this.client  = fixture.Client;
            }
        }

        public void Dispose()
        {
        }

        //---------------------------------------------------------------------

        [WorkflowInterface(TaskQueue = TemporalTestHelper.TaskQueue)]
        public interface IWorkflowWithResult : IWorkflow
        {
            [WorkflowMethod]
            Task<string> HelloAsync(string name);
        }

        [Workflow(AutoRegister = true)]
        public class WorkflowWithResult : WorkflowBase, IWorkflowWithResult
        {
            public async Task<string> HelloAsync(string name)
            {
                return await Task.FromResult($"Hello {name}!");
            }
        }

        [Fact(Timeout = TemporalTestHelper.TestTimeout)]
        public async Task Workflow_WithResult1()
        {
            await SyncContext.Clear;
            
            // Verify that we can call a simple workflow that accepts a
            // parameter and returns a result.

            var stub = client.NewWorkflowStub<IWorkflowWithResult>();

            Assert.Equal("Hello Jeff!", await stub.HelloAsync("Jeff"));
        }

        [Fact(Timeout = TemporalTestHelper.TestTimeout)]
        public async Task Workflow_WithResult2()
        {
            await SyncContext.Clear;

            // Verify that we can call a simple workflow that accepts a
            // parameter and returns a result.

            var stub = client.NewWorkflowStub<IWorkflowWithResult>();

            Assert.Equal("Hello Jack!", await stub.HelloAsync("Jack"));
        }
    }
}
