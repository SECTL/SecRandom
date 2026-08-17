using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Services.Draw;
using SecRandom.Services.Linkage;
using SecRandom.Services.Notification;
using SecRandom.Services.Security;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.SecAgent;

/// <summary>
/// Loopback-only REST endpoint for the local SecAgent connector.
/// SecRandom intentionally exposes ordinary HTTP/JSON here; tool discovery and hidden-tool
/// behavior belong to the SecAgent plugin.
/// </summary>
public sealed class SecAgentHttpHostedService(
    ILogger<SecAgentHttpHostedService> logger,
    IProfileService profileService,
    MainConfigHandler configHandler,
    IDrawTemporaryRecordService temporaryRecordService,
    DrawEngine drawEngine,
    LinkageDrawCoordinator linkageDrawCoordinator,
    NotificationService notificationService) : BackgroundService
{
    private const string Prefix = "http://127.0.0.1:3910/api/secagent/v1/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpListener _listener = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener.Prefixes.Add(Prefix);
        try
        {
            _listener.Start();
            logger.LogInformation("SecAgent loopback REST endpoint started at {Prefix}.", Prefix[..^1]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start SecAgent loopback REST endpoint at {Prefix}.", Prefix);
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().WaitAsync(stoppingToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(context, stoppingToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (HttpListenerException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _listener.Stop();
            _listener.Close();
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (_listener.IsListening)
            _listener.Stop();
        return base.StopAsync(cancellationToken);
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
            var method = context.Request.HttpMethod.ToUpperInvariant();
            JsonNode result;

            if (method == "GET" && path == "/api/secagent/v1/students")
                result = ListStudents();
            else if (method == "POST" && path == "/api/secagent/v1/students")
                result = UpsertStudent(await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false));
            else if (method == "DELETE" && path == "/api/secagent/v1/students")
                result = RemoveStudent(await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false));
            else if (method == "POST" && path == "/api/secagent/v1/draw/students")
                result = await DrawStudentsAsync(await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                result = new JsonObject { ["error"] = "Endpoint not found." };
            }

            await WriteJsonAsync(context.Response, result, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            await WriteErrorAsync(context.Response, HttpStatusCode.BadRequest, ex.Message).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            await WriteErrorAsync(context.Response, HttpStatusCode.Conflict, ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SecAgent REST request failed.");
            await WriteErrorAsync(context.Response, HttpStatusCode.InternalServerError, "SecRandom request failed.").ConfigureAwait(false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private JsonObject ListStudents()
    {
        var list = profileService.CurrentStudentList;
        return new JsonObject
        {
            ["profile"] = list?.Name ?? string.Empty,
            ["students"] = new JsonArray((list?.Students ?? []).Select(ToJson).ToArray())
        };
    }

    private JsonObject UpsertStudent(JsonObject arguments)
    {
        var list = profileService.CurrentStudentList ?? throw new InvalidOperationException("No current student profile.");
        var recordId = ParseGuid(arguments["record_id"]?.GetValue<string>());
        var id = StringArgument(arguments, "id");
        var student = recordId is not null ? list.Students.FirstOrDefault(item => item.RecordId == recordId) : null;
        student ??= !string.IsNullOrWhiteSpace(id) ? list.Students.FirstOrDefault(item => item.Id == id) : null;
        if (student is null)
        {
            student = new Student { RecordId = recordId ?? Guid.NewGuid() };
            list.Students.Add(student);
        }

        student.Id = id;
        student.Name = StringArgument(arguments, "name");
        student.Group = StringArgument(arguments, "group");
        student.Gender = StringArgument(arguments, "gender");
        student.Tags = StringArgument(arguments, "tags");
        student.Exists = arguments["exists"]?.GetValue<bool>() ?? true;
        if (!student.IsCandidate)
            throw new ArgumentException("Student requires a nonblank id or name.");
        profileService.SaveProfile();
        return new JsonObject { ["student"] = ToJson(student), ["profile"] = list.Name };
    }

    private JsonObject RemoveStudent(JsonObject arguments)
    {
        var list = profileService.CurrentStudentList ?? throw new InvalidOperationException("No current student profile.");
        var recordId = ParseGuid(arguments["record_id"]?.GetValue<string>());
        var id = StringArgument(arguments, "id");
        var name = StringArgument(arguments, "name");
        var matches = list.Students.Where(item =>
            (recordId is not null && item.RecordId == recordId)
            || (!string.IsNullOrWhiteSpace(id) && item.Id == id)
            || (!string.IsNullOrWhiteSpace(name) && item.Name == name)).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException(matches.Count == 0 ? "Student was not found." : "Student selector matched more than one student.");
        list.Students.Remove(matches[0]);
        profileService.SaveProfile();
        return new JsonObject { ["removed"] = ToJson(matches[0]), ["profile"] = list.Name };
    }

    private async Task<JsonObject> DrawStudentsAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var mode = StringArgument(arguments, "mode");
        if (mode is not ("flash" or "result_only"))
            throw new ArgumentException("mode must be flash or result_only.");

        var requestedCount = Math.Clamp(arguments["count"]?.GetValue<int>() ?? 1, 1, 100);
        if (mode == "flash") requestedCount = 1;
        var gender = StringArgument(arguments, "gender");
        var includeTags = StringArray(arguments, "include_tags");
        var excludeTags = StringArray(arguments, "exclude_tags");
        var includeIds = StringArray(arguments, "include_ids");
        var includeNames = StringArray(arguments, "include_names");
        var listName = profileService.CurrentStudentList?.Name ?? string.Empty;
        var temporaryCounts = temporaryRecordService.GetStudentCounts(listName, gender, string.Empty);
        var hasMatchingStudents = (profileService.CurrentStudentList?.Students ?? [])
            .Any(student => student.IsCandidate && Matches(student, gender, includeTags, excludeTags, includeIds, includeNames));
        var mayResetExhaustedRound = configHandler.Data.QuickDrawSettings.DrawMode != DrawMode.Repeat
                                     && hasMatchingStudents;

        var result = await InvokeAuthorizedAsync(SecurityOperation.QuickDrawStart, () =>
        {
            DrawResult<Student> DrawFromRemainingStudents()
                => drawEngine.DrawStudent(requestedCount,
                    student => Matches(student, gender, includeTags, excludeTags, includeIds, includeNames)
                               && !HasReachedTemporaryLimit(student, temporaryCounts),
                    DrawSettingsType.QuickDraw, linkageDrawCoordinator.GetCourseName());

            var draw = DrawFromRemainingStudents();
            if (!draw.IsSuccess && mayResetExhaustedRound && draw.Status == DrawStatus.RepeatLimitExhausted)
            {
                temporaryRecordService.ResetStudentList(listName);
                temporaryCounts = temporaryRecordService.GetStudentCounts(listName, gender, string.Empty);
                draw = DrawFromRemainingStudents();
            }

            if (!draw.IsSuccess || draw.Result.Count == 0)
                return Task.FromResult(draw);

            profileService.RecordStudentHistory(draw.Result, DateTime.Now, requestedCount,
                drawMethod: (int)configHandler.Data.QuickDrawSettings.DrawType,
                courseName: linkageDrawCoordinator.GetCourseName());
            temporaryRecordService.RecordStudents(listName, gender, string.Empty, draw.Result);
            if (mode == "flash")
                notificationService.QueueStudents(NotificationSettingsType.QuickDraw, linkageDrawCoordinator.GetCourseName(), draw.Result);
            return Task.FromResult(draw);
        }, cancellationToken).ConfigureAwait(false);

        return new JsonObject
        {
            ["mode"] = mode,
            ["count"] = result.Result.Count,
            ["status"] = result.Status.ToString(),
            ["profile"] = listName,
            ["students"] = new JsonArray(result.Result.Select(ToJson).ToArray())
        };
    }

    private async Task<DrawResult<Student>> InvokeAuthorizedAsync(SecurityOperation operation, Func<Task<DrawResult<Student>>> action, CancellationToken cancellationToken)
    {
        DrawResult<Student>? result = null;
        var authorized = await linkageDrawCoordinator.AuthorizeAsync(operation,
            async () => result = await action().ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        return authorized && result is not null ? result : new DrawResult<Student> { Status = DrawStatus.Failure };
    }

    private bool HasReachedTemporaryLimit(Student student, IReadOnlyDictionary<string, int> temporaryCounts)
    {
        var settings = configHandler.Data.QuickDrawSettings;
        var threshold = settings.DrawMode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, settings.HalfRepeat),
            _ => 1
        };
        return threshold > 0 && temporaryCounts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(student)) >= threshold;
    }

    private static bool Matches(Student student, string gender, IReadOnlyCollection<string> includeTags, IReadOnlyCollection<string> excludeTags,
        IReadOnlyCollection<string> includeIds, IReadOnlyCollection<string> includeNames)
    {
        var tags = student.Tags.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (string.IsNullOrWhiteSpace(gender) || string.Equals(student.Gender, gender, StringComparison.OrdinalIgnoreCase))
            && includeTags.All(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            && excludeTags.All(tag => !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            && (includeIds.Count == 0 || includeIds.Contains(student.Id, StringComparer.OrdinalIgnoreCase))
            && (includeNames.Count == 0 || includeNames.Contains(student.Name, StringComparer.OrdinalIgnoreCase));
    }

    private static JsonObject ToJson(Student student) => new()
    {
        ["record_id"] = ProfileRecordIdentity.EnsureRecordId(student),
        ["id"] = student.Id,
        ["name"] = student.Name,
        ["group"] = student.Group,
        ["gender"] = student.Gender,
        ["tags"] = student.Tags,
        ["exists"] = student.Exists
    };

    private static async Task<JsonObject> ReadBodyAsync(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        var body = await JsonNode.ParseAsync(request.InputStream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject;
        return body ?? throw new ArgumentException("Request body must be a JSON object.");
    }

    private static string StringArgument(JsonObject arguments, string name) => arguments[name]?.GetValue<string>()?.Trim() ?? string.Empty;
    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var result) ? result : null;

    private static IReadOnlyList<string> StringArray(JsonObject arguments, string name)
        => arguments[name] is JsonArray array
            ? array.Select(item => item?.GetValue<string>()?.Trim()).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray()
            : [];

    private static async Task WriteJsonAsync(HttpListenerResponse response, JsonNode value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value.ToJsonString(JsonOptions));
        response.ContentType = "application/json";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteErrorAsync(HttpListenerResponse response, HttpStatusCode status, string message)
    {
        response.StatusCode = (int)status;
        return WriteJsonAsync(response, new JsonObject { ["error"] = message }, CancellationToken.None);
    }
}
