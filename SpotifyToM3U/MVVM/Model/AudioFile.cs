using CommunityToolkit.Mvvm.ComponentModel;
using NLog;
using SpotifyToM3U.Core;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace SpotifyToM3U.MVVM.Model
{
    public partial class AudioFile : ObservableObject
    {
        private static readonly Logger _logger = SpotifyToM3ULogger.GetLogger(typeof(AudioFile));

        public AudioFile() { }

        public AudioFile(string location)
        {
            Location = location;
            Tags = [];

            try
            {
                TagLib.File file = TagLib.File.Create(Location);
                _title = file.Tag.Title;
                _cutTitle = IOManager.CutString(file.Tag.Title);
                _artists = file.Tag.Performers.Length > 0 ? file.Tag.Performers : new string[] { "" };
                _cutArtists = _artists.Select(IOManager.CutString).ToArray();
                _album = file.Tag.Album;
                _genre = file.Tag.Genres.Length > 0 ? file.Tag.Genres[0] : "";
                _year = file.Tag.Year;
                int index = location.LastIndexOf('.');
                if (index != -1)
                {
                    Extension = location.Substring(index + 1).ToUpper();
                }

                // Extract path-based metadata as fallback for missing ID3 tags
                ExtractPathBasedMetadata(location);

                _logger.Trace($"Successfully loaded audio file: {location} - Title: {_title}, Artist: {string.Join(", ", _artists)}, PathArtist: {_pathDerivedArtist}, PathAlbum: {_pathDerivedAlbum}");
            }
            catch (TagLib.UnsupportedFormatException ex)
            {
                _logger.Debug($"Unsupported audio format: {location} - {ex.Message}");
                // Still try to extract path-based metadata for unsupported formats
                ExtractPathBasedMetadata(location);
            }
            catch (TagLib.CorruptFileException ex)
            {
                _logger.Warn($"Corrupt audio file detected: {location} - {ex.Message}");
                // Still try to extract path-based metadata for corrupt files
                ExtractPathBasedMetadata(location);
            }
            catch (System.Exception ex)
            {
                _logger.Error(ex, $"Error loading audio file: {location}");
                // Still try to extract path-based metadata on error
                ExtractPathBasedMetadata(location);
            }
        }

        /// <summary>
        /// Extracts metadata from the file path structure.
        /// Supports folder structures like: Artist/Album/## Title.ext or Artist/Album/Title.ext
        /// This data is used as fallback when ID3 tags are missing.
        /// </summary>
        private void ExtractPathBasedMetadata(string location)
        {
            try
            {
                string? directory = Path.GetDirectoryName(location);
                string filename = Path.GetFileNameWithoutExtension(location);

                // Extract title from filename (handles "## Title" or "## - Title" patterns)
                _pathDerivedTitle = ExtractTitleFromFilename(filename);

                if (!string.IsNullOrEmpty(directory))
                {
                    // Get immediate parent folder (typically Album)
                    string? albumFolder = Path.GetFileName(directory);
                    if (!string.IsNullOrEmpty(albumFolder))
                    {
                        _pathDerivedAlbum = albumFolder;
                    }

                    // Get grandparent folder (typically Artist)
                    string? parentDirectory = Path.GetDirectoryName(directory);
                    if (!string.IsNullOrEmpty(parentDirectory))
                    {
                        string? artistFolder = Path.GetFileName(parentDirectory);
                        if (!string.IsNullOrEmpty(artistFolder))
                        {
                            _pathDerivedArtist = artistFolder;
                        }
                    }
                }

                // Apply fallbacks: use path-derived values when ID3 tags are missing
                ApplyPathBasedFallbacks();

                if (!string.IsNullOrEmpty(_pathDerivedArtist) || !string.IsNullOrEmpty(_pathDerivedAlbum))
                {
                    _logger.Debug($"Extracted path metadata for {filename}: Artist='{_pathDerivedArtist}', Album='{_pathDerivedAlbum}', Title='{_pathDerivedTitle}'");
                }
            }
            catch (System.Exception ex)
            {
                _logger.Debug($"Could not extract path-based metadata from {location}: {ex.Message}");
            }
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

            // If no track number pattern, return the filename as-is (might be "Artist - Title" format)
            return filename.Trim();
        }

        /// <summary>
        /// Applies path-derived metadata as fallbacks when ID3 tags are missing or empty.
        /// </summary>
        private void ApplyPathBasedFallbacks()
        {
            // Fallback for missing title
            if (string.IsNullOrWhiteSpace(_title) && !string.IsNullOrWhiteSpace(_pathDerivedTitle))
            {
                _title = _pathDerivedTitle;
                _cutTitle = IOManager.CutString(_pathDerivedTitle);
                _logger.Debug($"Using path-derived title: {_title}");
            }

            // Fallback for missing artist
            bool hasNoArtist = _artists == null || _artists.Length == 0 ||
                              (_artists.Length == 1 && string.IsNullOrWhiteSpace(_artists[0]));
            if (hasNoArtist && !string.IsNullOrWhiteSpace(_pathDerivedArtist))
            {
                _artists = new[] { _pathDerivedArtist };
                _cutArtists = new[] { IOManager.CutString(_pathDerivedArtist) };
                _logger.Debug($"Using path-derived artist: {_pathDerivedArtist}");
            }

            // Fallback for missing album
            if (string.IsNullOrWhiteSpace(_album) && !string.IsNullOrWhiteSpace(_pathDerivedAlbum))
            {
                _album = _pathDerivedAlbum;
                _logger.Debug($"Using path-derived album: {_pathDerivedAlbum}");
            }
        }



        public string Extension { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public List<int> Tags { get; set; } = new();

        [ObservableProperty]
        private string _title = string.Empty;
        [ObservableProperty]
        private string _cutTitle = string.Empty;
        [ObservableProperty]
        private string[] _artists = new string[] { "" };
        [ObservableProperty]
        private string[] _cutArtists = new string[] { "" };
        [ObservableProperty]
        private string _genre = string.Empty;
        [ObservableProperty]
        private uint _year;
        [ObservableProperty]
        private string _album = string.Empty;

        // Path-derived metadata (extracted from folder structure as fallback)
        // These are stored separately to preserve the source of the data
        [ObservableProperty]
        private string _pathDerivedArtist = string.Empty;
        [ObservableProperty]
        private string _pathDerivedAlbum = string.Empty;
        [ObservableProperty]
        private string _pathDerivedTitle = string.Empty;

        [XmlIgnore]
        public ConcurrentDictionary<Track, double> TrackValueDictionary { get; } = new();
    }
}
