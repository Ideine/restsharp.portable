﻿using System;
using System.Linq;
using System.Text;

using JetBrains.Annotations;

using RestSharp.Portable.Impl;

namespace RestSharp.Portable.Content
{
    /// <summary>
    /// Collects the content elements for a generic content implementation (as used by the WebRequest implementations of RestClient)
    /// </summary>
    public static class GenericContentCollector
    {
        private static readonly char[] _semicolon = { ';' };

        /// <summary>
        /// Gets the content for a request
        /// </summary>
        /// <param name="client">The REST client that will execute the request</param>
        /// <param name="request">REST request to get the content for</param>
        /// <param name="parameters">The merged request parameters</param>
        /// <returns>The HTTP content to be sent</returns>
        public static IHttpContent GetContent([CanBeNull] this IRestClient client, IRestRequest request, RequestParameters parameters)
        {
            IHttpContent content;
            var collectionMode = request?.ContentCollectionMode ?? ContentCollectionMode.MultiPartForFileParameters;
            if (collectionMode != ContentCollectionMode.BasicContent)
            {
                var fileParameters = parameters.OtherParameters.GetFileParameters().ToList();
                if (collectionMode == ContentCollectionMode.MultiPart || fileParameters.Count != 0)
                {
                    content = client.GetMultiPartContent(request, parameters);
                }
                else
                {
                    content = client.GetBasicContent(request, parameters);
                }
            }
            else
            {
                content = client.GetBasicContent(request, parameters);
            }

            if (content == null)
                return null;

            foreach (var param in parameters.ContentHeaderParameters)
            {
                if (content.Headers.Contains(param.Name))
                {
                    content.Headers.Remove(param.Name);
                }

                if (param.ValidateOnAdd)
                {
                    content.Headers.Add(param.Name, param.ToRequestString());
                }
                else
                {
                    content.Headers.TryAddWithoutValidation(param.Name, param.ToRequestString());
                }
            }

            return content;
        }

        /// <summary>
        /// Returns the HttpContent for the body parameter
        /// </summary>
        /// <param name="request">
        /// The request the body parameter belongs to
        /// </param>
        /// <param name="body">
        /// The body parameter
        /// </param>
        /// <returns>
        /// The resulting HttpContent
        /// </returns>
        internal static IHttpContent GetBodyContent(this IRestRequest request, Parameter body)
        {
            if (body == null)
            {
                return null;
            }

            string contentType;
            var buffer = body.Value as byte[];
            if (buffer != null)
            {
                contentType = body.ContentType ?? "application/octet-stream";
            }
            else
            {
                var s = body.Value as string;
                if (s != null && (body.Encoding != null || request.Serializer == null))
                {
                    var encoding = body.Encoding ?? Encoding.UTF8;
                    if (body.ContentType != null)
                    {
                        contentType = body.ContentType;
                        if (!contentType.Contains("charset="))
                            contentType += $";charset={encoding.WebName}";
                    }
                    else
                    {
                        contentType = $"text/plain;charset={encoding.WebName}";
                    }
                    buffer = encoding.GetBytes(s);
                }
                else
                {
                    buffer = request.Serializer.Serialize(body.Value);
                    contentType = request.Serializer.ContentType;
                }
            }

            var content = new ByteArrayContent(buffer);
            content.Headers.ReplaceWithoutValidation("Content-Type", contentType);
            content.Headers.ReplaceWithoutValidation("Content-Length", buffer.Length.ToString());
            return content;
        }

        /// <summary>
        /// Gets the basic content (without files) for a request
        /// </summary>
        /// <param name="client">The REST client that will execute the request</param>
        /// <param name="request">REST request to get the content for</param>
        /// <param name="parameters">The merged request parameters</param>
        /// <returns>The HTTP content to be sent</returns>
        private static IHttpContent GetBasicContent([CanBeNull] this IRestClient client, IRestRequest request, RequestParameters parameters)
        {
            IHttpContent content;
            var body = parameters.OtherParameters.FirstOrDefault(x => x.Type == ParameterType.RequestBody);
            if (body != null)
            {
                content = request.GetBodyContent(body);
            }
            else
            {
                var effectiveMethod = client.GetEffectiveHttpMethod(request, parameters.OtherParameters);
                if (effectiveMethod != Method.GET)
                {
                    var getOrPostParameters = parameters.OtherParameters.GetGetOrPostParameters().ToList();
                    content = getOrPostParameters.Count == 0 ? null : new PostParametersContent(getOrPostParameters);
                }
                else
                {
                    content = null;
                }
            }

            return content;
        }

        /// <summary>
        /// Gets the multi-part content (with files) for a request
        /// </summary>
        /// <param name="client">The REST client that will execute the request</param>
        /// <param name="request">REST request to get the content for</param>
        /// <param name="parameters">The merged request parameters</param>
        /// <returns>The HTTP content to be sent</returns>
        private static IHttpContent GetMultiPartContent([CanBeNull] this IRestClient client, IRestRequest request, RequestParameters parameters)
        {
            var isPostMethod = client.GetEffectiveHttpMethod(request, parameters.OtherParameters) == Method.POST;
            var multipartContent = new MultipartFormDataContent(new GenericHttpHeaders());
            foreach (var parameter in parameters.OtherParameters)
            {
                var fileParameter = parameter as FileParameter;
                if (fileParameter != null)
                {
                    var file = fileParameter;
                    var data = new ByteArrayContent((byte[])file.Value);
                    if (!string.IsNullOrEmpty(file.ContentType))
                    {
                        data.Headers.ReplaceWithoutValidation("Content-Type", file.ContentType);
                    }

                    data.Headers.ReplaceWithoutValidation("Content-Length", file.ContentLength.ToString());
                    multipartContent.Add(data, file.Name, file.FileName);
                }
                else if (isPostMethod && parameter.Type == ParameterType.GetOrPost)
                {
                    IHttpContent data;
                    var bytes = parameter.Value as byte[];
                    if (bytes != null)
                    {
                        var rawData = bytes;
                        data = new ByteArrayContent(rawData);
                        data.Headers.ReplaceWithoutValidation(
                            "Content-Type",
                            string.IsNullOrEmpty(parameter.ContentType) ? "application/octet-stream" : parameter.ContentType);
                        data.Headers.ReplaceWithoutValidation("Content-Length", rawData.Length.ToString());
                        multipartContent.Add(data, parameter.Name);
                    }
                    else
                    {
                        var value = parameter.ToRequestString();
                        data = new StringContent(value, parameter.Encoding ?? ParameterExtensions.DefaultEncoding);
                        if (!string.IsNullOrEmpty(parameter.ContentType))
                        {
                            data.Headers.ReplaceWithoutValidation("Content-Type", parameter.ContentType);
                        }

                        multipartContent.Add(data, parameter.Name);
                    }
                }
                else if (parameter.Type == ParameterType.RequestBody)
                {
                    var data = request.GetBodyContent(parameter);
                    var parameterName = parameter.Name ?? data.Headers.GetValue("Content-Type");
                    parameterName = (parameterName ?? string.Empty)
                        .Split(_semicolon, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .FirstOrDefault(x => !string.IsNullOrEmpty(x));
                    if (string.IsNullOrEmpty(parameterName))
                        throw new InvalidOperationException("You must specify a name for a body parameter.");
                    multipartContent.Add(data, parameterName);
                }
            }

            return multipartContent;
        }
    }
}
