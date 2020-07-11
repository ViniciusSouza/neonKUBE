﻿//-----------------------------------------------------------------------------
// FILE:	    HttpClientExtensions.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2020 by neonFORGE, LLC.  All rights reserved.
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Diagnostics;
using Neon.Collections;
using Neon.Tasks;
using Neon.Net;

namespace System.Net.Http
{
    /// <summary>
    /// <see cref="HttpClient"/> extension methods, mostly related to supporting <see cref="LogActivity"/> 
    /// related headers.
    /// </summary>
    public static partial class HttpClientExtensions
    {
        /// <summary>
        /// Ensures that an HTTP operation succedded but thrown an exception if it didn't.
        /// </summary>
        /// <param name="response">The response to be checked.</param>
        /// <returns>The <paramref name="response"/> on success.</returns>
        /// <exception cref="HttpException">Thrown for failures.</exception>
        private static HttpResponseMessage EnsureSuccess(HttpResponseMessage response)
        {
            Covenant.Requires<ArgumentNullException>(response != null, nameof(response));

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpException($"{response.StatusCode}: {response.ReasonPhrase}", requestUri: response.RequestMessage.RequestUri.ToString());
            }

            return response;
        }

        /// <summary>
        /// Sends a GET request to the specified string URI ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> GetSafeAsync(
            this HttpClient         client, 
            Uri                     requestUri, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default, 
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends a GET request to a specified <see cref="Uri"/> ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> GetSafeAsync(
            this HttpClient         client, 
            string                  requestUri, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default, 
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends a GET to a specified string URI and returns the response body as a byte array ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response byte array.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<byte[]> GetByteArraySafeAsync(
            this HttpClient         client, 
            string                  requestUri, 
            ArgDictionary           headers  = null,
            LogActivity             activity = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            var response = EnsureSuccess(await client.SendAsync(request));

            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// Sends a GET to a specified <see cref="Uri"/> and returns the response body as a byte array ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response byte array.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<byte[]> GetByteArraySafeAsync(
            this HttpClient         client,
            Uri                     requestUri,
            ArgDictionary           headers  = null,
            LogActivity             activity = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            var response = EnsureSuccess(await client.SendAsync(request));

            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// Sends a GET to a specified string URI and returns the response body as a <see cref="Stream"/> ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response stream.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<Stream> GetStreamSafeAsync(
            this HttpClient         client, 
            string                  requestUri, 
            ArgDictionary           headers  = null,
            LogActivity             activity = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            var response = EnsureSuccess(await client.SendAsync(request));

            return await response.Content.ReadAsStreamAsync();
        }

        /// <summary>
        /// Sends a GET to a specified <see cref="Uri"/> and returns the response body as a <see cref="Stream"/> ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response stream.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<Stream> GetStreamSafeAsync(
            this HttpClient         client,
            Uri                     requestUri, 
            ArgDictionary           headers  = null,
            LogActivity             activity = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            var response = EnsureSuccess(await client.SendAsync(request));

            return await response.Content.ReadAsStreamAsync();
        }

        /// <summary>
        /// Sends a GET request to a string URI and returns the response as a string ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<string> GetStringSafeAsync(
            this HttpClient         client, 
            string                  requestUri,
            ArgDictionary           headers  = null,
            LogActivity             activity = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            var response = EnsureSuccess(await client.SendAsync(request));

            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Sends a GET request to a <see cref="Uri"/> and returns the response as a string ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<string> GetStringSafeAsync(
            this HttpClient         client,
            Uri                     requestUri,
            ArgDictionary           headers  = null,
            LogActivity             activity = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            var response = EnsureSuccess(await client.SendAsync(request));

            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Sends a POST request to a string URI ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> PostSafeAsync(
            this HttpClient         client, 
            string                  requestUri,
            HttpContent             content,
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default,
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);

            request.Content = content;

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends a POST request to a <see cref="Uri"/> ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> PostSafeAsync(
            this HttpClient         client,
            Uri                     requestUri, 
            HttpContent             content, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default, 
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);

            request.Content = content;

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends a PUT request to a string URI ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> PutSafeAsync(
            this HttpClient         client,
            string                  requestUri, 
            HttpContent             content, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default,
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(HttpMethod.Put, requestUri);

            request.Content = content;

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends a PUT request to a <see cref="Uri"/> ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> PutSafeAsync(
            this HttpClient         client,
            Uri                     requestUri, 
            HttpContent             content, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default,
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(HttpMethod.Put, requestUri);

            request.Content = content;

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }


        /// <summary>
        /// Sends a DELETE request to a string URI ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> DeleteSafeAsync(
            this HttpClient         client, 
            string                  requestUri, 
            HttpContent             content           = null, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default,
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(deleteMethod, requestUri);

            if (content != null)
            {
                request.Content = content;
            }

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends a DELETE request to a <see cref="Uri"/> ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> DeleteSafeAsync(
            this HttpClient         client,
            Uri                     requestUri, 
            HttpContent             content           = null, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default,
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(deleteMethod, requestUri);

            if (content != null)
            {
                request.Content = content;
            }

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends a PATCH request to a string URI ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> PatchSafeAsync(
            this HttpClient         client, 
            string                  requestUri, 
            HttpContent             content, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default,
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(patchMethod, requestUri);

            request.Content = content;

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends a PATCH request to a <see cref="Uri"/> ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> PatchSafeAsync(
            this HttpClient         client,
            Uri                     requestUri, 
            HttpContent             content, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default,
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(patchMethod, requestUri);

            request.Content = content;

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends a OPTIONS request to a string URI ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> OptionsSafeAsync(
            this HttpClient         client,
            string                  requestUri,
            HttpContent             content           = null, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default,
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(optionsMethod, requestUri);

            if (content != null)
            {
                request.Content = content;
            }

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends a OPTIONS request to a <see cref="Uri"/> ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> OptionsSafeAsync(
            this HttpClient         client,
            Uri                     requestUri,
            HttpContent             content           = null,
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default, 
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(optionsMethod, requestUri);

            if (content != null)
            {
                request.Content = content;
            }

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends a HEAD request to a string URI ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> HeadSafeAsync(
            this HttpClient         client,
            string                  requestUri, 
            HttpContent             content           = null, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default, 
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(headMethod, requestUri);

            if (content == null)
            {
                request.Content = content;
            }

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends a HEAD request to a <see cref="Uri"/> ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> HeadSafeAsync(
            this HttpClient         client, 
            Uri                     requestUri, 
            HttpContent             content           = null, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default,
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            var request = new HttpRequestMessage(headMethod, requestUri);

            if (content != null)
            {
                request.Content = content;
            }

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }

        /// <summary>
        /// Sends an <see cref="HttpRequestMessage"/> ensuring that the operation succeeded.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="request">The request.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="activity">Optional <see cref="LogActivity"/> whose ID is to be included in the request.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request has already been sent by the <see cref="HttpClient"/> class.</exception>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public static async Task<HttpResponseMessage> SendSafeAsync(
            this HttpClient         client,
            HttpRequestMessage      request,
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default,
            LogActivity             activity          = default)
        {
            await SyncContext.ClearAsync;

            if (!string.IsNullOrEmpty(activity.Id))
            {
                request.Headers.Add(LogActivity.HttpHeader, activity.Id);
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }

            return EnsureSuccess(await client.SendAsync(request, completionOption, cancellationToken));
        }
    }
}
