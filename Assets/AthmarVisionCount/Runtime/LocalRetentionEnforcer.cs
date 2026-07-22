using System;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    public static class LocalRetentionEnforcer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyRetention()
        {
            var config = Resources.Load<AppConfig>("AthmarVisionCountConfig");
            if (config == null || config.RetentionDays < 1)
                return;

            try
            {
                var sessionsDeleted = new LocalScanRepository().DeleteExpired(config.RetentionDays);
                var exportsDeleted = new ExportFileService().DeleteExpired(config.RetentionDays);
                if (sessionsDeleted > 0 || exportsDeleted > 0)
                    Debug.Log($"Applied local retention: deleted {sessionsDeleted} sessions and {exportsDeleted} exports.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unable to apply local retention: " + exception.Message);
            }
        }
    }
}
