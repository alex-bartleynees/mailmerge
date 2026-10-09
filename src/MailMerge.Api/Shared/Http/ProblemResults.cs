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
