using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using ResultingKit;
using ResultingKit.AspNetCore;

namespace ValidatingKit.AspNetCore;

/// <summary>
/// Validates minimal API arguments before the handler runs. A failure short-circuits with the
/// <c>ValidationProblemDetails</c> that ResultingKit.AspNetCore builds; the handler never sees invalid input.
/// </summary>
/// <remarks>
/// Which arguments to validate is decided once, when the endpoint is built. Per request the filter resolves each
/// validator from <see cref="HttpContext.RequestServices"/>, so any lifetime works, and runs it. Endpoints that
/// validate also advertise a 400 <see cref="HttpValidationProblemDetails"/> response to OpenAPI.
/// </remarks>
public static class ValidationEndpointExtensions
{
    private static readonly ProducesResponseTypeMetadata s_problem =
        new(StatusCodes.Status400BadRequest, typeof(HttpValidationProblemDetails), ["application/problem+json"]);

    /// <summary>
    /// Validates every argument whose type has an <see cref="IValidator{T}"/> registered, on this endpoint or on
    /// every endpoint in this group.
    /// </summary>
    /// <typeparam name="TBuilder">An endpoint or a group, such as the result of <c>MapPost</c> or <c>MapGroup</c>.</typeparam>
    /// <param name="builder">The endpoint or group.</param>
    /// <param name="statusCode">
    /// Picks the status for a failure, as in ResultingKit.AspNetCore. <see langword="null"/> uses 400 and documents it
    /// for OpenAPI.
    /// </param>
    /// <example>
    /// <code>
    /// builder.Services.AddValidatorsFromAssemblyContaining&lt;Program&gt;();
    ///
    /// var api = app.MapGroup("/api").WithValidation();
    /// api.MapPost("/orders", (PlaceOrder order, OrderService orders) =&gt; orders.Place(order).ToHttpResult());
    /// </code>
    /// </example>
    /// <remarks>
    /// Applying it to a group and again to one of its endpoints validates once; the endpoint's
    /// <paramref name="statusCode"/> wins. Endpoints with nothing to validate get no filter at all.
    /// </remarks>
    [RequiresDynamicCode("Closes generic validator checks over parameter types at startup. Use Validate<T>() under native AOT.")]
    [RequiresUnreferencedCode("Closes generic validator checks over parameter types at startup. Use Validate<T>() when trimming.")]
    public static TBuilder WithValidation<TBuilder>(this TBuilder builder, Func<Error, int>? statusCode = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint =>
        {
            if (endpoint.Metadata.OfType<ValidationMarker>().FirstOrDefault() is { } existing)
            {
                existing.StatusCode = statusCode;
                if (statusCode is null && !endpoint.Metadata.Contains(s_problem))
                {
                    endpoint.Metadata.Add(s_problem);
                }
                else
                {
                    endpoint.Metadata.Remove(s_problem);
                }

                return;
            }

            if (endpoint.Metadata.OfType<MethodInfo>().FirstOrDefault() is not { } method)
            {
                return;
            }

            var checks = ArgumentCheck.ForValidators(ArgumentCheck.Describe(method), endpoint.ApplicationServices);
            if (checks.Length == 0)
            {
                return;
            }

            var marker = new ValidationMarker { StatusCode = statusCode };
            endpoint.Metadata.Add(marker);
            if (statusCode is null)
            {
                endpoint.Metadata.Add(s_problem);
            }
            endpoint.FilterFactories.Add((_, next) => Filter(checks, marker, next));
        });

        return builder;
    }

    /// <summary>Validates this endpoint's <typeparamref name="T"/> argument with inline rules, without a validator class.</summary>
    /// <typeparam name="T">The argument type, such as the request body.</typeparam>
    /// <param name="builder">The endpoint.</param>
    /// <param name="rules">The rules, such as <c>n =&gt; n.Field(x =&gt; x.Text, f =&gt; f.NotBlank())</c>.</param>
    /// <param name="code">The <see cref="Error.Code"/> of a failure. Defaults to <see cref="Validation.DefaultCode"/>.</param>
    /// <param name="statusCode">Picks the status for a failure. <see langword="null"/> uses 400.</param>
    /// <exception cref="InvalidOperationException">At startup, when the handler has no parameter of type <typeparamref name="T"/>.</exception>
    /// <remarks>Needs no reflection beyond reading the handler's parameters, so it is safe under trimming and native AOT.</remarks>
    public static RouteHandlerBuilder Validate<T>(
        this RouteHandlerBuilder builder,
        Func<Validation<T>, Validation<T>> rules,
        string code = Validation.DefaultCode,
        Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentException.ThrowIfNullOrEmpty(code);

        builder.Add(endpoint =>
        {
            var method = endpoint.Metadata.OfType<MethodInfo>().FirstOrDefault();
            var parameter = method?.GetParameters().FirstOrDefault(p => p.ParameterType == typeof(T))
                ?? throw new InvalidOperationException(
                    $"Validate<{typeof(T).Name}>() was applied to '{endpoint.DisplayName}', whose handler has no {typeof(T).Name} parameter.");

            ArgumentCheck[] checks = [new RulesCheck<T>(parameter.Position, parameter.Name ?? "value", code, rules)];
            var marker = new ValidationMarker { StatusCode = statusCode };

            if (statusCode is null && !endpoint.Metadata.Contains(s_problem))
            {
                endpoint.Metadata.Add(s_problem);
            }

            endpoint.FilterFactories.Add((_, next) => Filter(checks, marker, next));
        });

        return builder;
    }

    private static EndpointFilterDelegate Filter(ArgumentCheck[] checks, ValidationMarker marker, EndpointFilterDelegate next)
    {
        var statusCode = marker.StatusCode;

        return invocation =>
        {
            var services = invocation.HttpContext.RequestServices;
            var arguments = invocation.Arguments;

            foreach (var check in checks)
            {
                var result = check.Validate(services, arguments[check.Index]);
                if (result.IsFailure)
                {
                    return ValueTask.FromResult<object?>(result.ToHttpResult(statusCode));
                }
            }

            return next(invocation);
        };
    }

    /// <summary>Marks an endpoint as validated by <c>WithValidation</c>, so a second call only updates the status.</summary>
    private sealed class ValidationMarker
    {
        public Func<Error, int>? StatusCode { get; set; }
    }
}
