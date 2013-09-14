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
using System.Threading.Tasks;

namespace CDR.LibRTMP.Media
{
    /// <summary>
    /// This object stores the info for a playlist item.
    /// 
    /// The programmer can use "Tag" to store his own info for this MediaItem
    /// </summary>
    public class MediaItem : ICloneable
    {
        public Guid GUID = Guid.Empty;
        
        public string MediaFile = ""; // RMTP URL
        public long NetStreamDurationInMS = -1;
        public long MetaDurationInMS = -1; // can be set by user so when stream hasn't played yet we still no the duration
        // Name of album/track, track number artiests/composers
        public MediaItemMetaData MetaData = null;
        // user var (if cloneable it will be cloned else assigned)
        public object Tag = null;

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal bool _ShuffleDone = false; // used to support shuffleling in the playlist (internal use for class Playlist)


        public MediaItem(string mediaFile = "", MediaItemMetaData MetaData = null)
        {
            this.GUID = Guid.NewGuid();
            this.MediaFile = mediaFile;
            this.MetaData = MetaData;
        }

        public override string ToString()
        {
            if (Tag != null)
            {
                return Tag.ToString();
            }

            return MediaFile;
        }

        /// <summary>
        /// returns user duration (when set) otherwise always
        /// duration given bakc by netstream after the firsttime 
        /// it's played.
        /// </summary>
        public long DurationInMS
        {
            get
            {
                if (NetStreamDurationInMS != -1)
                {
                    return NetStreamDurationInMS;
                }

                return MetaDurationInMS;
            }
        }

        #region ICloneable interface implementation

        /// <summary>
        /// Deep clone
        /// </summary>
        public object Clone()
        {
            MediaItem clone = new MediaItem();

            clone.GUID = new Guid(GUID.ToString());
            clone.MediaFile = MediaFile;
            clone.NetStreamDurationInMS = NetStreamDurationInMS;
            clone.MetaDurationInMS = MetaDurationInMS;
            if (MetaData is ICloneable)
            {
                clone.MetaData = (MediaItemMetaData)(MetaData as ICloneable).Clone();
            }
            else
            {
                clone.MetaData = MetaData;
            }
            if (Tag is ICloneable)
            {
                clone.Tag = (Tag as ICloneable).Clone();
            }
            else
            {
                clone.Tag = Tag;
            }
            clone._ShuffleDone = _ShuffleDone;

            return clone;
        }

        #endregion
    }

}
