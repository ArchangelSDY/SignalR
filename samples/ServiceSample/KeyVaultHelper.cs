using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System;
using System.Security.Cryptography.X509Certificates;

namespace ServiceSample
{
    public static class KeyVaultHelper
    {
        public static X509Certificate2 GetKestrelCertificate(IConfiguration config)
        {
            var kvAuthCallback = CreateAuthCallback(config["Https:KeyVaultClientId"], config["Https:KeyVaultClientCertPath"]);
            var kv = new KeyVaultClient(kvAuthCallback);
            var sb = kv.GetSecretAsync(config["Https:KeyVaultCertUri"]).Result;
            return new X509Certificate2(Convert.FromBase64String(sb.Value));
        }

        private static KeyVaultClient.AuthenticationCallback CreateAuthCallback(string clientId, string clientCertPath)
        {
            return async (string authority, string resource, string scope) =>
            {
                var authContext = new AuthenticationContext(authority);
                var clientCert = new X509Certificate2(clientCertPath);
                var credentials = new ClientAssertionCertificate(clientId, clientCert);
                AuthenticationResult result = await authContext.AcquireTokenAsync(resource, credentials);

                if (result == null)
                    throw new InvalidOperationException("Failed to obtain the JWT token");

                return result.AccessToken;
            };
        }
    }
}
