using System;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    public static class LocalRetentionEnforcer
    {
        private const int MaximumRetentionDays = 365;
        private const string ConfigResourceName = "AthmarVisionCountConfig";

        private readonly struct RetentionPolicy
        {
            public RetentionPolicy(string customerCode, int retentionDays)
            {
                CustomerCode = customerCode;
                RetentionDays = retentionDays;
            }

            public string CustomerCode { get; }
            public int RetentionDays { get; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyRetention()
        {
            try
            {
                var config = Resources.Load<AppConfig>(ConfigResourceName);
                ApplyRetentionPolicy(
                    new CustomerPackageStore(),
                    config == null ? string.Empty : config.CustomerCode,
                    config == null ? 0 : config.RetentionDays,
                    new LocalScanRepository(),
                    new ExportFileService(),
                    DateTime.UtcNow);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unable to apply local retention: " + exception.Message);
            }
        }

        public static void ApplyRetentionPolicy(int retentionDays)
        {
            try
            {
                var config = Resources.Load<AppConfig>(ConfigResourceName);
                ApplyRetentionPolicy(
                    new CustomerPackageStore(),
                    config == null ? string.Empty : config.CustomerCode,
                    retentionDays,
                    new LocalScanRepository(),
                    new ExportFileService(),
                    DateTime.UtcNow);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unable to apply local retention: " + exception.Message);
            }
        }

        public static void ApplyRetentionPolicy(
            CustomerPackageStore packageStore,
            string bundledCustomerCode,
            int bundledRetentionDays,
            LocalScanRepository sessions,
            ExportFileService exports,
            DateTime utcNow)
        {
            if (sessions == null)
                throw new ArgumentNullException(nameof(sessions));
            if (exports == null)
                throw new ArgumentNullException(nameof(exports));

            var policy = ResolveRetentionPolicy(packageStore, bundledCustomerCode, bundledRetentionDays);
            if (!policy.HasValue)
                return;

            var sessionsDeleted = sessions.DeleteExpired(policy.Value.CustomerCode, policy.Value.RetentionDays, utcNow);
            var exportsDeleted = exports.DeleteExpired(policy.Value.CustomerCode, policy.Value.RetentionDays, utcNow);
            if (sessionsDeleted > 0 || exportsDeleted > 0)
            {
                Debug.Log(
                    $"Applied local retention for customer '{policy.Value.CustomerCode}': " +
                    $"deleted {sessionsDeleted} sessions and {exportsDeleted} exports.");
            }
        }

        private static RetentionPolicy? ResolveRetentionPolicy(
            CustomerPackageStore packageStore,
            string bundledCustomerCode,
            int bundledRetentionDays)
        {
            if (packageStore != null)
            {
                if (packageStore.TryLoadActive(out var snapshot, out var error))
                {
                    var customerCode = CustomerStorageScope.Normalize(snapshot.Configuration.CustomerCode);
                    var retentionDays = snapshot.Configuration.RetentionDays;
                    if (customerCode.Length > 0 && IsValidRetention(retentionDays))
                        return new RetentionPolicy(customerCode, retentionDays);

                    Debug.LogWarning("Active customer package retention is invalid; using bundled fallback.");
                }
                else if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogWarning("Active customer package retention is invalid; using bundled fallback: " + error);
                }
            }

            bundledCustomerCode = CustomerStorageScope.Normalize(bundledCustomerCode);
            if (bundledCustomerCode.Length > 0 && IsValidRetention(bundledRetentionDays))
                return new RetentionPolicy(bundledCustomerCode, bundledRetentionDays);

            Debug.LogWarning("Local retention was skipped because no valid customer-scoped retention policy is available.");
            return null;
        }

        private static bool IsValidRetention(int retentionDays)
        {
            return retentionDays >= 1 && retentionDays <= MaximumRetentionDays;
        }
    }
}
