﻿//-----------------------------------------------------------------------------
// FILE:        Test_SyncSignal.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2021 by neonFORGE LLC.  All rights reserved.
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

#pragma warning disable xUnit1026 // Theory methods should use all of their parameters

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Data;
using Neon.IO;
using Neon.Temporal;
using Neon.Temporal.Internal;
using Neon.Xunit;
using Neon.Xunit.Temporal;

using Newtonsoft.Json;
using Xunit;

namespace TestTemporal
{
    [Collection(TestCollection.NonParallel)]
    [CollectionDefinition(TestCollection.NonParallel, DisableParallelization = true)]
    public class Test_SyncSignals : IClassFixture<TemporalFixture>, IDisposable
    {
        private const int testIterations = 2;

        private TemporalFixture     fixture;
        private TemporalClient      client;
        private HttpClient          proxyClient;

        public Test_SyncSignals(TemporalFixture fixture)
        {
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

            if (fixture.Start(settings, composeFile: TemporalTestHelper.TemporalStackDefinition, reconnect: true, keepRunning: TemporalTestHelper.KeepTemporalServerOpen) == TestFixtureStatus.Started)
            {
                this.fixture     = fixture;
                this.client      = fixture.Client;
                this.proxyClient = new HttpClient() { BaseAddress = client.ProxyUri };

                // Create a worker and register the workflow and activity 
                // implementations to let Temporal know we're open for business.

                var worker = client.NewWorkerAsync().Result;

                worker.RegisterAssemblyAsync(Assembly.GetExecutingAssembly()).Wait();
                worker.StartAsync().Wait();
            }
            else
            {
                this.fixture     = fixture;
                this.client      = fixture.Client;
                this.proxyClient = new HttpClient() { BaseAddress = client.ProxyUri };
            }
        }

        public void Dispose()
        {
            if (proxyClient != null)
            {
                proxyClient.Dispose();
                proxyClient = null;
            }
        }

        private void LogStart(int iteration)
        {
            //TemporalHelper.DebugLog("");
            //TemporalHelper.DebugLog("---------------------------------");
            //TemporalHelper.DebugLog("");
            //TemporalHelper.DebugLog($"ITERATION: {iteration}");
        }

        //---------------------------------------------------------------------

        [WorkflowInterface(TaskQueue = TemporalTestHelper.TaskQueue)]
        public interface ISyncSignal : IWorkflow
        {
            [WorkflowMethod]
            Task RunAsync(TimeSpan delay);

            [SignalMethod("signal-void", Synchronous = true)]
            Task SignalAsync(TimeSpan delay);

            [SignalMethod("signal-with-result", Synchronous = true)]
            Task<string> SignalAsync(TimeSpan delay, string input);
        }

        [Workflow(AutoRegister = true)]
        public class SyncSignal : WorkflowBase, ISyncSignal
        {
            public static readonly TimeSpan WorkflowDelay = TimeSpan.FromSeconds(8);
            public static readonly TimeSpan SignalDelay   = TimeSpan.FromSeconds(3);

            public static bool SignalBeforeDelay = false;
            public static bool SignalAfterDelay  = false;

            public static void Clear()
            {
                SignalBeforeDelay = false;
                SignalAfterDelay  = false;
            }

            public async Task RunAsync(TimeSpan delay)
            {
                await Workflow.SleepAsync(delay);
            }

            public async Task SignalAsync(TimeSpan delay)
            {
                SignalBeforeDelay = true;
                await Task.Delay(delay);
                SignalAfterDelay = true;
            }

            public async Task<string> SignalAsync(TimeSpan delay, string input)
            {
                SignalBeforeDelay = true;
                await Task.Delay(delay);
                SignalAfterDelay = true;

                return input;
            }
        }

        [WorkflowInterface(TaskQueue = TemporalTestHelper.TaskQueue)]
        public interface ISyncChildSignal : IWorkflow
        {
            [WorkflowMethod]
            Task<bool> RunAsync(TimeSpan signalDelay, bool signalWithResult);
        }

        [Workflow(AutoRegister = true)]
        public class SyncChildSignal : WorkflowBase, ISyncChildSignal
        {
            public async Task<bool> RunAsync(TimeSpan signalDelay, bool signalWithResult)
            {
                SyncSignal.Clear();

                // Start a child workflow and then send a synchronous
                // signal that returns void or a result depending on
                // the parameter.
                //
                // The method returns TRUE on success.

                var childStub = Workflow.NewChildWorkflowFutureStub<ISyncSignal>();
                var future    = await childStub.StartAsync(SyncSignal.WorkflowDelay);
                var pass      = true;

                if (signalWithResult)
                {
                    var result = await childStub.Stub.SignalAsync(signalDelay, "Hello World!");

                    pass = result == "Hello World!";
                }
                else
                {
                    await childStub.Stub.SignalAsync(signalDelay);
                }

                pass = pass && SyncSignal.SignalBeforeDelay;
                pass = pass && SyncSignal.SignalAfterDelay;

                await future.GetAsync();

                return pass;
            }
        }

        [Theory]
        [Repeat(testIterations)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignal_WithoutResult(int iteration)
        {
            LogStart(iteration);

            // Verify that sending a synchronous signal returning void
            // works as expected when there's no delay executing the signal.

            SyncSignal.Clear();

            var stub = client.NewWorkflowStub<ISyncSignal>();
            var task = stub.RunAsync(SyncSignal.WorkflowDelay);

            await stub.SignalAsync(TimeSpan.Zero);
            Assert.True(SyncSignal.SignalBeforeDelay);
            Assert.True(SyncSignal.SignalAfterDelay);

            await task;
        }

        [Theory]
        [Repeat(testIterations)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignalFuture_WithoutResult(int iteration)
        {
            LogStart(iteration);

            // Verify that sending a synchronous signal returning void
            // via a future works as expected when there's no delay
            // executing the signal.

            SyncSignal.Clear();

            var stub   = client.NewWorkflowFutureStub<ISyncSignal>();
            var future = await stub.StartAsync(SyncSignal.WorkflowDelay);

            await stub.SyncSignalAsync("signal-void", TimeSpan.Zero);
            await future.GetAsync();

            Assert.True(SyncSignal.SignalBeforeDelay);
            Assert.True(SyncSignal.SignalAfterDelay);
        }

        [Theory]
        [Repeat(testIterations)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignal_WithoutResult_AndDelay(int iteration)
        {
            LogStart(iteration);

            // Verify that sending a synchronous signal returning void
            // works as expected when we delay the signal execution long
            // enough to force query retries.

            SyncSignal.Clear();

            var stub = client.NewWorkflowStub<ISyncSignal>();
            var task = stub.RunAsync(SyncSignal.WorkflowDelay);

            await stub.SignalAsync(SyncSignal.SignalDelay);
            Assert.True(SyncSignal.SignalBeforeDelay);
            Assert.True(SyncSignal.SignalAfterDelay);

            await task;
        }

        [Theory]
        [Repeat(testIterations)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignal_WithResult(int iteration)
        {
            LogStart(iteration);

            // Verify that sending a synchronous signal returning a result
            // works as expected when there's no delay executing the signal.

            SyncSignal.Clear();

            var stub   = client.NewWorkflowStub<ISyncSignal>();
            var task   = stub.RunAsync(SyncSignal.WorkflowDelay);
            var result = await stub.SignalAsync(TimeSpan.Zero, "Hello World!");

            Assert.True(SyncSignal.SignalBeforeDelay);
            Assert.True(SyncSignal.SignalAfterDelay);
            Assert.Equal("Hello World!", result);

            await task;
        }

        [Theory]
        [Repeat(testIterations)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignalFuture_WithResult(int iteration)
        {
            LogStart(iteration);

            // Verify that sending a synchronous signal returning void
            // via a future works as expected when there's no delay
            // executing the signal.

            SyncSignal.Clear();

            var stub   = client.NewWorkflowFutureStub<ISyncSignal>();
            var future = await stub.StartAsync(SyncSignal.WorkflowDelay);
            var result = await stub.SyncSignalAsync<string>("signal-with-result", TimeSpan.Zero, "Hello World!");

            Assert.Equal("Hello World!", result);

            await future.GetAsync();

            Assert.True(SyncSignal.SignalBeforeDelay);
            Assert.True(SyncSignal.SignalAfterDelay);
        }

        [Theory]
        [Repeat(testIterations)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignal_WithResult_AndDelay(int iteration)
        {
            LogStart(iteration);

            // Verify that sending a synchronous signal returning a result
            // works as expected when we delay the signal execution long
            // enough to force query retries.

            SyncSignal.Clear();

            var stub   = client.NewWorkflowStub<ISyncSignal>();
            var task   = stub.RunAsync(SyncSignal.WorkflowDelay);
            var result = await stub.SignalAsync(SyncSignal.SignalDelay, "Hello World!");

            Assert.True(SyncSignal.SignalBeforeDelay);
            Assert.True(SyncSignal.SignalAfterDelay);
            Assert.Equal("Hello World!", result);

            await task;
        }

        [Theory]
        [Repeat(testIterations)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignalChild_WithoutResult(int iteration)
        {
            LogStart(iteration);

            // Verify that sending a synchronous child signal returning void
            // works as expected when there's no delay executing the signal.

            var stub = client.NewWorkflowStub<ISyncChildSignal>();
            var task = stub.RunAsync(TimeSpan.Zero, signalWithResult: false);

            Assert.True(await task);
        }

        [Theory]
        [Repeat(testIterations)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignalChild_WithoutResult_AndDelay(int iteration)
        {
            LogStart(iteration);

            // Verify that sending a synchronous child signal returning void
            // works as expected when we delay the signal execution long
            // enough to force query retries.

            var stub = client.NewWorkflowStub<ISyncChildSignal>();
            var task = stub.RunAsync(SyncSignal.SignalDelay, signalWithResult: false);

            Assert.True(await task);
        }

        [Theory]
        [Repeat(testIterations)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignalChild_WithResult(int iteration)
        {
            LogStart(iteration);

            // Verify that sending a synchronous child signal returning a result
            // works as expected when there's no delay executing the signal.

            var stub = client.NewWorkflowStub<ISyncChildSignal>();
            var task = stub.RunAsync(TimeSpan.Zero, signalWithResult: true);

            Assert.True(await task);
        }

        [Theory]
        [Repeat(testIterations)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignalChild_WithResult_AndDelay(int iteration)
        {
            LogStart(iteration);

            // Verify that sending a synchronous child signal returning a result
            // works as expected when we delay the signal execution long
            // enough to force query retries.

            var stub = client.NewWorkflowStub<ISyncChildSignal>();
            var task = stub.RunAsync(SyncSignal.SignalDelay, signalWithResult: true);

            Assert.True(await task);
        }

        //---------------------------------------------------------------------

        [WorkflowInterface(TaskQueue = TemporalTestHelper.TaskQueue)]
        public interface IQueuedSignal : IWorkflow
        {
            [WorkflowMethod(Name = "run-void")]
            Task RunVoidAsync();

            [SignalMethod("signal-void", Synchronous = true)]
            Task SignalVoidAsync(string name);

            [WorkflowMethod(Name = "run-with-result")]
            Task RunResultAsync();

            [SignalMethod("signal-with-result", Synchronous = true)]
            Task<string> SignalResultAsync(string name);
        }

        [Workflow(AutoRegister = true)]
        public class QueuedSignal : WorkflowBase, IQueuedSignal
        {
            public static bool SignalProcessed;
            public static string Name;

            public static void Clear()
            {
                SignalProcessed = false;
                Name = null;
            }

            private WorkflowQueue<SignalRequest>         voidQueue;
            private WorkflowQueue<SignalRequest<string>> resultQueue;

            public async Task RunVoidAsync()
            {
                voidQueue = await Workflow.NewQueueAsync<SignalRequest>();

                var signalRequest = await voidQueue.DequeueAsync();

                SignalProcessed = true;
                Name = signalRequest.Arg<string>("name");

                await signalRequest.ReplyAsync();
            }

            public async Task SignalVoidAsync(string name)
            {
                var signalRequest = new SignalRequest();

                await voidQueue.EnqueueAsync(signalRequest);
                throw new WaitForSignalReplyException();
            }

            public async Task RunResultAsync()
            {
                resultQueue = await Workflow.NewQueueAsync<SignalRequest<string>>();

                var signalRequest = await resultQueue.DequeueAsync();

                SignalProcessed = true;
                Name = signalRequest.Arg<string>("name");

                await signalRequest.ReplyAsync($"Hello {Name}!");
            }

            public async Task<string> SignalResultAsync(string name)
            {
                var signalRequest = new SignalRequest<string>();

                await resultQueue.EnqueueAsync(signalRequest);
                throw new WaitForSignalReplyException();
            }
        }

        [Theory]
        [Repeat(testIterations)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignal_Queued_WithoutResult(int iteration)
        {
            LogStart(iteration);

            // Verify that [SignalRequest] works for void signals.
            //
            // This is a bit tricky.  The workflow waits for a signal,
            // processes it and then returns.  We'll know this happened
            // because the static [SignalProcessed] property will be set
            // and also because the signal and workflow methods returned.

            QueuedSignal.Clear();

            var stub = client.NewWorkflowStub<IQueuedSignal>();
            var task = stub.RunVoidAsync();

            await stub.SignalVoidAsync("Jack");
            await task;

            Assert.True(QueuedSignal.SignalProcessed);
            Assert.Equal("Jack", QueuedSignal.Name);
        }

        [Theory]
        [Repeat(testIterations)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignal_Queued_WithResult(int iteration)
        {
            LogStart(iteration);

            // Verify that [SignalRequest] works for signals that return 
            // a result.
            //
            // This is a bit tricky.  The workflow waits for a signal,
            // processes it and then returns.  We'll know this happened
            // because the static [SignalProcessed] property will be set
            // and also because the signal and workflow methods returned.

            QueuedSignal.Clear();

            var stub = client.NewWorkflowStub<IQueuedSignal>();
            var task = stub.RunResultAsync();

            var result = await stub.SignalResultAsync("Jill");

            await task;

            Assert.True(QueuedSignal.SignalProcessed);
            Assert.True(result == "Hello Jill!");
            Assert.Equal("Jill", QueuedSignal.Name);
        }

        //---------------------------------------------------------------------

        [ActivityInterface(TaskQueue = TemporalTestHelper.TaskQueue)]
        public interface IDelayActivity : IActivity
        {
            [ActivityMethod]
            Task DelayAsync(TimeSpan delay);
        }

        [Activity(AutoRegister = true)]
        public class DelayActivity : ActivityBase, IDelayActivity
        {
            public async Task DelayAsync(TimeSpan delay)
            {
                await Task.Delay(delay);
            }
        }

        [WorkflowInterface(TaskQueue = TemporalTestHelper.TaskQueue)]
        public interface ISignalWithActivity : IWorkflow
        {
            [WorkflowMethod(Name = "run")]
            Task RunAsync(TimeSpan delay);

            [SignalMethod("signal", Synchronous = true)]
            Task<string> SignalAsync(string name);
        }

        [Workflow(AutoRegister = true)]
        public class SignalWithActivity : WorkflowBase, ISignalWithActivity
        {
            private WorkflowQueue<SignalRequest<string>> signalQueue;

            public async Task RunAsync(TimeSpan delay)
            {
                signalQueue = await Workflow.NewQueueAsync<SignalRequest<string>>();

                var stub = Workflow.NewActivityStub<IDelayActivity>();

                await stub.DelayAsync(delay);

                var signalRequest = await signalQueue.DequeueAsync();
                var name          = signalRequest.Arg<string>("name");
                var reply         = (string)null;

                if (name != null)
                {
                    reply = $"Hello {name}!";
                }

                await signalRequest.ReplyAsync(reply);
            }

            public async Task<string> SignalAsync(string name)
            {
                await signalQueue.EnqueueAsync(new SignalRequest<string>());
                throw new WaitForSignalReplyException();
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        [InlineData(20)]
        [InlineData(30)]
        [InlineData(40)]
        [InlineData(50)]
        [InlineData(60)]
        [InlineData(70)]
        [InlineData(80)]
        [InlineData(90)]
        [InlineData(100)]
        [InlineData(200)]
        [InlineData(500)]
        [InlineData(1000)]
        [InlineData(2000)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignal_WithActivity(double delayMilliSeconds)
        {
            // Verify that synchronous signals work when the workflow also
            // executes an activity.  We're going to have the activity
            // execute for varying periods of time.
            //
            // This test is actually verifying that we fixed this
            // race condition:
            //
            //      https://github.com/nforgeio/neonKUBE/issues/769

            var stub = client.NewWorkflowStub<ISignalWithActivity>();
            var task = stub.RunAsync(TimeSpan.FromMilliseconds(delayMilliSeconds));

            var result = await stub.SignalAsync("Jill");

            await task;

            Assert.Equal("Hello Jill!", result);
        }

        //---------------------------------------------------------------------

        [ActivityInterface(TaskQueue = TemporalTestHelper.TaskQueue)]
        public interface ISignalBlastDelayActivity : IActivity
        {
            [ActivityMethod]
            Task DelayAsync(TimeSpan delay);
        }

        [Activity(AutoRegister = true)]
        public class SignalBlastDelayActivity : ActivityBase, ISignalBlastDelayActivity
        {
            public async Task DelayAsync(TimeSpan delay)
            {
                await Task.Delay(delay);
            }
        }

        [WorkflowInterface(TaskQueue = TemporalTestHelper.TaskQueue)]
        public interface ISignalBlastProcessor : IWorkflow
        {
            [WorkflowMethod(Name = "run")]
            Task RunAsync(TimeSpan delay);

            [SignalMethod("signal", Synchronous = true)]
            Task<string> SignalAsync(string name);
        }

        [Workflow(AutoRegister = true)]
        public class SignalBlastProcessor : WorkflowBase, ISignalBlastProcessor
        {
            private WorkflowQueue<SignalRequest<string>> signalQueue;

            public async Task RunAsync(TimeSpan delay)
            {
                var delayStub = Workflow.NewLocalActivityStub<ISignalBlastDelayActivity, SignalBlastDelayActivity>();

                signalQueue = await Workflow.NewQueueAsync<SignalRequest<string>>(int.MaxValue);

                // Process messages until we get one that passes NULL.

                while (true)
                {
                    var signalRequest = await signalQueue.DequeueAsync();
                    var name          = signalRequest.Arg<string>("name");
                    var reply         = (string)null;

                    if (name != null)
                    {
                        reply = $"Hello {name}!";

                        if (delay > TimeSpan.Zero)
                        {
                            await delayStub.DelayAsync(delay);
                        }
                    }

                    await signalRequest.ReplyAsync(reply);

                    if (name == null)
                    {
                        break;
                    }
                }
            }

            public async Task<string> SignalAsync(string name)
            {
                await signalQueue.EnqueueAsync(new SignalRequest<string>());
                throw new WaitForSignalReplyException();
            }
        }

        [Theory]
        [InlineData(1, 0.0)]
        [InlineData(10, 1.0)]
        [InlineData(100, 0.0)]
        //[InlineData(1000, 0.0)]
        //[InlineData(2000, 0.0)]
        //[InlineData(4000, 0.0)]
        //[InlineData(8000, 0.0)]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonTemporal)]
        public async Task SyncSignal_Blast(int count, double delaySeconds)
        {
            // Blast a bunch of synchronous signals at a workflow instance
            // to verify that we've fixed:
            //
            //      https://github.com/nforgeio/neonKUBE/issues/773

            var stub = client.NewWorkflowStub<ISignalBlastProcessor>();
            var task = stub.RunAsync(TimeSpan.FromMilliseconds(delaySeconds));

            for (int i = 0; i < count; i++)
            {
                Assert.Equal("Hello Jill!", await stub.SignalAsync("Jill"));
            }

            Assert.Null(await stub.SignalAsync(null));  // Signal the workflow to complete

            await task;
        }
    }
}
