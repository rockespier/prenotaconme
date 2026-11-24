using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace PrenotaConMe.Shared.Validation;

/// <summary>Collects field errors and turns them into an RFC 7807 validation problem (400).</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsValid => _errors.Count == 0;

    /// <summary>Adds <paramref name="error"/> for <paramref name="field"/> when <paramref name="failed"/> is true.</summary>
    public ValidationErrors AddIf(bool failed, string field, string error)
    {
        if (failed)
        {
            Add(field, error);
        }

        return this;
    }

    public ValidationErrors Add(string field, string error)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add(error);
        return this;
    }

    public ValidationProblem ToProblem() =>
        TypedResults.ValidationProblem(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
}
