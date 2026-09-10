using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    public sealed class ExportFileService
    {
        private readonly string _rootDirectory;

        public ExportFileService(string rootDirectory = null)
        {
            _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(Application.persistentDataPath, "exports")
                : rootDirectory;
        }

        public string SaveConfirmedSession(ScanSessionRecord session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (!session.Confirmed)
                throw new InvalidOperationException("Only a confirmed session can be exported.");

            session.CustomerCode = CustomerStorageScope.ResolveOrRequire(session.CustomerCode);
            Directory.CreateDirectory(_rootDirectory);
            var fileName = CustomerStorageScope.StorageKey(session.CustomerCode) + "--" + SanitizeFileName(session.SessionId) + ".csv";
            var path = Path.Combine(_rootDirectory, fileName);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, CsvExportService.BuildConfirmedSessionCsv(session), new UTF8Encoding(true));

            if (File.Exists(path))
                File.Delete(path);
            File.Move(temporaryPath, path);
            return path;
        }

        public IReadOnlyList<string> ListExports()
        {
            if (!Directory.Exists(_rootDirectory))
                return Array.Empty<string>();

            var files = Directory.GetFiles(_rootDirectory, "*.csv", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            Array.Reverse(files);
            return files;
        }

        public int DeleteExpired(string customerCode, int retentionDays, DateTime? utcNow = null)
        {
            customerCode = CustomerStorageScope.Require(customerCode);
            if (retentionDays < 1)
                throw new ArgumentOutOfRangeException(nameof(retentionDays));
            if (!Directory.Exists(_rootDirectory))
                return 0;

            var prefix = CustomerStorageScope.StorageKey(customerCode) + "--";
            var cutoff = (utcNow ?? DateTime.UtcNow).ToUniversalTime().AddDays(-retentionDays);
            var deleted = 0;
            foreach (var path in Directory.GetFiles(_rootDirectory, "*.csv", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (!Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal))
                        continue;
                    if (File.GetLastWriteTimeUtc(path) > cutoff)
                        continue;
                    File.Delete(path);
                    deleted++;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Unable to evaluate export retention for '{path}': {exception.Message}");
                }
            }

            return deleted;
        }

        public void DeleteAll()
        {
            if (Directory.Exists(_rootDirectory))
                Directory.Delete(_rootDirectory, true);
        }

        private static string SanitizeFileName(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value;
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
                value = value.Replace(invalidCharacter, '_');
            return value;
        }
    }
}
