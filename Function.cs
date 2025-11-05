using System.Net.Http.Headers;
using System.Text;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace EsepWebhook;

public class Function
{
    private static readonly HttpClient Http = new();

    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        // Basic health check
        if (request.HttpMethod?.Equals("GET", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Ok(new { ok = true, message = "EsepWebhook alive" });
        }

        // Verify GitHub event header (we only care about "issues")
        request.Headers ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        request.MultiValueHeaders ??= new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
        var eventHeader = request.Headers.TryGetValue("X-GitHub-Event", out var h) ? h : null;
        if (!string.Equals(eventHeader, "issues", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new { ignored = true, reason = "Not an 'issues' event" });
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest("Empty body");
        }

        // Parse the GitHub issues payload minimally
        dynamic? payload;
        try
        {
            payload = JsonConvert.DeserializeObject<dynamic>(request.Body);
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Failed to parse JSON: {ex}");
            return BadRequest("Invalid JSON");
        }

        // Safely extract fields
        string action = payload?.action ?? "unknown";
        string issueUrl = payload?.issue?.html_url ?? "";
        string issueTitle = payload?.issue?.title ?? "(no title)";
        int issueNumber = payload?.issue?.number ?? 0;
        string repo = payload?.repository?.full_name ?? "(unknown repo)";
        string sender = payload?.sender?.login ?? "(unknown sender)";

        if (string.IsNullOrWhiteSpace(issueUrl))
        {
            return Ok(new { ignored = true, reason = "No issue.html_url present" });
        }

        // Build Slack message
        var slackUrl = Environment.GetEnvironmentVariable("SLACK_URL");
        if (string.IsNullOrWhiteSpace(slackUrl))
        {
            context.Logger.LogError("SLACK_URL environment variable is not set");
            return ServerError("Slack not configured");
        }

        var text = $"*[{repo}]* Issue *#{issueNumber}* {action} by `{sender}`\n*{issueTitle}*\n{issueUrl}";

        var slackPayload = new
        {
            text, // simple text payload
        };

        var json = JsonConvert.SerializeObject(slackPayload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, slackUrl);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Content = content;

            var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                context.Logger.LogError($"Slack returned {resp.StatusCode}: {body}");
                return ServerError("Failed to post to Slack");
            }
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error calling Slack: {ex}");
            return ServerError("Slack call failed");
        }

        return Ok(new { posted = true, issue = issueUrl, action, repo });
    }

    private static APIGatewayProxyResponse Ok(object obj) =>
        new()
        {
            StatusCode = 200,
            Body = JsonConvert.SerializeObject(obj),
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" }
        };

    private static APIGatewayProxyResponse BadRequest(string message) =>
        new()
        {
            StatusCode = 400,
            Body = JsonConvert.SerializeObject(new { error = message }),
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" }
        };

    private static APIGatewayProxyResponse ServerError(string message) =>
        new()
        {
            StatusCode = 500,
            Body = JsonConvert.SerializeObject(new { error = message }),
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" }
        };
}