namespace LoanApi.Api.Application;

public static class JwtConfiguration
{
    public static string GetSigningKey(IConfiguration configuration)
    {
        string? key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Jwt:Key must be configured outside source control.");
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key must contain at least 32 characters.");
        }

        return key;
    }
}
