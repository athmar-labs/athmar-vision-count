using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    internal static class CustomerStorageScope
    {
        private const string ConfigResourceName = "AthmarVisionCountConfig";

        public static string ResolveOrRequire(string customerCode)
        {
            var normalized = Normalize(customerCode);
            if (normalized.Length == 0)
                normalized = ResolveCurrentCustomerCode();
            return Require(normalized);
        }

        public static string Require(string customerCode)
        {
            var normalized = Normalize(customerCode);
            if (normalized.Length == 0)
                throw new InvalidOperationException("A customer code is required for persisted inventory data.");
            return normalized;
        }

        public static string Normalize(string customerCode)
        {
            return string.IsNullOrWhiteSpace(customerCode) ? string.Empty : customerCode.Trim();
        }

        public static string StorageKey(string customerCode)
        {
            var normalized = Require(customerCode);
            using var algorithm = SHA256.Create();
            var digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(normalized));
            var builder = new StringBuilder(digest.Length * 2);
            for (var index = 0; index < digest.Length; index++)
                builder.Append(digest[index].ToString("x2"));
            return builder.ToString();
        }

        private static string ResolveCurrentCustomerCode()
        {
            try
            {
                var store = new CustomerPackageStore();
                if (store.TryLoadActive(out var snapshot, out _))
                    return Normalize(snapshot.Configuration.CustomerCode);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unable to resolve active customer package for local storage: " + exception.Message);
            }

            var config = Resources.Load<AppConfig>(ConfigResourceName);
            return Normalize(config == null ? null : config.CustomerCode);
        }
    }
}
