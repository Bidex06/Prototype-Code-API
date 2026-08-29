using Microsoft.AspNetCore.DataProtection;

namespace TradingBotEngine.Services
{
    public interface ICredentialProtector
    {
        string Protect(string plaintext);
        string Unprotect(string protectedValue);
    }

    public sealed class CredentialProtector : ICredentialProtector
    {
        private readonly IDataProtector _protector;

        public CredentialProtector(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("TradingBotEngine.BrokerCredentials.v1");
        }

        public string Protect(string plaintext)
        {
            if (string.IsNullOrWhiteSpace(plaintext))
                throw new ArgumentException("Credential cannot be empty.", nameof(plaintext));

            return _protector.Protect(plaintext);
        }

        public string Unprotect(string protectedValue)
        {
            if (string.IsNullOrWhiteSpace(protectedValue))
                throw new ArgumentException("Protected credential cannot be empty.", nameof(protectedValue));

            return _protector.Unprotect(protectedValue);
        }
    }
}
