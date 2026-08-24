using System;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    public static class LocalRetentionEnforcer
    {
        private const int MaximumRetentionDays = 365;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyRetention()
        {
            try
            {
                var config = Resources.Load<AppConfig>("AthmarVisionCountConfig");
                ApplyRetentionPolicy(
                    new CustomerPackageStore(),
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
                ApplyRetentionPolicy(
                    null,
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
            int bundledRetentionDays,
            LocalScanRepository sessions,
            ExportFileService exports,
            DateTime utcNow)
        {
            if (sessions == null)
                throw new ArgumentNullException(nameof(sessions));
            if (exports == null)
                throw new ArgumentNullException(nameof(exports));

            var retentionDays = ResolveRetentionDays(packageStore, bundledRetentionDays);
            if (retentionDays == 0)
                return;

            var sessionsDeleted = sessions.DeleteExpired(retentionDays, utcNow);
            var exportsDeleted = exports.DeleteExpired(retentionDays, utcNow);
            if (sessionsDeleted > 0 || exportsDeleted > 0)
                Debug.Log($"Applied local retention: deleted {sessionsDeleted} sessions and {exportsDeleted} exports.");
        }

        private static int ResolveRetentionDays(CustomerPackageStore packageStore, int bundledRetentionDays)
        {
            if (packageStore != null)
            {
                if (packageStore.TryLoadActive(out var snapshot, out var error))
                    return snapshot.Configuration.RetentionDays;
                if (!string.IsNullOrEmpty(error))
                    Debug.LogWarning("Active customer package retention is invalid; using bundled fallback: " + error);
            }

            if (bundledRetentionDays >= 1 && bundledRetentionDays <= MaximumRetentionDays)
                return bundledRetentionDays;

            Debug.LogWarning("Local retention was skipped because no valid retention policy is available.");
            return 0;
        }
    }
}
