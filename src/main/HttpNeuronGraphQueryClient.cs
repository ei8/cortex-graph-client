/*
    This file is part of the d# project.
    Copyright (c) 2016-2018 ei8
    Authors: ei8
     This program is free software; you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License version 3
    as published by the Free Software Foundation with the addition of the
    following permission added to Section 15 as permitted in Section 7(a):
    FOR ANY PART OF THE COVERED WORK IN WHICH THE COPYRIGHT IS OWNED BY
    EI8. EI8 DISCLAIMS THE WARRANTY OF NON INFRINGEMENT OF THIRD PARTY RIGHTS
     This program is distributed in the hope that it will be useful, but
    WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
    or FITNESS FOR A PARTICULAR PURPOSE.
    See the GNU Affero General Public License for more details.
    You should have received a copy of the GNU Affero General Public License
    along with this program; if not, see http://www.gnu.org/licenses or write to
    the Free Software Foundation, Inc., 51 Franklin Street, Fifth Floor,
    Boston, MA, 02110-1301 USA, or download the license from the following URL:
    https://github.com/ei8/cortex-diary/blob/master/LICENSE
     The interactive user interfaces in modified source and object code versions
    of this program must display Appropriate Legal Notices, as required under
    Section 5 of the GNU Affero General Public License.
     You can be released from the requirements of the license by purchasing
    a commercial license. Buying such a license is mandatory as soon as you
    develop commercial activities involving the d# software without
    disclosing the source code of your own applications.
     For more information, please contact ei8 at this address: 
     support@ei8.works
 */

using NLog;
using neurUL.Common.Http;
using Polly;
using Splat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ei8.Cortex.Graph.Common;
using neurUL.Common.Domain.Model;
using Polly.Retry;

namespace ei8.Cortex.Graph.Client
{
    public class HttpNeuronGraphQueryClient : INeuronGraphQueryClient
    {
        private readonly IRequestProvider requestProvider;
        
        private static AsyncRetryPolicy exponentialRetryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                3,
                attempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)),
                (ex, _) => HttpNeuronGraphQueryClient.logger.Error(ex, "Error occurred while querying Neurul Cortex. " + ex.InnerException?.Message)
            );

        private static readonly string GetNeuronsPathTemplate = "cortex/graph/neurons";
        private static readonly string GetRelativesPathTemplate = "cortex/graph/neurons/{0}/relatives";
        private static readonly string GetTerminalsPathTemplate = "cortex/graph/terminals";
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public HttpNeuronGraphQueryClient(IRequestProvider requestProvider = null)
        {
            var rp = requestProvider ?? Locator.Current.GetService<IRequestProvider>();
            AssertionConcern.AssertArgumentNotNull(rp, nameof(requestProvider));

            this.requestProvider = rp;
        }

        public async Task<QueryResult> GetNeuronById(string outBaseUrl, string id, NeuronQuery neuronQuery, CancellationToken token = default(CancellationToken)) =>
            await HttpNeuronGraphQueryClient.exponentialRetryPolicy.ExecuteAsync(
                async () => await this.GetNeuronByIdInternal(outBaseUrl, id, neuronQuery, token).ConfigureAwait(false));

        private async Task<QueryResult> GetNeuronByIdInternal(string outBaseUrl, string id, NeuronQuery neuronQuery, CancellationToken token = default(CancellationToken))
        {
            return await HttpNeuronGraphQueryClient.GetNeuronsUnescaped(
                outBaseUrl,
                $"{HttpNeuronGraphQueryClient.GetNeuronsPathTemplate}/{id}",
                neuronQuery.ToString(),
                token,
                requestProvider
                );
        }

        public async Task<QueryResult> GetNeuronById(string outBaseUrl, string id, string centralId, NeuronQuery neuronQuery, CancellationToken token = default(CancellationToken)) =>
            await HttpNeuronGraphQueryClient.exponentialRetryPolicy.ExecuteAsync(
                async () => await this.GetNeuronByIdWithCentralInternal(outBaseUrl, id, centralId, neuronQuery, token).ConfigureAwait(false));

        private async Task<QueryResult> GetNeuronByIdWithCentralInternal(string outBaseUrl, string id, string centralId, NeuronQuery neuronQuery, CancellationToken token = default(CancellationToken))
        {
            return await HttpNeuronGraphQueryClient.GetNeuronsUnescaped(
                outBaseUrl,
                $"{HttpNeuronGraphQueryClient.GetNeuronsPathTemplate}/{centralId}/relatives/{id}", 
                neuronQuery.ToString(), 
                token, 
                requestProvider
                );
        }

        public async Task<QueryResult> GetNeurons(string outBaseUrl, NeuronQuery neuronQuery, CancellationToken token = default(CancellationToken)) =>
            await this.GetNeurons(outBaseUrl, null, neuronQuery, token);

        public async Task<QueryResult> GetNeurons(string outBaseUrl, string centralId, NeuronQuery neuronQuery, CancellationToken token = default(CancellationToken)) =>
            await HttpNeuronGraphQueryClient.exponentialRetryPolicy.ExecuteAsync(
                async () => await this.GetNeuronsInternal(outBaseUrl, centralId, neuronQuery, token).ConfigureAwait(false));

        private async Task<QueryResult> GetNeuronsInternal(string outBaseUrl, string centralId, NeuronQuery neuronQuery, CancellationToken token = default(CancellationToken))
        {
            var path = string.IsNullOrEmpty(centralId) ? 
                HttpNeuronGraphQueryClient.GetNeuronsPathTemplate : 
                string.Format(HttpNeuronGraphQueryClient.GetRelativesPathTemplate, centralId);

            return await HttpNeuronGraphQueryClient.GetNeuronsUnescaped(outBaseUrl, path, neuronQuery.ToString(), token, requestProvider);
        }
        
        private static async Task<QueryResult> GetNeuronsUnescaped(string outBaseUrl, string path, string queryString, CancellationToken token, IRequestProvider requestProvider)
        {
            var result = await requestProvider.GetAsync<QueryResult>(
                           $"{outBaseUrl}{path}{queryString}",
                           token: token
                           );
            result.Neurons.ToList().ForEach(n => n.UnescapeTag());
            return result;
        }

        public async Task<QueryResult> GetTerminalById(string outBaseUrl, string id, NeuronQuery neuronQuery, CancellationToken token = default) =>
            await HttpNeuronGraphQueryClient.exponentialRetryPolicy.ExecuteAsync(
                async () => await this.GetTerminalByIdInternal(outBaseUrl, id, neuronQuery, token).ConfigureAwait(false));

        private async Task<QueryResult> GetTerminalByIdInternal(string outBaseUrl, string id, NeuronQuery neuronQuery, CancellationToken token = default(CancellationToken))
        {
            return await this.requestProvider.GetAsync<QueryResult>(
                $"{outBaseUrl}{HttpNeuronGraphQueryClient.GetTerminalsPathTemplate}/{id}{neuronQuery.ToString()}",
                token: token
                );
        }

        public async Task<QueryResult> GetTerminals(string outBaseUrl, NeuronQuery neuronQuery, CancellationToken token = default(CancellationToken)) =>
            await HttpNeuronGraphQueryClient.exponentialRetryPolicy.ExecuteAsync(
                async () => await this.GetTerminalsInternal(outBaseUrl, neuronQuery, token).ConfigureAwait(false));

        private async Task<QueryResult> GetTerminalsInternal(string outBaseUrl, NeuronQuery neuronQuery, CancellationToken token = default(CancellationToken))
        {
            return await requestProvider.GetAsync<QueryResult>(
                $"{outBaseUrl}{HttpNeuronGraphQueryClient.GetTerminalsPathTemplate}{neuronQuery.ToString()}",
                token: token
                );
        }
    }
}
