﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Temporal;

namespace Snippets_SignalWorkflow
{
    #region code
    [WorkflowInterface(TaskQueue = "my-tasks")]
    public interface IMyWorkflow : IWorkflow
    {
        [WorkflowMethod]
        Task DoItAsync();

        [SignalMethod("signal")]
        Task SignalAsync(string message);

        [QueryMethod("get-status")]
        Task<string> GetStatusAsync();
    }

    [Workflow]
    public class MyWorkflow : WorkflowBase, IMyWorkflow
    {
        private string                  state = "started";
        private WorkflowQueue<string>   signalQueue;

        public async Task DoItAsync()
        {
            signalQueue = await Workflow.NewQueueAsync<string>();

            while (true)
            {
                state = await signalQueue.DequeueAsync();

                if (state == "done")
                {
                    break;
                }
            }
        }

        public async Task SignalAsync(string message)
        {
            await signalQueue.EnqueueAsync(message);
        }

        public async Task<string> GetStatusAsync()
        {
            return await Task.FromResult(state);
        }
    }

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var settings = new TemporalSettings()
            {
                Namespace       = "my-namespace",
                TaskQueue       = "my-tasks",
                CreateNamespace = true,
                HostPort        = "localhost:7933"
            };

            using (var client = await TemporalClient.ConnectAsync(settings))
            {
                // Create a worker and register the workflow and activity 
                // implementations to let Temporal know we're open for business.

                var worker = await client.NewWorkerAsync();

                await worker.RegisterAssemblyAsync(Assembly.GetExecutingAssembly());
                await worker.StartAsync();

                // Invoke the workflow, send it some signals and very that
                // it changed its state to the signal value.

                var stub = client.NewWorkflowStub<IMyWorkflow>();
                var task = stub.DoItAsync();

                await stub.SignalAsync("signal #1");
                Console.WriteLine(await stub.GetStatusAsync());

                await stub.SignalAsync("signal #2");
                Console.WriteLine(await stub.GetStatusAsync());

                // This signal completes the workflow.

                await stub.SignalAsync("done");
                await task;
            }
        }
    }
    #endregion
}
