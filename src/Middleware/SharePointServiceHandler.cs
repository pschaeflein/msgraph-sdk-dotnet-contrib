using Graph.Community.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Graph;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Graph.Community
{
  public class SharePointServiceHandler : DelegatingHandler
  {
    /// <summary>
    /// SharePointServiceHandlerOption property
    /// </summary>
    internal SharePointServiceHandlerOption SharePointServiceHandlerOption { get; set; }

    /// <summary>
    /// Azure AppInsights Telemetry client
    /// </summary>
    internal TelemetryClient TelemetryClient { get; private set; }
    private readonly TelemetryConfiguration telemetryConfiguration = TelemetryConfiguration.CreateDefault();

    /// <summary>
    /// Constructs a new <see cref="SharePointServiceHandler"/> 
    /// </summary>
    /// <param name="sharepointServiceHandlerOption">An OPTIONAL <see cref="Microsoft.Graph.SharePointServiceHandlerOption"/> to configure <see cref="SharePointServiceHandler"/></param>
    public SharePointServiceHandler(SharePointServiceHandlerOption sharepointServiceHandlerOption = null)
    {
      SharePointServiceHandlerOption = sharepointServiceHandlerOption ?? new SharePointServiceHandlerOption();
      telemetryConfiguration.InstrumentationKey = "d882bd7a-a378-4117-bd7c-71fc95a44cd1";
      TelemetryClient = new TelemetryClient(telemetryConfiguration);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var disableTelemetry = CommunityGraphClientFactory.TelemetryDisabled;
      string resourceUri = null;

      SharePointServiceHandlerOption = request.GetMiddlewareOption<SharePointServiceHandlerOption>();

      if (SharePointServiceHandlerOption == null)
      {
        // This is not a request to SharePoint
        var segments = request.RequestUri.Segments;

        if (segments?.Length > 2)
        {
          resourceUri = $"{segments[1]}{segments[2]}";
        }
      }
      else
      {
        disableTelemetry = SharePointServiceHandlerOption.DisableTelemetry;
        resourceUri = SharePointServiceHandlerOption.ResourceUri;
      }

      var context = request.GetRequestContext();


      GraphCommunityEventSource.Singleton.SharePointServiceHandlerPreprocess(resourceUri, context);

      var response = await base.SendAsync(request, cancellationToken);

      GraphCommunityEventSource.Singleton.SharePointServiceHandlerPostprocess(resourceUri, context, response.StatusCode);


      if (SharePointServiceHandlerOption != null && !response.IsSuccessStatusCode)
      {
        using (response)
        {
          var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
          GraphCommunityEventSource.Singleton.SharePointServiceHandlerNonsuccess(resourceUri, context, responseContent);

          if (disableTelemetry != true)
          {
            LogServiceRequest(resourceUri, context, request.Method, response.StatusCode, responseContent);
          }

          var errorResponse = this.ConvertErrorResponseAsync(responseContent);
          Error error = null;

          if (errorResponse == null || errorResponse.Error == null)
          {
            // we couldn't parse the error, so return generic message
            if (response != null && response.StatusCode == HttpStatusCode.NotFound)
            {
              error = new Error { Code = "itemNotFound" };
            }
            else
            {
              error = new Error
              {
                Code = "generalException",
                Message = "Unexpected exception returned from the service."
              };
            }
          }
          else
          {
            error = errorResponse.Error;
          }

          // If the error has a json body, include it in the exception.
          if (response.Content?.Headers.ContentType?.MediaType == "application/json")
          {
            string rawResponseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            throw new ServiceException(error,
                                       response.Headers,
                                       response.StatusCode,
                                       rawResponseBody);
          }
          else
          {
            // Pass through the response headers and status code to the ServiceException.
            // System.Net.HttpStatusCode does not support RFC 6585, Additional HTTP Status Codes.
            // Throttling status code 429 is in RFC 6586. The status code 429 will be passed through.
            throw new ServiceException(error, response.Headers, response.StatusCode);
          }
        }
      }
      else
      {
        if (disableTelemetry != true)
        {
          LogServiceRequest(resourceUri, context, request.Method, response.StatusCode, null);
        }
      }

      return response;

    }

    /// <summary>
    /// Converts the <see cref="HttpRequestException"/> into an <see cref="ErrorResponse"/> object;
    /// </summary>
    /// <param name="response">The <see cref="HttpResponseMessage"/> to convert.</param>
    /// <returns>The <see cref="ErrorResponse"/> object.</returns>
    private ErrorResponse ConvertErrorResponseAsync(string responseContent)
    {
      try
      {
        // try our best to provide a helpful message...
        var responseObject = JsonDocument.Parse(responseContent).RootElement;
        var message = responseObject.TryGetProperty("error_description", out var errorDescription)
          ? errorDescription.ToString()
          : responseContent;

        var error = new ErrorResponse()
        {
          Error = new Error()
          {
            Code = "SPError",
            Message = message
          }
        };

        return error;

      }
      catch (Exception)
      {
        // If there's an exception deserializing the error response,
        // return null and throw a generic ServiceException later.
        return null;
      }
    }

    internal void LogServiceRequest(string resourceUri, GraphRequestContext context, HttpMethod requestMethod, HttpStatusCode statusCode, string rawResponseContent)
    {
      Dictionary<string, string> properties = new Dictionary<string, string>(10)
      {
        { CommunityGraphConstants.Headers.CommunityLibraryVersionHeaderName, CommunityGraphConstants.Library.AssemblyVersion },
        { CommunityGraphConstants.TelemetryProperties.ResourceUri, resourceUri },
        { CommunityGraphConstants.TelemetryProperties.RequestMethod, requestMethod.ToString() },
        { CommunityGraphConstants.TelemetryProperties.ClientRequestId, context.ClientRequestId },
        { CommunityGraphConstants.TelemetryProperties.ResponseStatus, $"{statusCode} ({(int)statusCode})" }
      };

      if (!string.IsNullOrEmpty(rawResponseContent))
      {
        properties.Add(CommunityGraphConstants.TelemetryProperties.RawErrorResponse, rawResponseContent);
      }

      TelemetryClient.TrackEvent("GraphCommunityRequest", properties);
      TelemetryClient.Flush();
    }

  }
}
