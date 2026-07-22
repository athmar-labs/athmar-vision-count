using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    public sealed class LocalScanRepository
    {
        private readonly string _rootDirectory;

        public LocalScanRepository(string rootDirectory = null)
        {
            _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(Application.persistentDataPath, "confirmed-scans")
                : rootDirectory;
        }

        public string Save(ScanSessionRecord session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (!session.Confirmed)
                throw new InvalidOperationException("Unconfirmed sessions must not be persisted as final inventory records.");
            if (string.IsNullOrWhiteSpace(session.SessionId))
                throw new InvalidOperationException("Session ID is required.");

            Directory.CreateDirectory(_rootDirectory);
            var path = Path.Combine(_rootDirectory, SanitizeFileName(session.SessionId) + ".json");
            var temporaryPath = path + ".tmp";
            var json = JsonUtility.ToJson(session, true);
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));

            if (File.Exists(path))
                File.Delete(path);
            File.Move(temporaryPath, path);
            return path;
        }

        public IReadOnlyList<ScanSessionRecord> LoadAll()
        {
            var sessions = new List<ScanSessionRecord>();
            if (!Directory.Exists(_rootDirectory))
                return sessions;

            foreach (var path in Directory.GetFiles(_rootDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var session = JsonUtility.FromJson<ScanSessionRecord>(json);
                    if (session != null && session.Confirmed)
                        sessions.Add(session);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Skipped unreadable scan file '{path}': {exception.Message}");
                }
            }

            sessions.Sort((left, right) => string.CompareOrdinal(right.CompletedAtUtc, left.CompletedAtUtc));
            return sessions;
        }

        public void DeleteAllLocalData()
        {
            if (Directory.Exists(_rootDirectory))
                Directory.Delete(_rootDirectory, true);
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
                value = value.Replace(invalidCharacter, '_');
            return value;
        }
    }
}
