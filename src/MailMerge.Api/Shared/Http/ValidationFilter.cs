using FluentValidation;

namespace MailMerge.Api.Shared.Http;

public static class ValidationFilter
{
    /// <summary>
    /// Validates every endpoint argument that has a registered <see cref="IValidator{T}"/> before the handler runs.
    /// Validators are stateless singletons, so they're resolved once when the endpoint is built.
    /// </summary>
    public static TBuilder WithRequestValidation<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilterFactory((factory, next) =>
        {
            var validators = factory.MethodInfo.GetParameters()
                .Select((p, index) => (index, validator: factory.ApplicationServices.GetService(
                    typeof(IValidator<>).MakeGenericType(p.ParameterType)) as IValidator))
                .Where(x => x.validator is not null)
                .ToArray();

            if (validators.Length == 0)
            {
                return next;
            }

            return async invocation =>
            {
                foreach (var (index, validator) in validators)
                {
                    var argument = invocation.Arguments[index];
                    if (argument is null)
                    {
                        continue;
                    }

                    var context = new ValidationContext<object>(argument);
                    var result = await validator!.ValidateAsync(context, invocation.HttpContext.RequestAborted);
                    if (!result.IsValid)
                    {
                        return TypedResults.ValidationProblem(
                            result.ToDictionary(),
                            detail: result.Errors[0].ErrorMessage,
                            title: "Bad Request");
                    }
                }

                return await next(invocation);
            };
        });
        return builder;
    }
}
