using SecretPropertys.Attributes;

namespace SpotifyToM3U.Core
{
    /// <summary>
    /// Provides embedded test credentials for development and CI builds.
    /// Credentials are obfuscated at compile time using SecretPropertys.
    /// </summary>
    public static partial class EmbeddedSecrets
    {
        [BuildSecret("SpotifyClientIdSecret")]
        public static string SpotifyClientId { get; private set; } = string.Empty;

        [BuildSecret("SpotifyClientSecretSecret")]
        public static string SpotifyClientSecret { get; private set; } = string.Empty;

        /// <summary>
        /// Checks if embedded secrets are available in this build.
        /// </summary>
        public static bool AreAvailable => !string.IsNullOrWhiteSpace(SpotifyClientId) && !string.IsNullOrWhiteSpace(SpotifyClientSecret);
    }
}
