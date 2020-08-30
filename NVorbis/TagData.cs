using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NVorbis.Contracts;

namespace NVorbis
{
    internal class TagData : ITagData
    {
        private static IReadOnlyList<string> _emptyList = new List<string>();

        private Dictionary<string, IList<string>> _tags;

        public TagData(string vendor, string[] comments)
        {
            EncoderVendor = vendor;

            var tags = new Dictionary<string, IList<string>>();
            for (var i = 0; i < comments.Length; i++)
            {
                var parts = comments[i].Split('=');
                if (parts.Length == 1)
                    parts = new[] { parts[0], string.Empty };

                int bktIdx = parts[0].IndexOf('[', StringComparison.CurrentCulture);
                if (bktIdx > -1)
                {
                    parts[1] = parts[0].Substring(bktIdx + 1, parts[0].Length - bktIdx - 2)
                        .ToUpper(CultureInfo.CurrentCulture)
                        + ": "
                        + parts[1];

                    parts[0] = parts[0].Substring(0, bktIdx);
                }

                if (tags.TryGetValue(parts[0].ToUpperInvariant(), out var list))
                {
                    list.Add(parts[1]);
                }
                else
                {
                    tags.Add(parts[0].ToUpperInvariant(), new List<string> { parts[1] });
                }
            }
            _tags = tags;
        }

        public string GetTagSingle(string key, bool concatenate = false)
        {
            var values = GetTagMulti(key);
            if (values.Count > 0)
            {
                if (concatenate)
                    return string.Join(Environment.NewLine, values.ToArray());

                return values[values.Count - 1];
            }
            return string.Empty;
        }

        public IReadOnlyList<string> GetTagMulti(string key)
        {
            if (_tags.TryGetValue(key.ToUpperInvariant(), out var values))
                return (IReadOnlyList<string>)values;

            return _emptyList;
        }

        public IReadOnlyDictionary<string, IReadOnlyList<string>> All
        {
            get
            {
                return (IReadOnlyDictionary<string, IReadOnlyList<string>>)_tags;
            }
        }

        public string EncoderVendor { get; }

        public string Title => GetTagSingle("TITLE");

        public string Version => GetTagSingle("VERSION");

        public string Album => GetTagSingle("ALBUM");

        public string TrackNumber => GetTagSingle("TRACKNUMBER");

        public string Artist => GetTagSingle("ARTIST");

        public IReadOnlyList<string> Performers => GetTagMulti("PERFORMER");

        public string Copyright => GetTagSingle("COPYRIGHT");

        public string License => GetTagSingle("LICENSE");

        public string Organization => GetTagSingle("ORGANIZATION");

        public string Description => GetTagSingle("DESCRIPTION");

        public IReadOnlyList<string> Genres => GetTagMulti("GENRE");

        public IReadOnlyList<string> Dates => GetTagMulti("DATE");

        public IReadOnlyList<string> Locations => GetTagMulti("LOCATION");

        public string Contact => GetTagSingle("CONTACT");

        public string Isrc => GetTagSingle("ISRC");
    }
}