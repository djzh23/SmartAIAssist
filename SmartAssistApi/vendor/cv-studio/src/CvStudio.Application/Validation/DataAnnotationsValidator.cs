using System.ComponentModel.DataAnnotations;

namespace CvStudio.Application.Validation;

public static class DataAnnotationsValidator
{
    public static IReadOnlyList<string> Validate(object model)
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, context, results, true);

        return results
            .Where(static r => !string.IsNullOrWhiteSpace(r.ErrorMessage))
            .Select(static r => r.ErrorMessage!)
            .ToList();
    }
}

