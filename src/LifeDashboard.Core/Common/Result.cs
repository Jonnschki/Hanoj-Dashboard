namespace LifeDashboard.Core.Common;

/// <summary>Fehlerbeschreibung mit maschinenlesbarem Code und Klartext.</summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>Ergebnis einer Operation ohne Rückgabewert. Erwartbare Fehler werden so statt per Exception gemeldet.</summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);
}

/// <summary>Ergebnis einer Operation mit Rückgabewert.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>Wert bei Erfolg. Zugriff bei Fehler wirft <see cref="InvalidOperationException"/>.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Result has no value: {Error}");

    public static Result<T> Success(T value) => new(true, value, Error.None);

    public static new Result<T> Failure(Error error) => new(false, default, error);
}
