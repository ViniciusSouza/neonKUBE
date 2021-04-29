﻿//-----------------------------------------------------------------------------
// FILE:        Test_StubManager.WorkflowGenerate.cs
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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

using Test.Neon.Models;
using Newtonsoft.Json.Linq;

namespace TestTemporal
{
    public partial class Test_StubManager
    {
        //---------------------------------------------------------------------

        [ActivityInterface(TaskQueue = TemporalTestHelper.TaskQueue)]
        public interface IActivityEntryVoidNoArgs : IActivity
        {
            [ActivityMethod]
            Task RunAsync();
        }

        public class ActivityEntryVoidNoArgs : ActivityBase, IActivityEntryVoidNoArgs
        {
            public async Task RunAsync()
            {
                await Task.CompletedTask;
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Generate_ActivityEntryVoidNoArgs()
        {
            Assert.NotNull(StubManager.NewActivityStub<IActivityEntryVoidNoArgs>(client, new DummyWorkflow().Workflow));
            Assert.NotNull(StubManager.NewLocalActivityStub<IActivityEntryVoidNoArgs, ActivityEntryVoidNoArgs>(client, new DummyWorkflow().Workflow));
        }

        //---------------------------------------------------------------------

        [ActivityInterface(TaskQueue = TemporalTestHelper.TaskQueue)]
        public interface IActivityEntryVoidWithArgs : IActivity
        {
            [ActivityMethod]
            Task RunAsync(string arg1, int arg2);
        }

        public class ActivityEntryVoidWithArgs : ActivityBase, IActivityEntryVoidWithArgs
        {
            public async Task RunAsync(string arg1, int arg2)
            {
                await Task.CompletedTask;
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Generate_ActivityEntryVoidWithArgs()
        {
            Assert.NotNull(StubManager.NewActivityStub<IActivityEntryVoidWithArgs>(client, new DummyWorkflow().Workflow));
            Assert.NotNull(StubManager.NewLocalActivityStub<IActivityEntryVoidWithArgs, ActivityEntryVoidWithArgs>(client, new DummyWorkflow().Workflow));
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Generate_ActivityEntryVoidWithOptions()
        {
            Assert.NotNull(StubManager.NewActivityStub<IActivityEntryVoidWithArgs>(client, new DummyWorkflow().Workflow, options: new ActivityOptions() { Namespace = "my-namespace" }));
            Assert.NotNull(StubManager.NewLocalActivityStub<IActivityEntryVoidWithArgs, ActivityEntryVoidWithArgs>(client, new DummyWorkflow().Workflow, options: new LocalActivityOptions()));
        }

        //---------------------------------------------------------------------

        [ActivityInterface(TaskQueue = TemporalTestHelper.TaskQueue)]
        public interface IActivityEntryResultWithArgs : IActivity
        {
            [ActivityMethod]
            Task<int> RunAsync(string arg1, int arg2);
        }

        public class ActivityEntryResultWithArgs : ActivityBase, IActivityEntryResultWithArgs
        {
            public async Task<int> RunAsync(string arg1, int arg2)
            {
                return await Task.FromResult(1);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Generate_ActivityResultWithArgs()
        {
            Assert.NotNull(StubManager.NewActivityStub<IActivityEntryResultWithArgs>(client, new DummyWorkflow().Workflow));
            Assert.NotNull(StubManager.NewLocalActivityStub<IActivityEntryResultWithArgs, ActivityEntryResultWithArgs>(client, new DummyWorkflow().Workflow));
        }

        //---------------------------------------------------------------------

        [ActivityInterface(TaskQueue = TemporalTestHelper.TaskQueue)]
        public interface IActivityMultiMethods : IActivity
        {
            [ActivityMethod]
            Task RunAsync();

            [ActivityMethod(Name = "one")]
            Task<int> RunAsync(string arg1);

            [ActivityMethod(Name = "two")]
            Task<int> RunAsync(string arg1, string arg2);
        }

        public class ActivityMultiMethods : ActivityBase, IActivityMultiMethods
        {
            public async Task RunAsync()
            {
                await Task.CompletedTask;
            }

            public async Task<int> RunAsync(string arg1)
            {
                return await Task.FromResult(1);
            }

            public async Task<int> RunAsync(string arg1, string arg2)
            {
                return await Task.FromResult(2);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Generate_ActivityMultiMethods()
        {
            Assert.NotNull(StubManager.NewActivityStub<IActivityMultiMethods>(client, new DummyWorkflow().Workflow));
            Assert.NotNull(StubManager.NewLocalActivityStub<IActivityMultiMethods, ActivityMultiMethods>(client, new DummyWorkflow().Workflow));
        }
    }
}
