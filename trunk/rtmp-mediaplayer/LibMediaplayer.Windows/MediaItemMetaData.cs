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
        public object Cover = null; // cast it yourself to the right class when set
        public string AlbumTitle = "";
        public List<string> AlbumComposers = new List<string>();
        public List<string> AlbumPerformers = new List<string>();

        // These are filled when MetaData is a track
        public string AlbumTrackID = "";
        public string TrackTitle = "";
        public List<string> TrackComposers = new List<string>();
        public List<string> TrackPerformers = new List<string>();
        public long TrackDuration = -1;

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
