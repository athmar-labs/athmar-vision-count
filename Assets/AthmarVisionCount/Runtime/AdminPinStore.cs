using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    public sealed class AdminPinStore
    {
        private const string SaltKey = "athmar.admin.pin.salt.v1";
        private const string HashKey = "athmar.admin.pin.hash.v1";

        public bool HasPin => PlayerPrefs.HasKey(SaltKey) && PlayerPrefs.HasKey(HashKey);

        public void SetInitialPin(string pin)
        {
            if (HasPin)
                throw new InvalidOperationException("The administrator PIN has already been configured.");
            ValidatePin(pin);

            var salt = new byte[16];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(salt);
            var hash = ComputeHash(salt, pin);
            PlayerPrefs.SetString(SaltKey, Convert.ToBase64String(salt));
            PlayerPrefs.SetString(HashKey, Convert.ToBase64String(hash));
            PlayerPrefs.Save();
        }

        public bool Verify(string pin)
        {
            if (!HasPin || string.IsNullOrEmpty(pin))
                return false;

            try
            {
                var salt = Convert.FromBase64String(PlayerPrefs.GetString(SaltKey));
                var expected = Convert.FromBase64String(PlayerPrefs.GetString(HashKey));
                var actual = ComputeHash(salt, pin);
                return FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public void ChangePin(string currentPin, string newPin)
        {
            if (!Verify(currentPin))
                throw new UnauthorizedAccessException("The current administrator PIN is incorrect.");
            ValidatePin(newPin);

            PlayerPrefs.DeleteKey(SaltKey);
            PlayerPrefs.DeleteKey(HashKey);
            PlayerPrefs.Save();
            SetInitialPin(newPin);
        }

        public static void ValidatePin(string pin)
        {
            if (string.IsNullOrWhiteSpace(pin) || pin.Length < 6 || pin.Length > 12)
                throw new FormatException("Administrator PIN must contain 6 to 12 digits.");
            for (var index = 0; index < pin.Length; index++)
            {
                if (pin[index] < '0' || pin[index] > '9')
                    throw new FormatException("Administrator PIN must contain digits only.");
            }
        }

        private static byte[] ComputeHash(byte[] salt, string pin)
        {
            var pinBytes = Encoding.UTF8.GetBytes(pin);
            var payload = new byte[salt.Length + pinBytes.Length];
            Buffer.BlockCopy(salt, 0, payload, 0, salt.Length);
            Buffer.BlockCopy(pinBytes, 0, payload, salt.Length, pinBytes.Length);
            using var algorithm = SHA256.Create();
            return algorithm.ComputeHash(payload);
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            var difference = 0;
            for (var index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}
