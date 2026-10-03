namespace Enoch.Protocol;
public sealed record Problem(string Code, string Message, object? Details = null);
public sealed class EnochException(string code, string message, int status = 400, object? details = null) : Exception(message)
{
    public string Code { get; } = code; public int Status { get; } = status; public object? Details { get; } = details;
}
