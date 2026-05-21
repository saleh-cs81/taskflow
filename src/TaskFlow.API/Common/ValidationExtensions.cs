using FluentValidation;
using TaskFlow.Application.Common.Exceptions;

namespace TaskFlow.API.Common;

public static class ValidationExtensions
{
    // Runs a FluentValidation validator and throws a localizable ValidationAppException on failure.
    public static async Task ValidateAndThrowAppAsync<T>(this IValidator<T> validator, T instance, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(instance, ct);
        if (result.IsValid) return;

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        throw new ValidationAppException("error.validation", errors);
    }
}
