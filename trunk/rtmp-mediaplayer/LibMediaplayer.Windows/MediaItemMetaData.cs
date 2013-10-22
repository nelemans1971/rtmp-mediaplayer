#region License
// Copyright (c) 2013 Stichting Centrale Discotheek Rotterdam. All rights reserved.
// 
// website: http://www.muziekweb.nl
// e-mail:  info@cdr.nl
// 
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the
// Free Software Foundation; either version 2 of the License, or 
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful, but 
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
// for more details.
// 
// You should have received a copy of the GNU General Public License along
// with this program; if not, write to the Free Software Foundation, Inc.,
// 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA. or 
// visit www.gnu.org.
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CDR.LibRTMP.Media
{
    public class MediaItemMetaData
    {
        public MetaDataPresentation Presentation = MetaDataPresentation.Album; // what for kind of presentation does the metadata represent
        public MetaDataGenre MetaDataGenre = MetaDataGenre.Unknown;

        public string AlbumID = "";
        public string CoverURL = "";
        public object Cover = null; // cast it yourself to the right class when set
        public DateTime ReleaseDate = DateTime.MinValue;
        public string AlbumTitle = "";
        public List<string> AlbumComposers = new List<string>();
        public List<string> AlbumPerformers = new List<string>();
        public List<string> AlbumPerformersRole = new List<string>();
        public long AlbumDuration = -1;
        public string Media = ""; // eg 1 compact disc
        public string Orderinfo = "";
        public float AlbumRating = -1.0f;
        public string AlbumReview = "";

        // These are filled when MetaData is a track
        public string AlbumTrackID = "";
        public int TrackNumber = -1;
        public int TrackNumberCount = -1;
        public string TrackTitle = "";
        public List<string> TrackComposers = new List<string>();
        public List<string> TrackPerformers = new List<string>();
        public List<string> TrackPerformersRole = new List<string>();
        public long TrackDuration = -1; // in milliseconds

        // Store here data which doesn't fit in the above structure
        public object ExtraData1 = null;
        public object ExtraData2 = null;
        public object ExtraData3 = null;
        public object ExtraData4 = null;

        public string CoverPICO
        {
            get
            {
                return CoverURL;
            }
        }

        public string CoverSMALL
        {
            get
            {
                return CoverURL.Replace("PICO", "SMALL");
            }
        }

        public string CoverMEDIUM
        {
            get
            {
                return CoverURL.Replace("PICO", "MEDIUM");
            }
        }

        /// <summary>
        /// Only available inside Muziekwebplein
        /// </summary>
        public string CoverLARGE
        {
            get
            {
                return CoverURL.Replace("PICO", "LARGE");
            }
        }

        /// <summary>
        /// Only available inside Muziekwebplein
        /// </summary>
        public string CoverSUPERLARGE
        {
            get
            {
                return CoverURL.Replace("PICO", "SUPERLARGE");
            }
        }

        /// <summary>
        /// Only available inside Muziekwebplein
        /// </summary>
        public string CoverORG
        {
            get
            {
                return CoverURL.Replace("PICO", "ORG");
            }
        }

        // define static function which will create a default class filled
    }

    public enum MetaDataGenre
    {
        Unknown = 0,
        Popular,
        Classical
    }

    public enum MetaDataPresentation
    {
        Album = 0,
        Track
    }
}
