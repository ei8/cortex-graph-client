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

namespace ei8.Cortex.Graph.Client
{
    public class HttpNeuronGraphQueryClient : INeuronGraphQueryClient
    {
        private readonly IRequestProvider requestProvider;
        
        private static Policy exponentialRetryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                3,
                attempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)),
                (ex, _) => HttpNeuronGraphQueryClient.logger.Error(ex, "Error occurred while querying Neurul Cortex. " + ex.InnerException?.Message)
            );

        private static readonly string GetNeuronsPathTemplate = "cortex/graph/neurons";
        private static readonly string GetRelativesPathTemplate = "cortex/graph/neurons/{0}/relatives";
        private static readonly string GetTerminalsPathTemplate = "cortex/graph/terminals/{0}";
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public HttpNeuronGraphQueryClient(IRequestProvider requestProvider = null)
        {
            this.requestProvider = requestProvider ?? Locator.Current.GetService<IRequestProvider>();
        }

        public async Task<IEnumerable<Neuron>> GetNeuronById(string outBaseUrl, string id, string centralId = default(string), RelativeType type = RelativeType.NotSet, CancellationToken token = default(CancellationToken)) =>
            await HttpNeuronGraphQueryClient.exponentialRetryPolicy.ExecuteAsync(
                async () => await this.GetNeuronByIdInternal(outBaseUrl, id, centralId, type, token).ConfigureAwait(false));

        private async Task<IEnumerable<Neuron>> GetNeuronByIdInternal(string outBaseUrl, string id, string centralId = default(string), RelativeType type = RelativeType.NotSet, CancellationToken token = default(CancellationToken))
        {
            string path = string.Empty;
            var queryStringBuilder = new StringBuilder();

            // TODO: instead of forcibly modifying the request in the else statement, should we throw an exception to inform client that type is required when central is specified?
            if (!string.IsNullOrEmpty(centralId) && type != RelativeType.NotSet)
            {
                path = $"{HttpNeuronGraphQueryClient.GetNeuronsPathTemplate}/{centralId}/relatives/{id}";
                queryStringBuilder.Append($"?{nameof(type)}={type.ToString()}");
            }
            else
                path = $"{HttpNeuronGraphQueryClient.GetNeuronsPathTemplate}/{id}";

            return await HttpNeuronGraphQueryClient.GetNeuronsUnescaped(outBaseUrl, path, queryStringBuilder, token, requestProvider);
        }

        public async Task<IEnumerable<Neuron>> GetNeurons(string outBaseUrl, string centralId = default(string), RelativeType type = RelativeType.NotSet, NeuronQuery neuronQuery = null, int? limit = 1000, CancellationToken token = default(CancellationToken)) =>
            await HttpNeuronGraphQueryClient.exponentialRetryPolicy.ExecuteAsync(
                async () => await this.GetNeuronsInternal(outBaseUrl, centralId, type, neuronQuery, limit, token).ConfigureAwait(false));

        private async Task<IEnumerable<Neuron>> GetNeuronsInternal(string outBaseUrl, string centralId = default(string), RelativeType type = RelativeType.NotSet, NeuronQuery neuronQuery = null, int? limit = 1000, CancellationToken token = default(CancellationToken))
        {
            var queryStringBuilder = new StringBuilder();

            // TODO: if (type != RelativeType.NotSet)
            //    queryStringBuilder.Append("type=")
            //        .Append(type.ToString());
            if (neuronQuery != null)
            {
                HttpNeuronGraphQueryClient.AppendQuery(neuronQuery.Id, nameof(NeuronQuery.Id), queryStringBuilder);
                HttpNeuronGraphQueryClient.AppendQuery(neuronQuery.IdNot, nameof(NeuronQuery.IdNot), queryStringBuilder);
                HttpNeuronGraphQueryClient.AppendQuery(neuronQuery.TagContains, nameof(NeuronQuery.TagContains), queryStringBuilder);
                HttpNeuronGraphQueryClient.AppendQuery(neuronQuery.TagContainsNot, nameof(NeuronQuery.TagContainsNot), queryStringBuilder);
                HttpNeuronGraphQueryClient.AppendQuery(neuronQuery.Presynaptic, nameof(NeuronQuery.Presynaptic), queryStringBuilder);
                HttpNeuronGraphQueryClient.AppendQuery(neuronQuery.PresynapticNot, nameof(NeuronQuery.PresynapticNot), queryStringBuilder);
                HttpNeuronGraphQueryClient.AppendQuery(neuronQuery.Postsynaptic, nameof(NeuronQuery.Postsynaptic), queryStringBuilder);
                HttpNeuronGraphQueryClient.AppendQuery(neuronQuery.PostsynapticNot, nameof(NeuronQuery.PostsynapticNot), queryStringBuilder);
            }
            if (limit.HasValue)
            {
                if (queryStringBuilder.Length > 0)
                    queryStringBuilder.Append('&');

                queryStringBuilder
                    .Append("limit=")
                    .Append(limit.Value);
            }
            if (queryStringBuilder.Length > 0)
                queryStringBuilder.Insert(0, '?');

            var path = string.IsNullOrEmpty(centralId) ? HttpNeuronGraphQueryClient.GetNeuronsPathTemplate : string.Format(HttpNeuronGraphQueryClient.GetRelativesPathTemplate, centralId);

            return await HttpNeuronGraphQueryClient.GetNeuronsUnescaped(outBaseUrl, path, queryStringBuilder, token, requestProvider);
        }

        private static async Task<IEnumerable<Neuron>> GetNeuronsUnescaped(string outBaseUrl, string path, StringBuilder queryStringBuilder, CancellationToken token, IRequestProvider requestProvider)
        {
            var result = await requestProvider.GetAsync<IEnumerable<Neuron>>(
                           $"{outBaseUrl}{path}{queryStringBuilder.ToString()}",
                           token: token
                           );
            result.Where(n => n.Tag != null).ToList().ForEach(n => n.Tag = Regex.Unescape(n.Tag));
            return result;
        }

        private static void AppendQuery(IEnumerable<string> field, string fieldName, StringBuilder queryStringBuilder)
        {
            if (field != null && field.Any())
            {
                if (queryStringBuilder.Length > 0)
                    queryStringBuilder.Append('&');
                queryStringBuilder.Append(string.Join("&", field.Select(s => $"{fieldName}={s}")));
            }
        }

        public async Task<Terminal> GetTerminalById(string outBaseUrl, string id, CancellationToken token = default) =>
            await HttpNeuronGraphQueryClient.exponentialRetryPolicy.ExecuteAsync(
                async () => await this.GetTerminalByIdInternal(outBaseUrl, id, token).ConfigureAwait(false));

        private async Task<Terminal> GetTerminalByIdInternal(string outBaseUrl, string id, CancellationToken token = default(CancellationToken))
        {
            return await this.requestProvider.GetAsync<Terminal>(
                $"{outBaseUrl}{string.Format(HttpNeuronGraphQueryClient.GetTerminalsPathTemplate, id)}",
                token: token
                );
        }
    }
}
