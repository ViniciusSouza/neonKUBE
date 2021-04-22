﻿//-----------------------------------------------------------------------------
// FILE:	    CadenceClient.Workflow.cs
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
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Neon.Cadence;
using Neon.Cadence.Internal;
using Neon.Common;
using Neon.Retry;
using Neon.Tasks;
using Neon.Time;

namespace Neon.Cadence
{
    public partial class CadenceClient
    {
        //---------------------------------------------------------------------
        // Public Cadence workflow related operations.

        /// <summary>
        /// Registers a workflow implementation with Cadence.
        /// </summary>
        /// <typeparam name="TWorkflow">The <see cref="WorkflowBase"/> derived class implementing the workflow.</typeparam>
        /// <param name="workflowTypeName">
        /// Optionally specifies a custom workflow type name that will be used 
        /// for identifying the workflow implementation in Cadence.  This defaults
        /// to the fully qualified <typeparamref name="TWorkflow"/> type name.
        /// </param>
        /// <param name="domain">Optionally overrides the default client domain.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown if another workflow class has already been registered for <paramref name="workflowTypeName"/>.</exception>
        /// <exception cref="WorkflowWorkerStartedException">
        /// Thrown if a workflow worker has already been started for the client.  You must
        /// register workflow implementations before starting workers.
        /// </exception>
        /// <remarks>
        /// <note>
        /// Be sure to register all of your workflow implementations before starting workers.
        /// </note>
        /// </remarks>
        public async Task RegisterWorkflowAsync<TWorkflow>(string workflowTypeName = null, string domain = null)
            where TWorkflow : WorkflowBase
        {
            await SyncContext.ClearAsync;
            CadenceHelper.ValidateWorkflowImplementation(typeof(TWorkflow));
            CadenceHelper.ValidateWorkflowTypeName(workflowTypeName);
            EnsureNotDisposed();

            if (workflowWorkerStarted)
            {
                throw new WorkflowWorkerStartedException();
            }

            var workflowType = typeof(TWorkflow);

            if (string.IsNullOrEmpty(workflowTypeName))
            {
                workflowTypeName = CadenceHelper.GetWorkflowTypeName(workflowType, workflowType.GetCustomAttribute<WorkflowAttribute>());
            }

            await WorkflowBase.RegisterAsync(this, workflowType, workflowTypeName, ResolveDomain(domain));

            lock (registeredWorkflowTypes)
            {
                registeredWorkflowTypes.Add(CadenceHelper.GetWorkflowInterface(typeof(TWorkflow)));
            }
        }

        /// <summary>
        /// Scans the assembly passed looking for workflow implementations derived from
        /// <see cref="WorkflowBase"/> and tagged by <see cref="WorkflowAttribute"/> with
        /// <see cref="WorkflowAttribute.AutoRegister"/> set to <c>true</c> and registers 
        /// them with Cadence.
        /// </summary>
        /// <param name="assembly">The target assembly.</param>
        /// <param name="domain">Optionally overrides the default client domain.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="TypeLoadException">
        /// Thrown for types tagged by <see cref="WorkflowAttribute"/> that are not 
        /// derived from <see cref="WorkflowBase"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">Thrown if one of the tagged classes conflict with an existing registration.</exception>
        /// <exception cref="WorkflowWorkerStartedException">
        /// Thrown if a workflow worker has already been started for the client.  You must
        /// register workflow implementations before starting workers.
        /// </exception>
        /// <remarks>
        /// <note>
        /// Be sure to register all of your workflow implementations before starting workers.
        /// </note>
        /// </remarks>
        public async Task RegisterAssemblyWorkflowsAsync(Assembly assembly, string domain = null)
        {
            await SyncContext.ClearAsync;
            Covenant.Requires<ArgumentNullException>(assembly != null, nameof(assembly));
            EnsureNotDisposed();

            foreach (var type in assembly.GetTypes().Where(type => type.IsClass))
            {
                var workflowAttribute = type.GetCustomAttribute<WorkflowAttribute>();

                if (workflowAttribute != null && workflowAttribute.AutoRegister)
                {
                    var workflowTypeName = CadenceHelper.GetWorkflowTypeName(type, workflowAttribute);

                    await WorkflowBase.RegisterAsync(this, type, workflowTypeName, ResolveDomain(domain));

                    lock (registeredWorkflowTypes)
                    {
                        registeredWorkflowTypes.Add(CadenceHelper.GetWorkflowInterface(type));
                    }
                }
            }
        }

        /// <summary>
        /// <para>
        /// Sets the maximum number of sticky workflows for which of history will be 
        /// retained for workflow workers created by this client as a performance 
        /// optimization.  When this is exceeded, Cadence will may need to retrieve 
        /// the entire workflow history from the Cadence cluster when a workflow is 
        /// scheduled on the client's workers.
        /// </para>
        /// <para>
        /// This defaults to <b>10K</b> sticky workflows.
        /// </para>
        /// </summary>
        /// <param name="cacheMaximumSize">The maximum number of workflows to be cached.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public async Task SetCacheMaximumSizeAsync(int cacheMaximumSize)
        {
            await SyncContext.ClearAsync;
            Covenant.Requires<ArgumentNullException>(cacheMaximumSize >= 0, nameof(cacheMaximumSize));
            EnsureNotDisposed();

            var reply = (WorkflowSetCacheSizeReply)await CallProxyAsync(
                new WorkflowSetCacheSizeRequest()
                {
                    Size = cacheMaximumSize
                });

            reply.ThrowOnError();

            workflowCacheSize = cacheMaximumSize;
        }

        /// <summary>
        /// Returns the current maximum number of sticky workflows for which history
        /// will be retained as a performance optimization.
        /// </summary>
        /// <returns>The maximum number of cached workflows.</returns>
        public async Task<int> GetWorkflowCacheSizeAsync()
        {
            await SyncContext.ClearAsync;
            EnsureNotDisposed();

            return await Task.FromResult(workflowCacheSize);
        }

        /// <summary>
        /// Creates an untyped stub that can be used to start a single workflow execution.
        /// </summary>
        /// <param name="workflowTypeName">Specifies the workflow type name.</param>
        /// <param name="options">Specifies the workflow options (including the <see cref="WorkflowOptions.TaskList"/>).</param>
        /// <returns>The <see cref="WorkflowStub"/>.</returns>
        /// <remarks>
        /// <para>
        /// Unlike activity stubs, a workflow stub may only be used to launch a single
        /// workflow.  You'll need to create a new stub for each workflow you wish to
        /// invoke and then the first method called on a workflow stub must be
        /// the one of the methods tagged by <see cref="WorkflowMethodAttribute"/>.
        /// </para>
        /// <note>
        /// <para>
        /// .NET and Java workflows can implement multiple workflow methods using attributes
        /// and annotations to assign unique names to each.  Each workflow method is actually
        /// registered with Cadence as a distinct workflow type.  Workflow methods with a blank
        /// or <c>null</c> name will simply be registered using the workflow type name.
        /// </para>
        /// <para>
        /// Workflow methods with a name will be registered using a combination of the workflow
        /// type name and the method name, using <b>"::"</b> as the separator, like:
        /// </para>
        /// <code>
        /// WORKFLOW-TYPENAME::METHOD-NAME
        /// </code>
        /// <para>
        /// GOLANG doesn't support the concept of workflow methods.  GOLANG workflows 
        /// are just given a simple name which you'll pass here as <paramref name="workflowTypeName"/>
        /// to make cross platform calls.
        /// </para>
        /// </note>
        /// </remarks>
        public WorkflowStub NewUntypedWorkflowStub(string workflowTypeName, WorkflowOptions options)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(workflowTypeName), nameof(workflowTypeName));
            Covenant.Requires<ArgumentNullException>(options != null, nameof(options));
            EnsureNotDisposed();

            options = WorkflowOptions.Normalize(this, options);

            if (string.IsNullOrEmpty(options.TaskList))
            {
                throw new ArgumentNullException($"The workflow [{nameof(WorkflowOptions)}.{nameof(WorkflowOptions.TaskList)}] must be specified when the client doesn't set [{nameof(CadenceSettings)}.{nameof(CadenceSettings.DefaultTaskList)}].");
            }

            if (string.IsNullOrEmpty(options.Domain))
            {
                throw new ArgumentNullException($"The workflow [{nameof(WorkflowOptions)}.{nameof(WorkflowOptions.Domain)}] must be specified when the client doesn't set [{nameof(CadenceSettings)}.{nameof(CadenceSettings.DefaultDomain)}].");
            }

            return new WorkflowStub(this)
            {
                WorkflowTypeName = workflowTypeName,
                Options          = options
            };
        }

        /// <summary>
        /// Creates an untyped stub for a known workflow execution.
        /// </summary>
        /// <param name="workflowId">The workflow ID.</param>
        /// <param name="runId">Optionally specifies the workflow run ID.</param>
        /// <param name="domain">
        /// Optionally specifies the domain where the target workflow is running.
        /// This will be required when default domain for the client isn't specified
        /// or when the the target execution is running in a different domain.
        /// </param>
        /// <returns>The <see cref="WorkflowStub"/>.</returns>
        /// <remarks>
        /// Unlike activity stubs, a workflow stub may only be used to launch a single
        /// workflow.  You'll need to create a new stub for each workflow you wish to
        /// invoke and then the first method called on a workflow stub must be
        /// the one of the methods tagged by <see cref="WorkflowMethodAttribute"/>.
        /// </remarks>
        public WorkflowStub NewUntypedWorkflowStub(string workflowId, string runId = null, string domain = null)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(workflowId), nameof(workflowId));
            EnsureNotDisposed();

            domain = ResolveDomain(domain);

            if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException($"The [{nameof(domain)} parameter must be specified when the client doesn't set [{nameof(CadenceSettings)}.{nameof(CadenceSettings.DefaultDomain)}].");
            }

            return new WorkflowStub(this)
            {
                Execution = new WorkflowExecution(workflowId, runId),
                Options   = new WorkflowOptions() { Domain = domain }
            };
        }

        /// <summary>
        /// Creates an untyped stub for a known workflow execution.
        /// </summary>
        /// <param name="execution">The workflow execution.</param>
        /// <param name="domain">
        /// Optionally specifies the domain where the target workflow is running.
        /// This will be required when default domain for the client isn't specified
        /// or when the the target execution is running in a different domain.
        /// </param>
        /// <returns>The <see cref="WorkflowStub"/>.</returns>
        /// <remarks>
        /// Unlike activity stubs, a workflow stub may only be used to launch a single
        /// workflow.  You'll need to create a new stub for each workflow you wish to
        /// invoke and then the first method called on a workflow stub must be
        /// the one of the methods tagged by <see cref="WorkflowMethodAttribute"/>.
        /// </remarks>
        public WorkflowStub NewUntypedWorkflowStub(WorkflowExecution execution, string domain = null)
        {
            Covenant.Requires<ArgumentNullException>(execution != null, nameof(execution));
            EnsureNotDisposed();

            domain = ResolveDomain(domain);

            if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException($"The [{nameof(domain)} parameter must be specified when the client doesn't set [{nameof(CadenceSettings)}.{nameof(CadenceSettings.DefaultDomain)}].");
            }

            var options = new WorkflowOptions()
            {
                Domain = domain
            };

            return new WorkflowStub(this)
            {
                Execution = execution,
                Options   = options
            };
        }

        /// <summary>
        /// Creates a stub suitable for starting an external workflow and then waiting
        /// for the result as separate operations.
        /// </summary>
        /// <typeparam name="TWorkflowInterface">The target workflow interface.</typeparam>
        /// <param name="methodName">
        /// Optionally identifies the target workflow method.  This is the name specified in
        /// <c>[WorkflowMethod]</c> attribute for the workflow method or <c>null</c>/empty for
        /// the default workflow method.
        /// </param>
        /// <param name="options">Optionally specifies custom <see cref="WorkflowOptions"/>.</param>
        /// <returns>A <see cref="ChildWorkflowStub{TWorkflowInterface}"/> instance.</returns>
        public WorkflowFutureStub<TWorkflowInterface> NewWorkflowFutureStub<TWorkflowInterface>(string methodName = null, WorkflowOptions options = null)
            where TWorkflowInterface : class
        {
            CadenceHelper.ValidateWorkflowInterface(typeof(TWorkflowInterface));
            EnsureNotDisposed();

            options = WorkflowOptions.Normalize(this, options, typeof(TWorkflowInterface));

            return new WorkflowFutureStub<TWorkflowInterface>(this, methodName, options);
        }

        /// <summary>
        /// Creates a typed workflow stub connected to a known workflow execution
        /// using IDs.  This can be used to signal and query the workflow via the 
        /// type-safe interface methods.
        /// </summary>
        /// <typeparam name="TWorkflowInterface">Identifies the workflow interface.</typeparam>
        /// <param name="workflowId">Specifies the workflow ID.</param>
        /// <param name="runId">Optionally specifies the workflow's run ID.</param>
        /// <param name="domain">
        /// Optionally specifies the domain where the target workflow is running.
        /// This will be required when default domain for the client isn't specified
        /// or when the the target execution is running in a different domain.
        /// </param>
        /// <returns>The dynamically generated stub that implements the workflow methods defined by <typeparamref name="TWorkflowInterface"/>.</returns>
        /// <remarks>
        /// Unlike activity stubs, a workflow stub may only be used to launch a single
        /// workflow.  You'll need to create a new stub for each workflow you wish to
        /// invoke and then the first method called on a workflow stub must be
        /// the one of the methods tagged by <see cref="WorkflowMethodAttribute"/>.
        /// </remarks>
        public TWorkflowInterface NewWorkflowStub<TWorkflowInterface>(string workflowId, string runId = null, string domain = null)
            where TWorkflowInterface : class
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(workflowId), nameof(workflowId));
            CadenceHelper.ValidateWorkflowInterface(typeof(TWorkflowInterface));
            EnsureNotDisposed();

            domain = ResolveDomain(domain);

            if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException($"The [{nameof(domain)} parameter must be specified when the client doesn't set [{nameof(CadenceSettings)}.{nameof(CadenceSettings.DefaultDomain)}].");
            }

            return StubManager.NewWorkflowStub<TWorkflowInterface>(this, workflowId, runId, domain);
        }

        /// <summary>
        /// Creates a typed workflow stub connected to a known workflow execution
        /// using a <see cref="WorkflowExecution"/>.  This can be used to signal and
        /// query the workflow via the type-safe interface methods.
        /// </summary>
        /// <typeparam name="TWorkflowInterface">Identifies the workflow interface.</typeparam>
        /// <param name="execution">Specifies the <see cref="WorkflowExecution"/>.</param>
        /// <param name="domain">
        /// Optionally specifies the domain where the target workflow is running.
        /// This will be required when default domain for the client isn't specified
        /// or when the the target execution is running in a different domain.
        /// </param>
        /// <returns>The dynamically generated stub that implements the workflow methods defined by <typeparamref name="TWorkflowInterface"/>.</returns>
        /// <remarks>
        /// Unlike activity stubs, a workflow stub may only be used to launch a single
        /// workflow.  You'll need to create a new stub for each workflow you wish to
        /// invoke and then the first method called on a workflow stub must be
        /// the one of the methods tagged by <see cref="WorkflowMethodAttribute"/>.
        /// </remarks>
        public TWorkflowInterface NewWorkflowStub<TWorkflowInterface>(WorkflowExecution execution, string domain = null)
            where TWorkflowInterface : class
        {
            Covenant.Requires<ArgumentNullException>(execution != null, nameof(execution));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(execution.WorkflowId), nameof(execution.WorkflowId));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(execution.RunId), nameof(execution.RunId));
            CadenceHelper.ValidateWorkflowInterface(typeof(TWorkflowInterface));
            EnsureNotDisposed();

            domain = ResolveDomain(domain);

            if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException($"The [{nameof(domain)} parameter must be specified when the client doesn't set [{nameof(CadenceSettings)}.{nameof(CadenceSettings.DefaultDomain)}].");
            }

            return StubManager.NewWorkflowStub<TWorkflowInterface>(this, execution.WorkflowId, execution.RunId, domain);
        }

        /// <summary>
        /// Creates an untyped workflow stub to be used for launching a workflow.
        /// </summary>
        /// <param name="workflowTypeName">Specifies the workflow type name.</param>
        /// <param name="options">Optionally specifies the workflow options.</param>
        /// <returns>The new <see cref="WorkflowStub"/>.</returns>
        /// <remarks>
        /// <para>
        /// Unlike activity stubs, a workflow stub may only be used to launch a single
        /// workflow.  You'll need to create a new stub for each workflow you wish to
        /// invoke and then the first method called on a workflow stub must be
        /// the one of the methods tagged by <see cref="WorkflowMethodAttribute"/>.
        /// </para>
        /// <note>
        /// <para>
        /// .NET and Java workflows can implement multiple workflow methods using attributes
        /// and annotations to assign unique names to each.  Each workflow method is actually
        /// registered with Cadence as a distinct workflow type.  Workflow methods with a blank
        /// or <c>null</c> name will simply be registered using the workflow type name.
        /// </para>
        /// <para>
        /// Workflow methods with a name will be registered using a combination of the workflow
        /// type name and the method name, using <b>"::"</b> as the separator, like:
        /// </para>
        /// <code>
        /// WORKFLOW-TYPENAME::METHOD-NAME
        /// </code>
        /// <para>
        /// GOLANG doesn't support the concept of workflow methods.  GOLANG workflows 
        /// are just given a simple name which you'll pass here as <paramref name="workflowTypeName"/>
        /// to make cross platform calls.
        /// </para>
        /// </note>
        /// </remarks>
        public WorkflowStub NewWorkflowStub(string workflowTypeName, WorkflowOptions options = null)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(workflowTypeName), nameof(workflowTypeName));
            EnsureNotDisposed();

            options = WorkflowOptions.Normalize(this, options);

            if (string.IsNullOrEmpty(options.TaskList))
            {
                throw new ArgumentNullException($"The workflow [{nameof(WorkflowOptions)}.{nameof(WorkflowOptions.TaskList)}] must be specified when the client doesn't set [{nameof(CadenceSettings)}.{nameof(CadenceSettings.DefaultTaskList)}].");
            }

            if (string.IsNullOrEmpty(options.Domain))
            {
                throw new ArgumentNullException($"The workflow [{nameof(WorkflowOptions)}.{nameof(WorkflowOptions.Domain)}] must be specified when the client doesn't set [{nameof(CadenceSettings)}.{nameof(CadenceSettings.DefaultDomain)}].");
            }

            return new WorkflowStub(this)
            {
                WorkflowTypeName = workflowTypeName,
                Options          = options
            };
        }

        /// <summary>
        /// Creates a typed workflow stub that can be used to start as well as 
        /// query and signal the workflow via the type-safe interface methods.
        /// </summary>
        /// <typeparam name="TWorkflowInterface">Identifies the workflow interface.</typeparam>
        /// <param name="options">Optionally specifies the workflow options.</param>
        /// <param name="workflowTypeName">
        /// Optionally specifies the workflow type name by overriding the fully 
        /// qualified <typeparamref name="TWorkflowInterface"/> type name or the name
        /// specified by a <see cref="WorkflowAttribute"/>.
        /// </param>
        /// <returns>The dynamically generated stub that implements the workflow methods defined by <typeparamref name="TWorkflowInterface"/>.</returns>
        /// <remarks>
        /// <para>
        /// Unlike activity stubs, a workflow stub may only be used to launch a single
        /// workflow.  You'll need to create a new stub for each workflow you wish to
        /// invoke and then the first method called on a workflow stub must be
        /// the one of the methods tagged by <see cref="WorkflowMethodAttribute"/>.
        /// </para>
        /// <note>
        /// <para>
        /// .NET and Java workflows can implement multiple workflow methods using attributes
        /// and annotations to assign unique names to each.  Each workflow method is actually
        /// registered with Cadence as a distinct workflow type.  Workflow methods with a blank
        /// or <c>null</c> name will simply be registered using the workflow type name.
        /// </para>
        /// <para>
        /// Workflow methods with a name will be registered using a combination of the workflow
        /// type name and the method name, using <b>"::"</b> as the separator, like:
        /// </para>
        /// <code>
        /// WORKFLOW-TYPENAME::METHOD-NAME
        /// </code>
        /// <para>
        /// GOLANG doesn't support the concept of workflow methods.  GOLANG workflows 
        /// are just given a simple name which you'll pass here as <paramref name="workflowTypeName"/>
        /// to make cross platform calls.
        /// </para>
        /// </note>
        /// </remarks>
        public TWorkflowInterface NewWorkflowStub<TWorkflowInterface>(WorkflowOptions options = null, string workflowTypeName = null)
            where TWorkflowInterface : class
        {
            CadenceHelper.ValidateWorkflowInterface(typeof(TWorkflowInterface));
            EnsureNotDisposed();

            return StubManager.NewWorkflowStub<TWorkflowInterface>(this, options: options, workflowTypeName: workflowTypeName);
        }

        /// <summary>
        /// Describes a workflow execution by explicit IDs.
        /// </summary>
        /// <param name="workflowId">The workflow ID.</param>
        /// <param name="runid">Optionally specifies the run ID.</param>
        /// <param name="domain">Optionally specifies the domain.</param>
        /// <returns>The <see cref="WorkflowDescription"/></returns>
        /// <exception cref="EntityNotExistsException">Thrown if the workflow does not exist.</exception>
        public async Task<WorkflowDescription> DescribeWorkflowExecutionAsync(string workflowId, string runid = null, string domain = null)
        {
            await SyncContext.ClearAsync;
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(workflowId), nameof(workflowId));
            EnsureNotDisposed();

            domain = ResolveDomain(domain);

            var reply = (WorkflowDescribeExecutionReply)await CallProxyAsync(
                new WorkflowDescribeExecutionRequest()
                {
                    WorkflowId = workflowId,
                    RunId      = runid ?? string.Empty,
                    Domain     = domain
                });

            reply.ThrowOnError();

            return reply.Details.ToPublic();
        }

        /// <summary>
        /// Waits for a resonable period of time for Cadence to start a workflow.
        /// </summary>
        /// <param name="execution">Identifies the target workflow.</param>
        /// <param name="domain">Optional domain.</param>
        /// <param name="maxWait">
        /// Optionally overrides <see cref="CadenceSettings.MaxWorkflowWaitUntilRunningSeconds"/> to
        /// specify a custom maximum wait time.  The default setting is <b>30 seconds</b>.
        /// </param>
        /// <exception cref="EntityNotExistsException">Thrown if the target workflow does not exist.</exception>
        /// <exception cref="SyncSignalException">Thrown if the workflow is closed or the signal could not be executed for another reason.</exception>
        /// <exception cref="CadenceTimeoutException">Thrown when the workflow did not start running within a reasonable period of time.</exception>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <remarks>
        /// This method can be handy when writing non-emulated unit tests.
        /// </remarks>
        public async Task WaitForWorkflowStartAsync(WorkflowExecution execution, string domain = null, TimeSpan? maxWait = null)
        {
            Covenant.Requires<ArgumentNullException>(execution != null, nameof(execution));

            maxWait = maxWait ?? Settings.MaxWorkflowWaitUntilRunning;

            var transientExceptions = new Type[]
            {
                typeof(QueryFailedException),
                typeof(CadenceTimeoutException)
            };

            var retry = new ExponentialRetryPolicy(
                exceptionTypes:         transientExceptions,
                maxAttempts:            int.MaxValue,
                initialRetryInterval:   TimeSpan.FromSeconds(0.25),
                maxRetryInterval:       TimeSpan.FromSeconds(2),
                timeout:                maxWait);

            string timeoutMessage = null;

            await retry.InvokeAsync(
                async () =>
                {
                    var description = await DescribeWorkflowExecutionAsync(execution.WorkflowId, execution.RunId, domain);

                    if (description.Status.IsRunning)
                    {
                        return;
                    }
                    else if (description.Status.IsClosed)
                    {
                        throw new SyncSignalException($"{typeof(SyncSignalException).FullName}: Wait for workflow [workflowID={execution.WorkflowId}, runID={execution.RunId}] failed because the worflow is closed.");
                    }
                    else
                    {
                        // Avoid generating the same message string over again for each retry.

                        if (timeoutMessage == null)
                        {
                            timeoutMessage = $"{typeof(CadenceTimeoutException).FullName}:Wait for workflow [workflowID={execution.WorkflowId}, runID={execution.RunId}] failed to start within [{retry.Timeout}].";
                        }

                        throw new CadenceTimeoutException(timeoutMessage);
                    }
                });
        }

        //---------------------------------------------------------------------
        // Internal workflow related methods used by dynamically generated workflow stubs.

        /// <summary>
        /// Raised when an external workflow is executed.  This is used internally
        /// for unit tests that verify that workflow options are configured correctly. 
        /// </summary>
        internal event EventHandler<WorkflowOptions> WorkflowExecuteEvent;

        /// <summary>
        /// Raised when a child workflow is executed.  This is used internally
        /// for unit tests that verify that workflow options are configured correctly. 
        /// </summary>
        internal event EventHandler<ChildWorkflowOptions> ChildWorkflowExecuteEvent;

        /// <summary>
        /// Raises the <see cref="WorkflowExecuteEvent"/>.
        /// </summary>
        /// <param name="options">The workflow options.</param>
        internal void RaiseWorkflowExecuteEvent(WorkflowOptions options)
        {
            WorkflowExecuteEvent?.Invoke(this, options);
        }

        /// <summary>
        /// Raises the <see cref="ChildWorkflowExecuteEvent"/>.
        /// </summary>
        /// <param name="options">The workflow options.</param>
        internal void RaiseChildWorkflowExecuteEvent(ChildWorkflowOptions options)
        {
            ChildWorkflowExecuteEvent?.Invoke(this, options);
        }

        /// <summary>
        /// Starts an external workflow using a specific workflow type name, returning a <see cref="WorkflowExecution"/>
        /// that can be used to track the workflow and also wait for its result via <see cref="GetWorkflowResultAsync(WorkflowExecution, string)"/>.
        /// </summary>
        /// <param name="workflowTypeName">
        /// The type name used when registering the workers that will handle this workflow.
        /// This name will often be the fully qualified name of the workflow type but 
        /// this may have been customized when the workflow worker was registered.
        /// </param>
        /// <param name="args">Specifies the workflow arguments encoded into a byte array.</param>
        /// <param name="options">Specifies the workflow options.</param>
        /// <returns>A <see cref="WorkflowExecution"/> identifying the new running workflow instance.</returns>
        /// <exception cref="EntityNotExistsException">Thrown if there is no workflow registered for <paramref name="workflowTypeName"/>.</exception>
        /// <exception cref="BadRequestException">Thrown if the request is not valid.</exception>
        /// <exception cref="WorkflowRunningException">Thrown if a workflow with this ID is already running.</exception>
        /// <remarks>
        /// This method kicks off a new workflow instance and returns after Cadence has
        /// queued the operation but the method <b>does not</b> wait for the workflow to
        /// complete.
        /// </remarks>
        internal async Task<WorkflowExecution> StartWorkflowAsync(string workflowTypeName, byte[] args, WorkflowOptions options)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(workflowTypeName), nameof(workflowTypeName));
            EnsureNotDisposed();

            options = WorkflowOptions.Normalize(this, options);

            RaiseWorkflowExecuteEvent(options);

            var reply = (WorkflowExecuteReply)await CallProxyAsync(
                new WorkflowExecuteRequest()
                {
                    Workflow = workflowTypeName,
                    Domain   = options.Domain,
                    Args     = args,
                    Options  = options.ToInternal()
                });

            reply.ThrowOnError();

            var execution = reply.Execution;

            return new WorkflowExecution(execution.ID, execution.RunID);
        }

        /// <summary>
        /// Returns the result from a workflow execution, blocking until the workflow
        /// completes if it is still running.
        /// </summary>
        /// <param name="execution">Identifies the workflow execution.</param>
        /// <param name="domain">Optionally specifies the domain.  This defaults to the client domain.</param>
        /// <returns>The workflow result encoded as bytes or <c>null</c>.</returns>
        /// <exception cref="EntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="BadRequestException">Thrown if the request is invalid.</exception>
        /// <exception cref="InternalServiceException">Thrown for internal Cadence problems.</exception>
        internal async Task<byte[]> GetWorkflowResultAsync(WorkflowExecution execution, string domain = null)
        {
            Covenant.Requires<ArgumentNullException>(execution != null, nameof(execution));
            EnsureNotDisposed();

            domain = ResolveDomain(domain);

            if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentNullException($"The [{nameof(domain)}] parameter must be specified when the client doesn't set [{nameof(CadenceSettings)}.{nameof(CadenceSettings.DefaultDomain)}].");
            }

            var reply = (WorkflowGetResultReply)await CallProxyAsync(
                new WorkflowGetResultRequest()
                {
                    WorkflowId = execution.WorkflowId,
                    RunId      = execution.RunId,
                    Domain     = domain
                });

            reply.ThrowOnError();

            return reply.Result;
        }

        /// <summary>
        /// Starts a child workflow.
        /// </summary>
        /// <param name="parentWorkflow">The parent workflow.</param>
        /// <param name="workflowTypeName">
        /// The type name used when registering the workers that will handle this workflow.
        /// This name will often be the fully qualified name of the workflow type but 
        /// this may have been customized when the workflow worker was registered.
        /// </param>
        /// <param name="args">Specifies the workflow arguments encoded into a byte array.</param>
        /// <param name="options">Specifies the workflow options.</param>
        /// <returns>A <see cref="ChildExecution"/> identifying the new running workflow instance.</returns>
        /// <exception cref="EntityNotExistsException">Thrown if there is no workflow registered for <paramref name="workflowTypeName"/>.</exception>
        /// <exception cref="BadRequestException">Thrown if the request is not valid.</exception>
        /// <exception cref="WorkflowRunningException">Thrown if a workflow with this ID is already running.</exception>
        /// <remarks>
        /// This method kicks off a new child workflow instance and returns after Cadence has
        /// queued the operation but the method <b>does not</b> wait for the workflow to
        /// complete.
        /// </remarks>
        internal async Task<ChildExecution> StartChildWorkflowAsync(Workflow parentWorkflow, string workflowTypeName, byte[] args, ChildWorkflowOptions options)
        {
            Covenant.Requires<ArgumentNullException>(parentWorkflow != null, nameof(parentWorkflow));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(workflowTypeName), nameof(workflowTypeName));
            EnsureNotDisposed();

            options = ChildWorkflowOptions.Normalize(this, options);
            
            RaiseChildWorkflowExecuteEvent(options);

            var reply = await parentWorkflow.ExecuteNonParallel(
                async () =>
                {
                    return (WorkflowExecuteChildReply)await CallProxyAsync(
                        new WorkflowExecuteChildRequest()
                        {
                            ContextId              = parentWorkflow.ContextId,
                            Workflow               = workflowTypeName,
                            Args                   = args,
                            Options                = options.ToInternal(),
                            ScheduleToStartTimeout = options.ScheduleToStartTimeout > TimeSpan.Zero ? options.ScheduleToStartTimeout : Settings.WorkflowScheduleToStartTimeout
                        });
                });

            reply.ThrowOnError();
            parentWorkflow.UpdateReplay(reply);

            return new ChildExecution(reply.Execution.ToPublic(), reply.ChildId);
        }

        /// <summary>
        /// Returns the result from a child workflow execution, blocking until the workflow
        /// completes if it is still running.
        /// </summary>
        /// <param name="parentWorkflow">The parent workflow.</param>
        /// <param name="childExecution">Identifies the child workflow execution.</param>
        /// <returns>The workflow result encoded as bytes or <c>null</c>.</returns>
        /// <exception cref="EntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="BadRequestException">Thrown if the request is invalid.</exception>
        /// <exception cref="InternalServiceException">Thrown for internal Cadence problems.</exception>
        internal async Task<byte[]> GetChildWorkflowResultAsync(Workflow parentWorkflow, ChildExecution childExecution)
        {
            Covenant.Requires<ArgumentNullException>(parentWorkflow != null, nameof(parentWorkflow));
            Covenant.Requires<ArgumentNullException>(childExecution != null, nameof(childExecution));
            EnsureNotDisposed();

            var reply = await parentWorkflow.ExecuteNonParallel(
                async () =>
                {
                    return (WorkflowWaitForChildReply)await CallProxyAsync(
                        new WorkflowWaitForChildRequest()
                        {
                            ContextId = parentWorkflow.ContextId,
                            ChildId   = childExecution.ChildId
                        });
                });

            reply.ThrowOnError();
            parentWorkflow.UpdateReplay(reply);

            return reply.Result;
        }

        /// <summary>
        /// Returns the current state of a running workflow.
        /// </summary>
        /// <param name="execution">Identifies the workflow execution.</param>
        /// <param name="domain">Optionally specifies the domain.  This defaults to the client domain.</param>
        /// <returns>A <see cref="WorkflowDescription"/>.</returns>
        /// <exception cref="EntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="BadRequestException">Thrown if the request is invalid.</exception>
        /// <exception cref="InternalServiceException">Thrown for internal Cadence problems.</exception>
        internal async Task<WorkflowDescription> DescribeWorkflowExecutionAsync(WorkflowExecution execution, string domain = null)
        {
            Covenant.Requires<ArgumentNullException>(execution != null, nameof(execution));
            EnsureNotDisposed();

            var reply = (WorkflowDescribeExecutionReply)await CallProxyAsync(
                new WorkflowDescribeExecutionRequest()
                {
                    WorkflowId = execution.WorkflowId,
                    RunId      = execution.RunId,
                    Domain     = ResolveDomain(domain)
                });

            reply.ThrowOnError();

            return reply.Details.ToPublic();
        }

        /// <summary>
        /// Cancels a workflow if it has not already finished.
        /// </summary>
        /// <param name="execution">Identifies the running workflow.</param>
        /// <param name="domain">Optionally identifies the domain.  This defaults to the client domain.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="EntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="BadRequestException">Thrown if the request is invalid.</exception>
        /// <exception cref="InternalServiceException">Thrown for internal Cadence problems.</exception>
        internal async Task CancelWorkflowAsync(WorkflowExecution execution, string domain = null)
        {
            Covenant.Requires<ArgumentNullException>(execution != null, nameof(execution));
            EnsureNotDisposed();

            var reply = (WorkflowCancelReply)await CallProxyAsync(
                new WorkflowCancelRequest()
                {
                    WorkflowId = execution.WorkflowId,
                    RunId      = execution.RunId,
                    Domain     = ResolveDomain(domain)
                });

            reply.ThrowOnError();
        }

        /// <summary>
        /// Terminates a workflow if it has not already finished.
        /// </summary>
        /// <param name="execution">Identifies the running workflow.</param>
        /// <param name="reason">Optionally specifies an error reason string.</param>
        /// <param name="details">Optionally specifies additional details as a byte array.</param>
        /// <param name="domain">Optionally specifies the domain.  This defaults to the client domain.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="EntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="BadRequestException">Thrown if the request is invalid.</exception>
        /// <exception cref="InternalServiceException">Thrown for internal Cadence problems.</exception>
        public async Task TerminateWorkflowAsync(WorkflowExecution execution, string reason = null, byte[] details = null, string domain = null)
        {
            Covenant.Requires<ArgumentNullException>(execution != null, nameof(execution));
            EnsureNotDisposed();

            var reply = (WorkflowTerminateReply)await CallProxyAsync(
                new WorkflowTerminateRequest()
                {
                    WorkflowId = execution.WorkflowId,
                    RunId      = execution.RunId,
                    Domain     = ResolveDomain(domain),
                    Reason     = reason,
                    Details    = details
                });

            reply.ThrowOnError();
        }

        /// <summary>
        /// Transmits a signal to a running external workflow.  This low-level method accepts a byte array
        /// with the already encoded parameters.
        /// </summary>
        /// <param name="execution">The <see cref="WorkflowExecution"/>.</param>
        /// <param name="signalName">Identifies the signal.</param>
        /// <param name="signalArgs">Optionally specifies the signal arguments as a byte array.</param>
        /// <param name="domain">Optionally specifies the domain.  This defaults to the client domain.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="EntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="InternalServiceException">Thrown for internal Cadence problems.</exception>
        internal async Task SignalWorkflowAsync(WorkflowExecution execution, string signalName, byte[] signalArgs = null, string domain = null)
        {
            Covenant.Requires<ArgumentNullException>(execution != null, nameof(execution));
            EnsureNotDisposed();

            var reply = (WorkflowSignalReply)await CallProxyAsync(
                new WorkflowSignalRequest()
                {
                    WorkflowId = execution.WorkflowId,
                    SignalName = signalName,
                    SignalArgs = signalArgs,
                    RunId      = execution.RunId,
                    Domain     = ResolveDomain(domain)
                });

            reply.ThrowOnError();
        }

        /// <summary>
        /// Transmits a signal to an external workflow, starting the workflow if it's not currently running.
        /// This low-level method accepts a byte array with the already encoded parameters.
        /// </summary>
        /// <param name="workflowTypeName">The target workflow type name.</param>
        /// <param name="signalName">Identifies the signal.</param>
        /// <param name="signalArgs">Optionally specifies the signal arguments as a byte array.</param>
        /// <param name="startArgs">Optionally specifies the workflow arguments.</param>
        /// <param name="options">Optionally specifies the options to be used for starting the workflow when required.</param>
        /// <returns>The <see cref="WorkflowExecution"/>.</returns>
        /// <exception cref="EntityNotExistsException">Thrown if the domain does not exist.</exception>
        /// <exception cref="BadRequestException">Thrown if the request is invalid.</exception>
        /// <exception cref="InternalServiceException">Thrown for internal Cadence problems.</exception>
        internal async Task<WorkflowExecution> SignalWorkflowWithStartAsync(string workflowTypeName, string signalName, byte[] signalArgs, byte[] startArgs, WorkflowOptions options)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(workflowTypeName), nameof(workflowTypeName));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(signalName), nameof(signalName));
            EnsureNotDisposed();

            options = WorkflowOptions.Normalize(this, options);

            var reply = (WorkflowSignalWithStartReply)await CallProxyAsync(
                new WorkflowSignalWithStartRequest()
                {
                    Workflow     = workflowTypeName,
                    WorkflowId   = options.WorkflowId,
                    Options      = options.ToInternal(),
                    SignalName   = signalName,
                    SignalArgs   = signalArgs,
                    WorkflowArgs = startArgs,
                    Domain       = options.Domain
                });

            reply.ThrowOnError();

            return reply.Execution.ToPublic();
        }

        /// <summary>
        /// Queries an external workflow.  This low-level method accepts a byte array
        /// with the already encoded parameters and returns an encoded result.
        /// </summary>
        /// <param name="execution">The <see cref="WorkflowExecution"/>.</param>
        /// <param name="queryType">Identifies the query.</param>
        /// <param name="queryArgs">Optionally specifies the query arguments encoded as a byte array.</param>
        /// <param name="domain">Optionally specifies the domain.  This defaults to the client domain.</param>
        /// <returns>The query result encoded as a byte array.</returns>
        /// <exception cref="EntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="InternalServiceException">Thrown for internal Cadence problems.</exception>
        internal async Task<byte[]> QueryWorkflowAsync(WorkflowExecution execution, string queryType, byte[] queryArgs = null, string domain = null)
        {
            Covenant.Requires<ArgumentNullException>(execution != null, nameof(execution));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(queryType), nameof(queryType));
            EnsureNotDisposed();

            var reply = (WorkflowQueryReply)await CallProxyAsync(
                new WorkflowQueryRequest()
                {
                    WorkflowId = execution.WorkflowId,
                    QueryName  = queryType,
                    QueryArgs  = queryArgs,
                    RunId      = execution.RunId,
                    Domain     = ResolveDomain(domain)
                });

            reply.ThrowOnError();

            return reply.Result;
        }

        /// <summary>
        /// <para>
        /// Signals a child workflow.  This low-level method accepts a byte array
        /// with the already encoded parameters.
        /// </para>
        /// <note>
        /// This method blocks until the child workflow is scheduled and
        /// actually started on a worker.
        /// </note>
        /// </summary>
        /// <param name="parentWorkflow">The parent workflow.</param>
        /// <param name="childExecution">The child workflow execution.</param>
        /// <param name="signalName">Specifies the signal name.</param>
        /// <param name="signalArgs">Specifies the signal arguments as an encoded byte array.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="EntityNotExistsException">Thrown if the named domain does not exist.</exception>
        /// <exception cref="BadRequestException">Thrown when the request is invalid.</exception>
        /// <exception cref="InternalServiceException">Thrown for internal Cadence cluster problems.</exception>
        /// <exception cref="ServiceBusyException">Thrown when Cadence is too busy.</exception>
        internal async Task SignalChildWorkflowAsync(Workflow parentWorkflow, ChildExecution childExecution, string signalName, byte[] signalArgs)
        {
            Covenant.Requires<ArgumentNullException>(parentWorkflow != null, nameof(parentWorkflow));
            Covenant.Requires<ArgumentNullException>(childExecution != null, nameof(childExecution));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(signalName), nameof(signalName));

            var reply = await parentWorkflow.ExecuteNonParallel(
                async () =>
                {
                    return (WorkflowSignalChildReply)await CallProxyAsync(
                        new WorkflowSignalChildRequest()
                        {
                            ContextId   = parentWorkflow.ContextId,
                            ChildId     = childExecution.ChildId,
                            SignalName  = signalName,
                            SignalArgs  = signalArgs
                        });
                });

            reply.ThrowOnError();
            parentWorkflow.UpdateReplay(reply);
        }

        /// <summary>
        /// Transmits a signal to a running external workflow and then polls the completion by querying the workflow
        /// to wait for the signal to be received and processed by the workflow.  This overload does 
        /// not return a result.  This low-level method accepts a byte array with the already encoded 
        /// parameters.
        /// </summary>
        /// <param name="execution">The <see cref="WorkflowExecution"/>.</param>
        /// <param name="signalName">The target signal name.</param>
        /// <param name="signalId">The globally unique signal transaction ID.</param>
        /// <param name="signalArgs">Specifies the <see cref="SyncSignalCall"/> as a single item array and encoded as a byte array.</param>
        /// <param name="domain">Optionally specifies the domain.</param>
        /// <returns>The encoded signal results or <c>null</c> for signals that don't return a result.</returns>
        /// <exception cref="SyncSignalException">Thrown if the target synchronous signal doesn't exist or the workflow is already closed.</exception>
        /// <exception cref="InternalServiceException">Thrown for internal Cadence problems.</exception>
        /// <exception cref="CadenceTimeoutException">Thrown if the operation timed out while waiting for a reply.</exception>
        /// <remarks>
        /// <para>
        /// <paramref name="signalArgs"/> must include an internal <see cref="SyncSignalCall"/> encoded as 
        /// the only argument.  This first includes the information required by the worker to route to the 
        /// user's signal, the globally unique transaction ID that the worker will use to track the signal
        /// execution state and the client will use to poll for that state.  This also includes the encoded
        /// user arguments being passed to the signal.
        /// </para>
        /// <note>
        /// The value passed as <paramref name="signalId"/> must match that in <see cref="SyncSignalStatus"/>
        /// encoded as the encoded in <paramref name="signalArgs"/>.
        /// </note>
        /// </remarks>
        internal async Task<byte[]> SyncSignalWorkflowAsync(WorkflowExecution execution, string signalName, string signalId, byte[] signalArgs, string domain = null)
        {
            Covenant.Requires<ArgumentNullException>(execution != null, nameof(execution));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(signalName), nameof(signalName));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(signalId), nameof(signalId));
            Covenant.Requires<ArgumentNullException>(signalArgs != null && signalArgs.Length > 1, nameof(signalArgs));
            EnsureNotDisposed();

            // Detect whether the workflow is already closed or wait for it to start running.

            await WaitForWorkflowStartAsync(execution);

            // Send the signal.

            await SignalWorkflowAsync(execution, SignalSync, signalArgs, domain);

            // Poll for the result via queries.

            byte[]              queryArgs = CadenceHelper.ArgsToBytes(DataConverter, new object[] { signalId });
            byte[]              rawStatus = null;
            SyncSignalStatus    status    = null;

            status = await SyncSignalRetry.InvokeAsync<SyncSignalStatus>(
                async () =>
                {
                    try
                    {
                        // $todo(jefflill):
                        //
                        // We should use consistent queries here after we support query consistency:
                        //
                        //      https://github.com/nforgeio/neonKUBE/issues/751

                        rawStatus = await QueryWorkflowAsync(execution, QuerySyncSignal, queryArgs, domain);
                        status    = JsonDataConverter.Instance.FromData<SyncSignalStatus>(rawStatus);

                        if (!status.Completed)
                        {
                            throw new CadenceTimeoutException($"Timeout waiting for reply from signal [{signalName}].");
                        }

                        return status;
                    }
                    catch (EntityNotExistsException)
                    {
                        // Stop polling when the workflow is no longer open.

                        return new SyncSignalStatus()
                        {
                            Error = SyncSignalException.GetError<EntityNotExistsException>($"Workflow [workflowID={execution}, RunID={execution.RunId}] not found or is no longer open.")
                        };
                    }
                });

            // Handle any returned error.

            if (status.Error != null)
            {
                throw new SyncSignalException(status.Error);
            }

            return status.Result;
        }

        /// <summary>
        /// Transmits a signal to a child workflow and then polls for the completion by querying the workflow
        /// to wait for the signal to be received and processed by the workflow.  This overload does 
        /// not return a result.  This low-level method accepts a byte array with the already encoded 
        /// parameters.
        /// </summary>
        /// <param name="parentWorkflow">The parent workflow.</param>
        /// <param name="childExecution">The child workflow execution.</param>
        /// <param name="signalName">The target signal name.</param>
        /// <param name="signalId">The globally unique signal transaction ID.</param>
        /// <param name="signalArgs">Specifies the <see cref="SyncSignalCall"/> as a single item array and encoded as a byte array.</param>
        /// <returns>The encoded signal results or <c>null</c> for signals that don't return a result.</returns>
        /// <exception cref="SyncSignalException">Thrown if the target synchronous signal doesn't exist or the workflow is already closed.</exception>
        /// <exception cref="InternalServiceException">Thrown for internal Cadence problems.</exception>
        /// <exception cref="CadenceTimeoutException">Thrown if the operation timed out while waiting for a reply.</exception>
        /// <remarks>
        /// <para>
        /// <paramref name="signalArgs"/> must include an internal <see cref="SyncSignalCall"/> encoded as 
        /// the only argument.  This first includes the information required by the worker to route to the 
        /// user's signal, the globally unique transaction ID that the worker will use to track the signal
        /// execution state and the client will use to poll for that state.  This also includes the encoded
        /// user arguments being passed to the signal.
        /// </para>
        /// <note>
        /// The value passed as <paramref name="signalId"/> must match that in <see cref="SyncSignalStatus"/>
        /// encoded as the encoded in <paramref name="signalArgs"/>.
        /// </note>
        /// </remarks>
        internal async Task<byte[]> SyncSignalChildWorkflowAsync(Workflow parentWorkflow, ChildExecution childExecution, string signalName, string signalId, byte[] signalArgs)
        {
            Covenant.Requires<ArgumentNullException>(parentWorkflow != null, nameof(parentWorkflow));
            Covenant.Requires<ArgumentNullException>(childExecution != null, nameof(childExecution));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(signalName), nameof(signalName));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(signalId), nameof(signalId));
            Covenant.Requires<ArgumentNullException>(signalArgs != null && signalArgs.Length > 1, nameof(signalArgs));
            EnsureNotDisposed();

            // Detect whether the workflow is already closed or wait for it to start running.

            await WaitForWorkflowStartAsync(childExecution.Execution);

            // Send the signal.

            await SignalChildWorkflowAsync(parentWorkflow, childExecution, SignalSync, signalArgs);

            // Poll for the result via queries.

            byte[]              queryArgs = CadenceHelper.ArgsToBytes(DataConverter, new object[] { signalId });
            byte[]              rawStatus = null;
            SyncSignalStatus    status    = null;

            await SyncSignalRetry.InvokeAsync<SyncSignalStatus>(
                async () =>
                {
                    try
                    {
                        // $todo(jefflill):
                        //
                        // We should use consistent queries here after we support query consistency:
                        //
                        //      https://github.com/nforgeio/neonKUBE/issues/751

                        rawStatus = await QueryWorkflowAsync(childExecution.Execution, QuerySyncSignal, queryArgs);
                        status    = JsonDataConverter.Instance.FromData<SyncSignalStatus>(rawStatus);

                        if (!status.Completed)
                        {
                            throw new CadenceTimeoutException($"Timeout waiting for reply from signal [{signalName}].");
                        }

                        return status;
                    }
                    catch (EntityNotExistsException)
                    {
                        // Stop polling when the workflow is no longer open.

                        status.Error = $"Workflow [workflowID={childExecution.Execution}, RunID={childExecution.Execution.RunId}] not found or is no longer open.";
                        return status;
                    }
                });

            // Handle any returned error.

            if (status.Error != null)
            {
                throw new SyncSignalException(status.Error);
            }

            return status.Result;
        }
    }
}
