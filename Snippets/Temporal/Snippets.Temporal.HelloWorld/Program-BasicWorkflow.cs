﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Temporal;

namespace HelloWorld
{
    [WorkflowInterface(TaskQueue = "my-tasks")]
    public interface IHelloWorkflow : IWorkflow
    {
        [WorkflowMethod]
        Task<string> HelloAsync(string name);
    }

    public class HelloWorkflow : WorkflowBase, IHelloWorkflow
    {
        public async Task<string> HelloAsync(string name)
        {
            return await Task.FromResult($"Hello {name}!");
        }
    }

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            // Connect to Temporal

            var settings = new TemporalSettings()
            {
                Namespace       = "my-namespace",
                CreateNamespace = true,
                HostPort        = "localhost:7933"
            };

            using (var client = await TemporalClient.ConnectAsync(settings))
            {
                // Create a worker and register the workflow and activity 
                // implementations to let Temporal know we're open for business.

                var worker = await client.NewWorkerAsync();

                await worker.RegisterWorkflowAsync<HelloWorkflow>();
                await worker.StartAsync();

                // Invoke your workflow.

                var stub = client.NewWorkflowStub<IHelloWorkflow>();

                Console.WriteLine(await stub.HelloAsync("Jeff"));
            }
        }
    }
}
