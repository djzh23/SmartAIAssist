namespace CvStudio.Application.Exceptions;

public sealed class UnprocessableEntityException : Exception
{
    public UnprocessableEntityException(IReadOnlyList<string> errors) : base("Validation failed")
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

