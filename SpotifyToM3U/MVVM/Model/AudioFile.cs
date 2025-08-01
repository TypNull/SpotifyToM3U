using CommunityToolkit.Mvvm.ComponentModel;
using NLog;
using SpotifyToM3U.Core;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

                _logger.Trace($"Successfully loaded audio file: {location} - Title: {_title}, Artist: {string.Join(", ", _artists)}");
            }
            catch (TagLib.UnsupportedFormatException ex)
            {
                _logger.Debug($"Unsupported audio format: {location} - {ex.Message}");
            }
            catch (TagLib.CorruptFileException ex)
            {
                _logger.Warn($"Corrupt audio file detected: {location} - {ex.Message}");
            }
            catch (System.Exception ex)
            {
                _logger.Error(ex, $"Error loading audio file: {location}");
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

        [XmlIgnore]
        public ConcurrentDictionary<Track, double> TrackValueDictionary { get; } = new();
    }
}
