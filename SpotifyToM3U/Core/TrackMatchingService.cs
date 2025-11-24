using NLog;
using SpotifyToM3U.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpotifyToM3U.Core
{
    public class TrackMatchResult
    {
        public AudioFile AudioFile { get; set; } = null!;
        public double Confidence { get; set; }
        public string MatchType { get; set; } = string.Empty;
        public Dictionary<string, double> ScoreBreakdown { get; set; } = new();
    }

    public static class ComprehensiveTrackMatcher
    {
        private static readonly Logger _logger = SpotifyToM3ULogger.GetLogger(typeof(ComprehensiveTrackMatcher));

        private static readonly Dictionary<string, string[]> CommonWords = new()
        {
            ["noise"] = new[] { "official", "video", "audio", "lyric", "hd", "hq", "version", "edit", "extended", "radio" },
            ["featuring"] = new[] { "feat", "ft", "featuring", "with", "vs", "x", "&" },
            ["remix"] = new[] { "remix", "mix", "rework", "edit", "version", "vip" },
            ["remaster"] = new[] { "remaster", "remastered", "anniversary", "deluxe", "special", "edition" }
        };

        /// <summary>
        /// Find the best matching local track for a Spotify track
        /// </summary>
        public static TrackMatchResult? FindBestMatch(Track spotifyTrack, IEnumerable<AudioFile> audioFiles)
        {
            _logger.Debug($"Searching for best match for track: {spotifyTrack.Title} by {spotifyTrack.Artists}");

            List<TrackMatchResult> allMatches = FindAllMatches(spotifyTrack, audioFiles);
            TrackMatchResult? bestMatch = allMatches.FirstOrDefault();

            if (bestMatch != null)
            {
                _logger.Info($"Found match for '{spotifyTrack.Title}': {bestMatch.AudioFile.Title} (Confidence: {bestMatch.Confidence:P1})");
            }
            else
            {
                _logger.Debug($"No suitable match found for track: {spotifyTrack.Title}");
            }

            return bestMatch;
        }

        /// <summary>
        /// Find all potential matches sorted by confidence
        /// </summary>
        public static List<TrackMatchResult> FindAllMatches(Track spotifyTrack, IEnumerable<AudioFile> audioFiles)
        {
            List<TrackMatchResult> matches = new();

            foreach (AudioFile audioFile in audioFiles)
            {
                TrackMatchResult result = CalculateMatch(spotifyTrack, audioFile);
                if (result.Confidence >= 0.6) // Minimum threshold
                {
                    matches.Add(result);
                }
            }

            return matches.OrderByDescending(m => m.Confidence).ToList();
        }

        /// <summary>
        /// Calculate comprehensive match score between Spotify track and local file
        /// </summary>
        private static TrackMatchResult CalculateMatch(Track spotifyTrack, AudioFile audioFile)
        {
            TrackMatchResult result = new()
            {
                AudioFile = audioFile,
                ScoreBreakdown = new Dictionary<string, double>()
            };

            // Clean track titles for comparison
            string spotifyTitle = CleanTitle(spotifyTrack.Title);
            string audioTitle = CleanTitle(audioFile.Title);

            // 1. Title Matching (40% weight)
            double titleScore = CalculateTitleSimilarity(spotifyTitle, audioTitle);
            result.ScoreBreakdown["Title"] = titleScore;

            // 2. Artist Matching (35% weight)
            double artistScore = CalculateArtistSimilarity(spotifyTrack, audioFile);
            result.ScoreBreakdown["Artist"] = artistScore;

            // 3. Album Matching (15% weight) - bonus points
            // Now also considers path-derived album from folder structure
            double albumScore = CalculateAlbumSimilarity(spotifyTrack.Album?.Name, audioFile);
            result.ScoreBreakdown["Album"] = albumScore;

            // 4. Filename Matching (10% weight) - fallback
            double filenameScore = CalculateFilenameSimilarity(spotifyTrack, audioFile);
            result.ScoreBreakdown["Filename"] = filenameScore;

            // Calculate weighted total
            double weightedScore = (titleScore * 0.4) + (artistScore * 0.35) + (albumScore * 0.15) + (filenameScore * 0.1);

            // Apply bonuses and penalties
            double finalScore = ApplyMatchingBonuses(weightedScore, spotifyTrack, audioFile, result.ScoreBreakdown);

            result.Confidence = Math.Min(1.0, finalScore);
            result.MatchType = DetermineMatchType(result.ScoreBreakdown);

            return result;
        }

        /// <summary>
        /// Calculate title similarity with various strategies
        /// </summary>
        private static double CalculateTitleSimilarity(string spotifyTitle, string audioTitle)
        {
            if (string.IsNullOrEmpty(spotifyTitle) || string.IsNullOrEmpty(audioTitle))
                return 0.0;

            // Exact match
            if (spotifyTitle.Equals(audioTitle, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            // Remove common noise words and try again
            string cleanSpotify = RemoveNoiseWords(spotifyTitle);
            string cleanAudio = RemoveNoiseWords(audioTitle);

            if (cleanSpotify.Equals(cleanAudio, StringComparison.OrdinalIgnoreCase))
                return 0.95;

            // Fuzzy matching
            double fuzzyScore = CalculateFuzzyMatch(cleanSpotify, cleanAudio);

            // Check if one title contains the other (after cleaning)
            if (cleanSpotify.Contains(cleanAudio, StringComparison.OrdinalIgnoreCase) ||
                cleanAudio.Contains(cleanSpotify, StringComparison.OrdinalIgnoreCase))
            {
                fuzzyScore = Math.Max(fuzzyScore, 0.85);
            }

            // Word order independence check
            double wordOrderScore = CalculateWordOrderIndependentMatch(cleanSpotify, cleanAudio);

            return Math.Max(fuzzyScore, wordOrderScore);
        }

        /// <summary>
        /// Calculate artist similarity handling multiple artists and featuring
        /// </summary>
        private static double CalculateArtistSimilarity(Track spotifyTrack, AudioFile audioFile)
        {
            List<string> spotifyArtists = ExtractAllArtists(spotifyTrack);
            List<string> audioArtists = ExtractAllArtists(audioFile);

            if (!spotifyArtists.Any() || !audioArtists.Any())
                return 0.5; // Neutral when artist info is missing

            double maxScore = 0.0;

            // Try all combinations of artists
            foreach (string spotifyArtist in spotifyArtists)
            {
                foreach (string audioArtist in audioArtists)
                {
                    double score = CalculateFuzzyMatch(spotifyArtist, audioArtist);
                    maxScore = Math.Max(maxScore, score);

                    // Exact match bonus
                    if (spotifyArtist.Equals(audioArtist, StringComparison.OrdinalIgnoreCase))
                        return 1.0;
                }
            }

            // Check for partial artist matches (for collaborations)
            double partialScore = CalculatePartialArtistMatch(spotifyArtists, audioArtists);
            return Math.Max(maxScore, partialScore);
        }

        /// <summary>
        /// Calculate album similarity.
        /// Uses both ID3 tag album and path-derived album (from folder name).
        /// </summary>
        private static double CalculateAlbumSimilarity(string? spotifyAlbum, AudioFile audioFile)
        {
            if (string.IsNullOrEmpty(spotifyAlbum))
                return 0.5; // Neutral when Spotify album info missing

            string cleanSpotify = CleanTitle(spotifyAlbum);
            double maxScore = 0.5; // Default neutral score

            // Check ID3 tag album
            if (!string.IsNullOrEmpty(audioFile.Album))
            {
                string cleanAudio = CleanTitle(audioFile.Album);
                if (cleanSpotify.Equals(cleanAudio, StringComparison.OrdinalIgnoreCase))
                    return 1.0;
                maxScore = Math.Max(maxScore, CalculateFuzzyMatch(cleanSpotify, cleanAudio));
            }

            // Also check path-derived album (folder name)
            if (!string.IsNullOrEmpty(audioFile.PathDerivedAlbum))
            {
                string cleanPathAlbum = CleanTitle(audioFile.PathDerivedAlbum);
                if (cleanSpotify.Equals(cleanPathAlbum, StringComparison.OrdinalIgnoreCase))
                    return 1.0;
                double pathAlbumScore = CalculateFuzzyMatch(cleanSpotify, cleanPathAlbum);
                if (pathAlbumScore > maxScore)
                {
                    _logger.Debug($"Path-derived album '{audioFile.PathDerivedAlbum}' matched better than ID3 album");
                    maxScore = pathAlbumScore;
                }
            }

            return maxScore;
        }

        /// <summary>
        /// Calculate filename-based similarity as fallback.
        /// Supports multiple filename patterns:
        /// - "Artist - Title" (traditional)
        /// - "## Title" or "## - Title" (track number prefix, used with folder-based artist)
        /// </summary>
        private static double CalculateFilenameSimilarity(Track spotifyTrack, AudioFile audioFile)
        {
            string filename = System.IO.Path.GetFileNameWithoutExtension(audioFile.Location);
            double maxScore = 0.0;

            // Pattern 1: Traditional "Artist - Title" format
            string expectedPattern = $"{spotifyTrack.Artists?.FirstOrDefault()?.Name} - {spotifyTrack.Title}";
            string cleanFilename = CleanTitle(filename);
            string cleanExpected = CleanTitle(expectedPattern);
            maxScore = Math.Max(maxScore, CalculateFuzzyMatch(cleanFilename, cleanExpected));

            // Pattern 2: Track number prefix "## Title" - extract title and compare
            string extractedTitle = ExtractTitleFromFilename(filename);
            if (!string.IsNullOrEmpty(extractedTitle))
            {
                string cleanExtractedTitle = CleanTitle(extractedTitle);
                string cleanSpotifyTitle = CleanTitle(spotifyTrack.Title);
                double titleOnlyScore = CalculateFuzzyMatch(cleanExtractedTitle, cleanSpotifyTitle);
                maxScore = Math.Max(maxScore, titleOnlyScore);
            }

            // Pattern 3: Path-based matching - combine folder artist with filename title
            if (!string.IsNullOrEmpty(audioFile.PathDerivedArtist))
            {
                string pathBasedPattern = $"{audioFile.PathDerivedArtist} - {extractedTitle}";
                string cleanPathBased = CleanTitle(pathBasedPattern);
                double pathScore = CalculateFuzzyMatch(cleanPathBased, cleanExpected);
                maxScore = Math.Max(maxScore, pathScore);
            }

            return maxScore;
        }

        /// <summary>
        /// Extracts the title from a filename, removing track numbers and common prefixes.
        /// Handles patterns like: "01 Title", "01 - Title", "01. Title", "1 - Title"
        /// </summary>
        private static string ExtractTitleFromFilename(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return string.Empty;

            // Pattern to match track numbers at the start: "01 ", "01 - ", "01. ", "1 - ", etc.
            Match match = Regex.Match(filename, @"^\d{1,3}[\s.\-_]+(.+)$");
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            // If no track number pattern, return the filename as-is
            return filename.Trim();
        }

        /// <summary>
        /// Apply bonuses and penalties based on match quality
        /// </summary>
        private static double ApplyMatchingBonuses(double baseScore, Track spotifyTrack, AudioFile audioFile, Dictionary<string, double> breakdown)
        {
            double finalScore = baseScore;

            // Perfect title + artist match
            if (breakdown["Title"] > 0.95 && breakdown["Artist"] > 0.95)
            {
                finalScore += 0.1; // 10% bonus
            }

            // Album confirmation bonus
            if (breakdown["Album"] > 0.9)
            {
                finalScore += 0.05; // 5% bonus
            }

            // Multi-artist penalty (harder to match)
            if (spotifyTrack.Artists?.Length > 2)
            {
                finalScore -= 0.02; // Small penalty for complex collaborations
            }

            // File format quality bonus (prefer lossless)
            string extension = System.IO.Path.GetExtension(audioFile.Location).ToLower();
            if (extension == ".flac" || extension == ".wav")
            {
                finalScore += 0.01; // Tiny bonus for high quality
            }

            return finalScore;
        }

        /// <summary>
        /// Clean title by removing noise words and normalizing
        /// </summary>
        private static string CleanTitle(string? title)
        {
            if (string.IsNullOrEmpty(title))
                return string.Empty;

            string cleaned = title.ToLower().Trim();

            // Remove content in parentheses/brackets
            cleaned = Regex.Replace(cleaned, @"\s*[\(\[\{].*?[\)\]\}]\s*", " ");

            // Remove punctuation except hyphens and apostrophes
            cleaned = Regex.Replace(cleaned, @"[^\w\s\-']", " ");

            // Normalize whitespace
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }

        /// <summary>
        /// Remove common noise words from titles
        /// </summary>
        private static string RemoveNoiseWords(string title)
        {
            string cleaned = title;

            foreach (string noiseWord in CommonWords["noise"])
            {
                cleaned = Regex.Replace(cleaned, $@"\b{noiseWord}\b", "", RegexOptions.IgnoreCase);
            }

            // Remove featuring mentions
            foreach (string featWord in CommonWords["featuring"])
            {
                cleaned = Regex.Replace(cleaned, $@"\b{featWord}\b.*$", "", RegexOptions.IgnoreCase);
            }

            return Regex.Replace(cleaned, @"\s+", " ").Trim();
        }

        /// <summary>
        /// Extract all artists including featuring artists
        /// </summary>
        private static List<string> ExtractAllArtists(Track spotifyTrack)
        {
            List<string> artists = new();

            if (spotifyTrack.Artists != null)
            {
                artists.AddRange(spotifyTrack.Artists.Select(a => CleanTitle(a.Name)));
            }

            return artists.Where(a => !string.IsNullOrEmpty(a)).ToList();
        }

        /// <summary>
        /// Extract all artists from audio file.
        /// Uses multiple sources in order of priority:
        /// 1. ID3 tag artists (from TagLib)
        /// 2. Path-derived artist (from folder structure)
        /// 3. Filename pattern "Artist - Title"
        /// </summary>
        private static List<string> ExtractAllArtists(AudioFile audioFile)
        {
            List<string> artists = new();

            // Source 1: ID3 tag artists
            if (audioFile.Artists != null)
            {
                artists.AddRange(audioFile.Artists.Select(CleanTitle));
            }

            // Source 2: Path-derived artist (from folder structure like Artist/Album/Track.mp3)
            if (!artists.Any(a => !string.IsNullOrEmpty(a)) && !string.IsNullOrEmpty(audioFile.PathDerivedArtist))
            {
                artists.Add(CleanTitle(audioFile.PathDerivedArtist));
                _logger.Debug($"Using path-derived artist for matching: {audioFile.PathDerivedArtist}");
            }

            // Source 3: Extract from filename pattern "Artist - Title"
            if (!artists.Any(a => !string.IsNullOrEmpty(a)))
            {
                string filename = System.IO.Path.GetFileNameWithoutExtension(audioFile.Location);
                Match match = Regex.Match(filename, @"^(.+?)\s*-\s*(.+)$");
                if (match.Success)
                {
                    artists.Add(CleanTitle(match.Groups[1].Value));
                }
            }

            return artists.Where(a => !string.IsNullOrEmpty(a)).ToList();
        }

        /// <summary>
        /// Calculate fuzzy string similarity using Jaro-Winkler algorithm
        /// </summary>
        private static double CalculateFuzzyMatch(string str1, string str2)
        {
            if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
                return 0.0;

            // For very short strings, use exact matching
            if (str1.Length < 3 || str2.Length < 3)
                return str1.Equals(str2, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;

            // Use Levenshtein distance normalized
            int distance = LevenshteinDistance(str1, str2);
            int maxLength = Math.Max(str1.Length, str2.Length);
            double similarity = 1.0 - (double)distance / maxLength;

            // Additional boost for common prefixes
            int commonPrefix = GetCommonPrefixLength(str1, str2);
            if (commonPrefix > 2)
            {
                similarity += 0.1 * (commonPrefix / (double)Math.Min(str1.Length, str2.Length));
            }

            return Math.Min(1.0, similarity);
        }

        /// <summary>
        /// Calculate word order independent matching
        /// </summary>
        private static double CalculateWordOrderIndependentMatch(string str1, string str2)
        {
            string[] words1 = str1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] words2 = str2.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (!words1.Any() || !words2.Any())
                return 0.0;

            int matchedWords = 0;
            HashSet<string> usedWords = new();

            foreach (string word1 in words1)
            {
                foreach (string word2 in words2)
                {
                    if (!usedWords.Contains(word2) &&
                        word1.Equals(word2, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedWords++;
                        usedWords.Add(word2);
                        break;
                    }
                }
            }

            return (double)matchedWords / Math.Max(words1.Length, words2.Length);
        }

        /// <summary>
        /// Calculate partial artist matching for collaborations
        /// </summary>
        private static double CalculatePartialArtistMatch(List<string> spotifyArtists, List<string> audioArtists)
        {
            int matchCount = 0;
            int totalArtists = Math.Max(spotifyArtists.Count, audioArtists.Count);

            foreach (string spotifyArtist in spotifyArtists)
            {
                if (audioArtists.Any(audioArtist =>
                    CalculateFuzzyMatch(spotifyArtist, audioArtist) > 0.8))
                {
                    matchCount++;
                }
            }

            return (double)matchCount / totalArtists;
        }

        /// <summary>
        /// Determine match type based on score breakdown
        /// </summary>
        private static string DetermineMatchType(Dictionary<string, double> breakdown)
        {
            if (breakdown["Title"] > 0.95 && breakdown["Artist"] > 0.95)
                return "Exact Match";

            if (breakdown["Title"] > 0.9 && breakdown["Artist"] > 0.8)
                return "High Confidence";

            if (breakdown["Album"] > 0.9)
                return "Album Confirmed";

            if (breakdown["Filename"] > breakdown["Title"])
                return "Filename Match";

            // Check if path-derived data contributed significantly
            if (breakdown.ContainsKey("PathMatch") && breakdown["PathMatch"] > 0.8)
                return "Path Match";

            return "Fuzzy Match";
        }

        /// <summary>
        /// Calculate Levenshtein distance
        /// </summary>
        private static int LevenshteinDistance(string s1, string s2)
        {
            int len1 = s1.Length;
            int len2 = s2.Length;
            int[,] matrix = new int[len1 + 1, len2 + 1];

            for (int i = 0; i <= len1; i++)
                matrix[i, 0] = i;

            for (int j = 0; j <= len2; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= len1; i++)
            {
                for (int j = 1; j <= len2; j++)
                {
                    int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[len1, len2];
        }

        /// <summary>
        /// Get common prefix length
        /// </summary>
        private static int GetCommonPrefixLength(string str1, string str2)
        {
            int minLength = Math.Min(str1.Length, str2.Length);
            for (int i = 0; i < minLength; i++)
            {
                if (char.ToLower(str1[i]) != char.ToLower(str2[i]))
                    return i;
            }
            return minLength;
        }
    }
}