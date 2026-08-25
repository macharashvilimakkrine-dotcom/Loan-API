namespace LoanApi.Api.Application;

public abstract class ApiException(string message) : Exception(message);
public sealed class NotFoundException(string message) : ApiException(message);
public sealed class ConflictException(string message) : ApiException(message);
public sealed class ForbiddenException(string message) : ApiException(message);
public sealed class UnauthorizedException(string message) : ApiException(message);