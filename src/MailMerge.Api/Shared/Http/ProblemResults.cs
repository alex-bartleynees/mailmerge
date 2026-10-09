using Microsoft.AspNetCore.Http.HttpResults;
using SharedKernel.Results;

namespace MailMerge.Api.Shared.Http;

public static class ProblemResults
{
    /// <summary>
    /// An <see cref="Error"/> as RFC 7807 problem details. <c>detail</c> is a sentence staff can read; the UI shows
    /// it as-is. <c>code</c> is for programs.
    /// </summary>
    public static ProblemHttpResult ToProblem(this Error error) =>
        TypedResults.Problem(
            detail: error.Detail,
            statusCode: error.Status,
            title: error.Title,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}

public static class ResultHttpExtensions
{
    public static Results<Ok<T>, ProblemHttpResult> OkOrProblem<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.ValueOrThrow) : result.Error.ToProblem();

    public static Results<NoContent, ProblemHttpResult> NoContentOrProblem(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();

    public static Results<Created<T>, ProblemHttpResult> CreatedOrProblem<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.ValueOrThrow), result.ValueOrThrow) : result.Error.ToProblem();
}
