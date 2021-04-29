﻿//-----------------------------------------------------------------------------
// FILE:        Test_Messages.Workflow.cs
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
using Neon.Time;
using Neon.Xunit;
using Neon.Xunit.Temporal;

using Newtonsoft.Json;
using Test.Neon.Models;
using Xunit;

namespace TestTemporal
{
    public partial class Test_Messages
    {
        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityRegisterRequest()
        {
            ActivityRegisterRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityRegisterRequest();

                Assert.Equal(InternalMessageTypes.ActivityRegisterReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityRegisterRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Equal(0, message.WorkerId);
                Assert.Null(message.Name);
                Assert.False(message.DisableAlreadyRegisteredCheck);

                // Round-trip

                message.ClientId  = 444;
                message.RequestId = 555;
                message.WorkerId  = 666;
                message.Name      = "my-name";
                message.DisableAlreadyRegisteredCheck = true;

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.WorkerId);
                Assert.Equal("my-name", message.Name);
                Assert.True(message.DisableAlreadyRegisteredCheck);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityRegisterRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.WorkerId);
                Assert.Equal("my-name", message.Name);
                Assert.True(message.DisableAlreadyRegisteredCheck);

                // Verify Clone()

                message = (ActivityRegisterRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.WorkerId);
                Assert.Equal("my-name", message.Name);
                Assert.True(message.DisableAlreadyRegisteredCheck);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.WorkerId);
                Assert.Equal("my-name", message.Name);
                Assert.True(message.DisableAlreadyRegisteredCheck);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityRegisterReply()
        {
            ActivityRegisterReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityRegisterReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityRegisterReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.RequestId);
                Assert.Equal(0, message.ClientId);
                Assert.Null(message.Error);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityRegisterReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                // Verify Clone()

                message = (ActivityRegisterReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityExecuteRequest()
        {
            ActivityExecuteRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityExecuteRequest();

                Assert.Equal(InternalMessageTypes.ActivityExecuteReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityExecuteRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Activity);
                Assert.Null(message.Args);
                Assert.Null(message.Options);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Activity = "my-activity";
                message.Args = new byte[] { 0, 1, 2, 3, 4 };
                message.Options = new ActivityOptions()
                {
                    TaskQueue              = "my-taskqueue",
                    ScheduleToCloseTimeout = TimeSpan.FromSeconds(1),
                    ScheduleToStartTimeout = TimeSpan.FromSeconds(2),
                    StartToCloseTimeout    = TimeSpan.FromSeconds(3),
                    HeartbeatTimeout       = TimeSpan.FromSeconds(4),
                    WaitForCancellation    = true,
                    RetryPolicy           = new RetryPolicy() { MaximumInterval = TimeSpan.FromSeconds(5) }
                };

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("my-activity", message.Activity);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal("my-taskqueue", message.Options.TaskQueue);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(2), message.Options.ScheduleToStartTimeout);
                Assert.Equal(TimeSpan.FromSeconds(3), message.Options.StartToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(4), message.Options.HeartbeatTimeout);
                Assert.True(message.Options.WaitForCancellation);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityExecuteRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("my-activity", message.Activity);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal("my-taskqueue", message.Options.TaskQueue);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(2), message.Options.ScheduleToStartTimeout);
                Assert.Equal(TimeSpan.FromSeconds(3), message.Options.StartToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(4), message.Options.HeartbeatTimeout);
                Assert.True(message.Options.WaitForCancellation);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);

                // Verify Clone()

                message = (ActivityExecuteRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("my-activity", message.Activity);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal("my-taskqueue", message.Options.TaskQueue);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(2), message.Options.ScheduleToStartTimeout);
                Assert.Equal(TimeSpan.FromSeconds(3), message.Options.StartToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(4), message.Options.HeartbeatTimeout);
                Assert.True(message.Options.WaitForCancellation);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("my-activity", message.Activity);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal("my-taskqueue", message.Options.TaskQueue);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(2), message.Options.ScheduleToStartTimeout);
                Assert.Equal(TimeSpan.FromSeconds(3), message.Options.StartToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(4), message.Options.HeartbeatTimeout);
                Assert.True(message.Options.WaitForCancellation);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityExecuteReply()
        {
            ActivityExecuteReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityExecuteReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityExecuteReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Error);
                Assert.Null(message.Result);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");
                message.Result = new byte[] { 5, 6, 7, 8, 9 };

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityExecuteReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);

                // Verify Clone()

                message = (ActivityExecuteReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityInvokeRequest()
        {
            ActivityInvokeRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityInvokeRequest();

                Assert.Equal(InternalMessageTypes.ActivityInvokeReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityInvokeRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Activity);
                Assert.Null(message.Args);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Activity = "my-activity";
                message.Args = new byte[] { 0, 1, 2, 3, 4 };

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("my-activity", message.Activity);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityInvokeRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("my-activity", message.Activity);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);

                // Verify Clone()

                message = (ActivityInvokeRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("my-activity", message.Activity);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("my-activity", message.Activity);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityInvokeReply()
        {
            ActivityInvokeReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityInvokeReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityInvokeReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Error);
                Assert.Null(message.Result);
                Assert.False(message.Pending);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");
                message.Result = new byte[] { 5, 6, 7, 8, 9 };
                message.Pending = true;

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);
                Assert.True(message.Pending);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityInvokeReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);
                Assert.True(message.Pending);

                // Verify Clone()

                message = (ActivityInvokeReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);
                Assert.True(message.Pending);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);
                Assert.True(message.Pending);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityGetHeartbeatDetailsRequest()
        {
            ActivityGetHeartbeatDetailsRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityGetHeartbeatDetailsRequest();

                Assert.Equal(InternalMessageTypes.ActivityGetHeartbeatDetailsReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetHeartbeatDetailsRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetHeartbeatDetailsRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(555, message.RequestId);

                // Verify Clone()

                message = (ActivityGetHeartbeatDetailsRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(555, message.RequestId);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityGetHeartbeatDetailsReply()
        {
            ActivityGetHeartbeatDetailsReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityGetHeartbeatDetailsReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetHeartbeatDetailsReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Error);
                Assert.Null(message.Details);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");
                message.Details = new byte[] { 5, 6, 7, 8, 9 };

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Details);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetHeartbeatDetailsReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Details);

                // Verify Clone()

                message = (ActivityGetHeartbeatDetailsReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Details);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Details);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityRecordHeartbeatRequest()
        {
            ActivityRecordHeartbeatRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityRecordHeartbeatRequest();

                Assert.Equal(InternalMessageTypes.ActivityRecordHeartbeatReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityRecordHeartbeatRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.TaskToken);
                Assert.Null(message.Details);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.TaskToken = new byte[] { 5, 6, 7, 8, 9 };
                message.Details = new byte[] { 0, 1, 2, 3, 4 };

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.TaskToken);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Details);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityRecordHeartbeatRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.TaskToken);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Details);

                // Verify Clone()

                message = (ActivityRecordHeartbeatRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.TaskToken);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Details);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.TaskToken);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Details);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityRecordHeartbeatReply()
        {
            ActivityRecordHeartbeatReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityRecordHeartbeatReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityRecordHeartbeatReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Error);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityRecordHeartbeatReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                // Verify Clone()

                message = (ActivityRecordHeartbeatReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityHasHeartbeatDetailsRequest()
        {
            ActivityHasHeartbeatDetailsRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityHasHeartbeatDetailsRequest();

                Assert.Equal(InternalMessageTypes.ActivityHasHeartbeatDetailsReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityHasHeartbeatDetailsRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityHasHeartbeatDetailsRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);

                // Verify Clone()

                message = (ActivityHasHeartbeatDetailsRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityHasHeartbeatDetailsReply()
        {
            ActivityHasHeartbeatDetailsReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityHasHeartbeatDetailsReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityHasHeartbeatDetailsReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Error);
                Assert.False(message.HasDetails);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");
                message.HasDetails = true;

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.True(message.HasDetails);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityHasHeartbeatDetailsReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.True(message.HasDetails);

                // Verify Clone()

                message = (ActivityHasHeartbeatDetailsReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.True(message.HasDetails);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.True(message.HasDetails);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityStoppingRequest()
        {
            ActivityStoppingRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityStoppingRequest();

                Assert.Equal(InternalMessageTypes.ActivityStoppingReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityStoppingRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.ActivityId);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.ActivityId = "my-activityid";

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityStoppingRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("my-activityid", message.ActivityId);

                // Verify Clone()

                message = (ActivityStoppingRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("my-activityid", message.ActivityId);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("my-activityid", message.ActivityId);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityStoppingReply()
        {
            ActivityStoppingReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityStoppingReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityStoppingReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Error);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityStoppingReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                // Verify Clone()

                message = (ActivityStoppingReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityExecuteLocalRequest()
        {
            ActivityExecuteLocalRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityExecuteLocalRequest();

                Assert.Equal(InternalMessageTypes.ActivityExecuteLocalReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityExecuteLocalRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Args);
                Assert.Null(message.Options);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Args = new byte[] { 0, 1, 2, 3, 4 };
                message.Options = new LocalActivityOptions()
                {
                    ScheduleToCloseTimeout = TimeSpan.FromSeconds(1),
                    RetryPolicy = new RetryPolicy() { MaximumInterval = TimeSpan.FromSeconds(5) }
                };

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityExecuteLocalRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);

                // Verify Clone()

                message = (ActivityExecuteLocalRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityExecuteLocalReply()
        {
            ActivityExecuteLocalReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityExecuteLocalReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityExecuteLocalReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Error);
                Assert.Null(message.Result);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");
                message.Result = new byte[] { 0, 1, 2, 3, 4 };

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Result);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityExecuteLocalReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Result);

                // Verify Clone()

                message = (ActivityExecuteLocalReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Result);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Result);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityInvokeLocalRequest()
        {
            ActivityInvokeLocalRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityInvokeLocalRequest();

                Assert.Equal(InternalMessageTypes.ActivityInvokeLocalReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityInvokeLocalRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Args);
                Assert.Equal(0, message.ActivityTypeId);

                // Round-trip

                message.RequestId = 555;
                message.ActivityContextId = 666;
                message.ActivityTypeId = 777;
                message.Args = new byte[] { 0, 1, 2, 3, 4 };

                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityContextId);
                Assert.Equal(777, message.ActivityTypeId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityInvokeLocalRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityContextId);
                Assert.Equal(777, message.ActivityTypeId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);

                // Verify Clone()

                message = (ActivityInvokeLocalRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityContextId);
                Assert.Equal(777, message.ActivityTypeId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityContextId);
                Assert.Equal(777, message.ActivityTypeId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityInvokeLocalReply()
        {
            ActivityInvokeLocalReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityInvokeLocalReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityInvokeLocalReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Error);
                Assert.Null(message.Result);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");
                message.Result = new byte[] { 5, 6, 7, 8, 9 };

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityInvokeLocalReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);

                // Verify Clone()

                message = (ActivityInvokeLocalReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityGetInfoRequest()
        {
            ActivityGetInfoRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityGetInfoRequest();

                Assert.Equal(InternalMessageTypes.ActivityGetInfoReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetInfoRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;

                Assert.Equal(555, message.RequestId);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetInfoRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);

                // Verify Clone()

                message = (ActivityGetInfoRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
            }
        }

        private void AssertEqual(ActivityInfo expected, ActivityInfo actual)
        {
            Assert.Equal(expected.ActivityId , actual.ActivityId);
            Assert.Equal(expected.Attempt , actual.Attempt);
            Assert.Equal(expected.ActivityType.Name , actual.ActivityType.Name);
            Assert.Equal(expected.Deadline , actual.Deadline);
            Assert.Equal(expected.HeartbeatTimeout , actual.HeartbeatTimeout);
            Assert.Equal(expected.ScheduledTime, actual.ScheduledTime);
            Assert.Equal(expected.StartedTime , actual.StartedTime);
            Assert.Equal(expected.TaskQueue , actual.TaskQueue);
            Assert.Equal(expected.TaskToken , actual.TaskToken);
            Assert.Equal(expected.WorkflowNamespace , actual.WorkflowNamespace);
            Assert.Equal(expected.WorkflowExecution.WorkflowId , actual.WorkflowExecution.WorkflowId);
            Assert.Equal(expected.WorkflowExecution.RunId , actual.WorkflowExecution.RunId);
            Assert.Equal(expected.WorkflowType.Name , actual.WorkflowType.Name);
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityGetInfoReply()
        {
            ActivityGetInfoReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityGetInfoReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetInfoReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Error);
                Assert.Null(message.Info);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                var expected = new ActivityInfo()
                {
                    ActivityId = "666",
                    Attempt = 4,
                    ActivityType      = new ActivityType { Name = "my-activity"},
                    Deadline          = new DateTime(2020, 5, 24, 1, 48, 0),
                    HeartbeatTimeout  = TimeSpan.FromSeconds(5),
                    ScheduledTime     = new DateTime(2020, 5, 24, 1, 49, 0),
                    StartedTime       = new DateTime(2020, 5, 24, 1, 50, 0),
                    TaskQueue          = "my-taskqueue",
                    TaskToken         = Convert.ToBase64String(new byte[] { 0, 1, 2, 3, 4 }),
                    WorkflowNamespace = "my-namespace",
                    WorkflowExecution = new WorkflowExecution("777", "888"),
                    WorkflowType      = new WorkflowType { Name = "my-workflow" }
                };

                message.Info = expected;
                AssertEqual(expected, message.Info);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetInfoReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                AssertEqual(expected, message.Info);

                // Verify Clone()

                message = (ActivityGetInfoReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                AssertEqual(expected, message.Info);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                AssertEqual(expected, message.Info);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityCompleteRequest()
        {
            ActivityCompleteRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityCompleteRequest();

                Assert.Equal(InternalMessageTypes.ActivityCompleteReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityCompleteRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.TaskToken);
                Assert.Null(message.RunId);
                Assert.Null(message.Namespace);
                Assert.Null(message.ActivityId);
                Assert.Null(message.Result);
                Assert.Null(message.Error);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.TaskToken = new byte[] { 0, 1, 2, 3, 4 };
                message.Namespace = "my-namespace";
                message.RunId = "my-run-id";
                message.ActivityId = "my-activity-id";
                message.Error = new TemporalError(new EntityNotExistsException("my-error"));
                message.Result = new byte[] { 5, 6, 7, 8, 9 };

                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.TaskToken);
                Assert.Equal("my-run-id", message.RunId);
                Assert.Equal("my-namespace", message.Namespace);
                Assert.Equal("my-activity-id", message.ActivityId);
                Assert.Equal("Neon.Temporal.EntityNotExistsException{my-error}", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityCompleteRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.TaskToken);
                Assert.Equal("my-run-id", message.RunId);
                Assert.Equal("my-namespace", message.Namespace);
                Assert.Equal("my-activity-id", message.ActivityId);
                Assert.Equal("Neon.Temporal.EntityNotExistsException{my-error}", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);

                // Verify Clone()

                message = (ActivityCompleteRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.TaskToken);
                Assert.Equal("my-run-id", message.RunId);
                Assert.Equal("my-namespace", message.Namespace);
                Assert.Equal("my-activity-id", message.ActivityId);
                Assert.Equal("Neon.Temporal.EntityNotExistsException{my-error}", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.TaskToken);
                Assert.Equal("my-run-id", message.RunId);
                Assert.Equal("my-namespace", message.Namespace);
                Assert.Equal("my-activity-id", message.ActivityId);
                Assert.Equal("Neon.Temporal.EntityNotExistsException{my-error}", message.Error.ErrorJson);
                Assert.Equal(new byte[] { 5, 6, 7, 8, 9 }, message.Result);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityCompleteReply()
        {
            ActivityCompleteReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityCompleteReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityCompleteReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Error);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityCompleteReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                // Verify Clone()

                message = (ActivityCompleteReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityStartRequest()
        {
            ActivityStartRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityStartRequest();

                Assert.Equal(InternalMessageTypes.ActivityStartReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityStartRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Equal(0, message.ActivityId);
                Assert.Null(message.Activity);
                Assert.Null(message.Args);
                Assert.Null(message.Options);

                // Round-trip

                message.ClientId   = 444;
                message.RequestId  = 555;
                message.ActivityId = 666;
                message.Activity   = "my-activity";
                message.Args       = new byte[] { 0, 1, 2, 3, 4 };
                message.Options    = new ActivityOptions()
                {
                    TaskQueue              = "my-taskqueue",
                    ScheduleToCloseTimeout = TimeSpan.FromSeconds(1),
                    ScheduleToStartTimeout = TimeSpan.FromSeconds(2),
                    StartToCloseTimeout    = TimeSpan.FromSeconds(3),
                    HeartbeatTimeout       = TimeSpan.FromSeconds(4),
                    WaitForCancellation    = true,
                    RetryPolicy           = new RetryPolicy() { MaximumInterval = TimeSpan.FromSeconds(5) }
                };

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);
                Assert.Equal("my-activity", message.Activity);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal("my-taskqueue", message.Options.TaskQueue);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(2), message.Options.ScheduleToStartTimeout);
                Assert.Equal(TimeSpan.FromSeconds(3), message.Options.StartToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(4), message.Options.HeartbeatTimeout);
                Assert.True(message.Options.WaitForCancellation);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityStartRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);
                Assert.Equal("my-activity", message.Activity);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal("my-taskqueue", message.Options.TaskQueue);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(2), message.Options.ScheduleToStartTimeout);
                Assert.Equal(TimeSpan.FromSeconds(3), message.Options.StartToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(4), message.Options.HeartbeatTimeout);
                Assert.True(message.Options.WaitForCancellation);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);

                // Verify Clone()

                message = (ActivityStartRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);
                Assert.Equal("my-activity", message.Activity);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal("my-taskqueue", message.Options.TaskQueue);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(2), message.Options.ScheduleToStartTimeout);
                Assert.Equal(TimeSpan.FromSeconds(3), message.Options.StartToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(4), message.Options.HeartbeatTimeout);
                Assert.True(message.Options.WaitForCancellation);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);
                Assert.Equal("my-activity", message.Activity);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal("my-taskqueue", message.Options.TaskQueue);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(2), message.Options.ScheduleToStartTimeout);
                Assert.Equal(TimeSpan.FromSeconds(3), message.Options.StartToCloseTimeout);
                Assert.Equal(TimeSpan.FromSeconds(4), message.Options.HeartbeatTimeout);
                Assert.True(message.Options.WaitForCancellation);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityStartReply()
        {
            ActivityStartReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityStartReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityStartReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Error);
                Assert.Equal(InternalReplayStatus.Unspecified, message.ReplayStatus);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");
                message.ReplayStatus = InternalReplayStatus.Replaying;

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityStartReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);

                // Verify Clone()

                message = (ActivityStartReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityGetResultRequest()
        {
            ActivityGetResultRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityGetResultRequest();

                Assert.Equal(InternalMessageTypes.ActivityGetResultReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetResultRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Equal(0, message.ActivityId);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.ActivityId = 666;

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetResultRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);

                // Verify Clone()

                message = (ActivityGetResultRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityGetResultReply()
        {
            ActivityGetResultReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityGetResultReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetResultReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Result);
                Assert.Null(message.Error);
                Assert.Equal(InternalReplayStatus.Unspecified, message.ReplayStatus);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Result = new byte[] { 0, 1, 2, 3, 4 };
                message.Error = new TemporalError("MyError");
                message.ReplayStatus = InternalReplayStatus.Replaying;

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Result);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetResultReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Result);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);

                // Verify Clone()

                message = (ActivityGetResultReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Result);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Result);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityStartLocalRequest()
        {
            ActivityStartLocalRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityStartLocalRequest();

                Assert.Equal(InternalMessageTypes.ActivityStartLocalReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityStartLocalRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Equal(0, message.ActivityId);
                Assert.Equal(0, message.ActivityTypeId);
                Assert.Null(message.Args);
                Assert.Null(message.Options);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.ActivityId = 666;
                message.ActivityTypeId = 777;
                message.Args = new byte[] { 0, 1, 2, 3, 4 };
                message.Options = new LocalActivityOptions()
                {
                    ScheduleToCloseTimeout = TimeSpan.FromSeconds(1),
                    RetryPolicy = new RetryPolicy() { MaximumInterval = TimeSpan.FromSeconds(5) }
                };

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);
                Assert.Equal(777, message.ActivityTypeId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityStartLocalRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);
                Assert.Equal(777, message.ActivityTypeId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);

                // Verify Clone()

                message = (ActivityStartLocalRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);
                Assert.Equal(777, message.ActivityTypeId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);
                Assert.Equal(777, message.ActivityTypeId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Args);
                Assert.NotNull(message.Options);
                Assert.Equal(TimeSpan.FromSeconds(1), message.Options.ScheduleToCloseTimeout);
                Assert.NotNull(message.Options.RetryPolicy);
                Assert.Equal(TimeSpan.FromSeconds(5), message.Options.RetryPolicy.MaximumInterval);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityGetLocalResultReply()
        {
            ActivityGetLocalResultReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityGetLocalResultReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetLocalResultReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Result);
                Assert.Null(message.Error);
                Assert.Equal(InternalReplayStatus.Unspecified, message.ReplayStatus);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Result = new byte[] { 0, 1, 2, 3, 4 };
                message.Error = new TemporalError("MyError");
                message.ReplayStatus = InternalReplayStatus.Replaying;

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Result);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetLocalResultReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Result);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);

                // Verify Clone()

                message = (ActivityGetLocalResultReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Result);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, message.Result);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityGetLocalResultRequest()
        {
            ActivityGetLocalResultRequest message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityGetLocalResultRequest();

                Assert.Equal(InternalMessageTypes.ActivityGetLocalResultReply, message.ReplyType);

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetLocalResultRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Equal(0, message.ActivityId);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.ActivityId = 666;

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityGetLocalResultRequest>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);

                // Verify Clone()

                message = (ActivityGetLocalResultRequest)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal(666, message.ActivityId);
            }
        }

        [Fact]
        [Trait(TestTraits.Project, TestProject.NeonTemporal)]
        public void Test_ActivityStartLocalReply()
        {
            ActivityStartLocalReply message;

            using (var stream = new MemoryStream())
            {
                message = new ActivityStartLocalReply();

                // Empty message.

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityStartLocalReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(0, message.ClientId);
                Assert.Equal(0, message.RequestId);
                Assert.Null(message.Error);
                Assert.Equal(InternalReplayStatus.Unspecified, message.ReplayStatus);

                // Round-trip

                message.ClientId = 444;
                message.RequestId = 555;
                message.Error = new TemporalError("MyError");
                message.ReplayStatus = InternalReplayStatus.Replaying;

                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);

                stream.SetLength(0);
                stream.Write(message.SerializeAsBytes());
                stream.Seek(0, SeekOrigin.Begin);

                message = ProxyMessage.Deserialize<ActivityStartLocalReply>(stream);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);

                // Verify Clone()

                message = (ActivityStartLocalReply)message.Clone();
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);

                // Echo the message via the associated [temporal-proxy] and verify.

                message = EchoToProxy(message);
                Assert.NotNull(message);
                Assert.Equal(444, message.ClientId);
                Assert.Equal(555, message.RequestId);
                Assert.Equal("MyError", message.Error.ErrorJson);
                Assert.Equal(InternalReplayStatus.Replaying, message.ReplayStatus);
            }
        }
    }
}
