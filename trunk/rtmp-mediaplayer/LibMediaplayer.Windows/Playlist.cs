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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CDR.LibRTMP.Media
{
    /// <summary>
    /// Previous works as followed. When you change the CurrentMediaItem, there is
    /// a parameter which governs the addition to the previoushistorylist.
    /// In shufflemode this previoushistorylist will be used to calculate the previous
    /// MediaItem. When not in shufflemode, previous is caluculated based on the current
    /// position in the playlist and the repeatmode
    /// </summary>
    public class Playlist : ICloneable, IEnumerable
    {
        private const int MAX_MEDIAITEMS = 200;

        private static int playlistNumber = 0;

        private object lockVAR = new object();
        private string playlistName = "";
        private List<MediaItem> playlist = new List<MediaItem>(MAX_MEDIAITEMS);
        private int currentMediaItemIndex = -1;
        private int nextMediaItemIndex = -1; // mediaitem which will be played next
        private int previousMediaItemIndex = -1; // mediaitem which was really played!
        private List<int> previousHistoryList = new List<int>(MAX_MEDIAITEMS);
        private PlaylistRepeatMode repeatMode = PlaylistRepeatMode.RepeatNone;
        private bool shuffleMode = false;
        private Random randomizer = new Random();


        /// <summary>
        /// These three events will be fired if there is a change
        /// </summary>
        protected internal event PL_OnMediaItemChanged OnCurrentMediaItemChanged = null;
        protected internal event PL_OnMediaItemChanged OnPreviousMediaItemChanged = null;
        protected internal event PL_OnMediaItemChanged OnNextMediaItemChanged = null;


        public Playlist()
        {
            InitVars();
        }

        public Playlist(string playlistName)
        {
            lock (lockVAR)
            {
                InitVars();
                playlistNumber--; // restore counter (possible because we use a lock here!)
                this.playlistName = playlistName;
            }
        }

        private void InitVars()
        {
            lock (lockVAR)
            {
                playlistName = "Playlist" + playlistNumber.ToString();
                playlist.Clear();
                currentMediaItemIndex = -1;
                previousMediaItemIndex = -1;
                previousHistoryList.Clear();
                repeatMode = PlaylistRepeatMode.RepeatNone;
                shuffleMode = false;

                playlistNumber++;
            }
        }


        #region ICloneable interface implementation

        /// <summary>
        /// Deep clone, except for the MediaItem(s)
        /// </summary>
        public object Clone()
        {
            Playlist clone = new Playlist(this.playlistName);

            foreach (MediaItem item in this.playlist)
            {
                clone.playlist.Add(item);
            } //foreach
            clone.currentMediaItemIndex = this.currentMediaItemIndex;
            clone.nextMediaItemIndex  = this.nextMediaItemIndex ;
            clone.previousMediaItemIndex = this.previousMediaItemIndex;
            foreach (int itemIndex in this.previousHistoryList)
            {
                clone.previousHistoryList.Add(itemIndex);
            } //foreach
            clone.repeatMode = this.repeatMode;
            clone.shuffleMode = this.shuffleMode;
            clone.OnCurrentMediaItemChanged = this.OnCurrentMediaItemChanged;
            clone.OnPreviousMediaItemChanged = this.OnPreviousMediaItemChanged;
            clone.OnNextMediaItemChanged = this.OnNextMediaItemChanged;

            return clone;
        }

        #endregion


        #region public interface for playlist

        public string PlaylistName
        {
            get
            {
                return playlistName;
            }
        }

        /// <summary>
        /// Should always be the same as the Mediaplayer Repeatmode
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal PlaylistRepeatMode RepeatMode
        {
            get
            {
                return repeatMode;
            }
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal void ChangeRepeatMode(PlaylistRepeatMode newRepeatMode)
        {
            if (repeatMode != newRepeatMode)
            {
                int oldnextMediaItemIndex = NextMediaItemIndex;

                repeatMode = newRepeatMode;

                int newnextMediaItemIndex = NextMediaItemIndex;

                // We need to check if next mediaitem is changed!!
                if (oldnextMediaItemIndex != newnextMediaItemIndex && OnNextMediaItemChanged != null)
                {
                    OnNextMediaItemChanged(this, SafeSelectClonedMediaItem(oldnextMediaItemIndex), SafeSelectClonedMediaItem(newnextMediaItemIndex));
                }
            }
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal bool ShuffleMode
        {
            get
            {
                return shuffleMode;
            }
        }

        /// <summary>
        /// When shufflemode is changed to on, all mediaitems are set to be equally chosen.
        /// The current item is changed also, when "changeCurrentItemToo=true"
        /// 
        /// All items will eventuelly be chosen, when all items are chosen and repeatmode is on,
        /// the proccess starts agaian, else it stops.
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal void ChangeShuffleMode(bool newShuffleMode, bool changeCurrentItemToo = false)
        {
            if (shuffleMode != newShuffleMode)
            {                        
                int oldnextMediaItemIndex = NextMediaItemIndex;

                shuffleMode = newShuffleMode;

                // Reset ShuffleDone
                foreach (MediaItem item in playlist)
                {
                    item._ShuffleDone = false;
                } //foreach

                // fire calculation for nextMediaItem;
                nextMediaItemIndex = CalcShuffleMode_NextMediaItem(changeCurrentItemToo);

                if (oldnextMediaItemIndex != nextMediaItemIndex && OnNextMediaItemChanged != null)
                {
                    OnNextMediaItemChanged(this, SafeSelectClonedMediaItem(oldnextMediaItemIndex), SafeSelectClonedMediaItem(nextMediaItemIndex));
                }
            }
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal bool InsertMediaItem(int atPosition, MediaItem item)
        {
            if (atPosition < 0)
            {
                atPosition = 0;
            }

            int oldPreviousMediaItemIndex = PreviousMediaItemIndex;
            int oldCurrentMediaItemIndex = CurrentMediaItemIndex;
            int oldNextMediaItemIndex = NextMediaItemIndex;
            lock (lockVAR)
            {
                // Check if not too many tracks in playlist
                if (playlist.Count >= MAX_MEDIAITEMS)
                {
                    return false;
                }
                // Check GUID is unique. This is a requirment for the playlist to work correctly!!!
                foreach (MediaItem tmpItem in playlist)
                {
                    if (item.GUID.Equals(tmpItem.GUID))
                    {
                        return false;
                    }
                } //foreach


                // Pre checks are done, we can continue
                item._ShuffleDone = false;

                if (atPosition <= previousMediaItemIndex)
                {
                    // correct previousMediaItem index
                    previousMediaItemIndex++;
                }
                if (atPosition <= currentMediaItemIndex)
                {
                    playlist.Insert(atPosition, item);
                    currentMediaItemIndex++;
                }
                else
                {
                    if (atPosition >= playlist.Count)
                    {
                        playlist.Add(item);
                    }
                    else
                    {
                        playlist.Insert(atPosition, item);
                    }
                }

                // Maak eerste item actief
                if (!CheckIndexValidInPlaylist(currentMediaItemIndex))
                {
                    if (playlist.Count <= 0)
                    {
                        currentMediaItemIndex = -1;
                    }
                    else
                    {
                        currentMediaItemIndex = 0;
                    }
                }

                if (oldPreviousMediaItemIndex >= atPosition)
                {
                    oldPreviousMediaItemIndex++;
                }
                if (oldNextMediaItemIndex >= atPosition)
                {
                    oldNextMediaItemIndex++;
                }
                //corect historyList
                for (int i = 0; i < previousHistoryList.Count; i++)
                {
                    if (previousHistoryList[i] >= atPosition)
                    {
                        previousHistoryList[i]++;
                    }
                } //foreach

                // When going to 2 items a "next" has a new chance to become valid
                if ((shuffleMode && playlist.Count == 2) || !shuffleMode)
                {
                    nextMediaItemIndex = CalcNextMediaItem(false);
                }
                if (!shuffleMode)
                {
                    // when not in shuflemode previous will not change in subsequent calc to CalcPreviousMediaItem as long 
                    // as currentMediaItemIndex stays the same (content can off course change)
                   previousMediaItemIndex = CalcPreviousMediaItem(currentMediaItemIndex);
                }
            }
            int newPreviousMediaItemIndex = PreviousMediaItemIndex;
            int newCurrentMediaItemIndex = CurrentMediaItemIndex;
            int newNextMediaItemIndex = NextMediaItemIndex;


            // Do we need to fire an event that the previous MediaItem has changed?
            if (oldPreviousMediaItemIndex != newPreviousMediaItemIndex && OnCurrentMediaItemChanged != null)
            {
                OnCurrentMediaItemChanged(this, SafeSelectClonedMediaItem(oldPreviousMediaItemIndex), SafeSelectClonedMediaItem(newPreviousMediaItemIndex));
            }
            // Do we need to fire an event that the current MediaItem has changed?
            if (oldCurrentMediaItemIndex != newCurrentMediaItemIndex && OnCurrentMediaItemChanged != null)
            {
                OnCurrentMediaItemChanged(this, SafeSelectClonedMediaItem(oldCurrentMediaItemIndex), SafeSelectClonedMediaItem(newCurrentMediaItemIndex));
            }
            // Do we need to fire an event that the next MediaItem has changed?
            if (oldNextMediaItemIndex != newNextMediaItemIndex && OnNextMediaItemChanged != null)
            {
                OnNextMediaItemChanged(this, SafeSelectClonedMediaItem(oldNextMediaItemIndex), SafeSelectClonedMediaItem(newNextMediaItemIndex));
            }

            return true;
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal int AtPositionInsertMediaItem(PlaylistPosition position)
        {
            switch (position)
            {
                case PlaylistPosition.First:
                    return 0;
                case PlaylistPosition.Last:
                    int atPosition = 0;
                    lock (lockVAR)
                    {
                        atPosition = playlist.Count; // list is zero based ("count" not)!
                    }
                    return atPosition;

                case PlaylistPosition.Next:
                    return currentMediaItemIndex + 1;
            } //switch

            return -1;
        }


        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal bool RemoveMediaItem(int atPosition)
        {
            int oldPreviousMediaItemIndex = PreviousMediaItemIndex;
            int oldCurrentMediaItemIndex = CurrentMediaItemIndex;
            int oldNextMediaItemIndex = NextMediaItemIndex;
            lock (lockVAR)
            {
                if (!CheckIndexValidInPlaylist(atPosition))
                {
                    return false;
                }

                if (playlist.Count > 1)
                {
                    while (currentMediaItemIndex == atPosition || nextMediaItemIndex == atPosition)
                    {
                        // what now. (select next mediaitem for current!)
                        if (currentMediaItemIndex == atPosition)
                        {
                            previousMediaItemIndex = -1; // previous not available at this point
                            currentMediaItemIndex = NextMediaItemIndex;
                            nextMediaItemIndex = CalcNextMediaItem(false);
                        }
                        if (nextMediaItemIndex == atPosition)
                        {
                            nextMediaItemIndex = CalcNextMediaItem(nextMediaItemIndex, false);
                        }
                    } //while
                }
                if (previousMediaItemIndex == atPosition)
                {
                    oldPreviousMediaItemIndex = -1;
                    previousMediaItemIndex = -1;
                }

                playlist.RemoveAt(atPosition);

                // renumber internal index pointers
                if (previousMediaItemIndex > atPosition)
                {
                    previousMediaItemIndex--;
                }
                if (currentMediaItemIndex > atPosition)
                {
                    currentMediaItemIndex--;
                }
                if (nextMediaItemIndex > atPosition)
                {
                    nextMediaItemIndex--;
                }

                // adjust old pointers
                if (oldPreviousMediaItemIndex >= atPosition)
                {
                    oldPreviousMediaItemIndex--;
                }
                if (oldCurrentMediaItemIndex >= atPosition)
                {
                    oldCurrentMediaItemIndex--;
                }
                if (oldNextMediaItemIndex >= atPosition)
                {
                    oldNextMediaItemIndex--;
                }
                //corect historyList
                for (int i = previousHistoryList.Count - 1; i >= 0; i--)
                {
                    if (previousHistoryList[i] == atPosition)
                    {
                        previousHistoryList.RemoveAt(i);
                    }
                    else if (previousHistoryList[i] > atPosition)
                    {
                        previousHistoryList[i]--;
                    }
                } //foreach

                // get (new) previous item if not in shufflemode
                if (!shuffleMode)
                {
                    previousMediaItemIndex = CalcPreviousMediaItem(currentMediaItemIndex);
                }
            } //lock
            int newPreviousMediaItemIndex = PreviousMediaItemIndex;
            int newCurrentMediaItemIndex = CurrentMediaItemIndex;
            int newNextMediaItemIndex = NextMediaItemIndex;

            // Do we need to fire an event that the previous MediaItem has changed?
            if (oldPreviousMediaItemIndex != newPreviousMediaItemIndex && OnCurrentMediaItemChanged != null)
            {
                OnCurrentMediaItemChanged(this, SafeSelectClonedMediaItem(oldPreviousMediaItemIndex), SafeSelectClonedMediaItem(newPreviousMediaItemIndex));
            }
            // Do we need to fire an event that the current MediaItem has changed?
            if (oldCurrentMediaItemIndex != newCurrentMediaItemIndex && OnCurrentMediaItemChanged != null)
            {
                OnCurrentMediaItemChanged(this, SafeSelectClonedMediaItem(oldCurrentMediaItemIndex), SafeSelectClonedMediaItem(newCurrentMediaItemIndex));
            }
            // Do we need to fire an event that the next MediaItem has changed?
            if (oldNextMediaItemIndex != newNextMediaItemIndex && OnNextMediaItemChanged != null)
            {
                OnNextMediaItemChanged(this, SafeSelectClonedMediaItem(oldNextMediaItemIndex), SafeSelectClonedMediaItem(newNextMediaItemIndex));
            }

            return true;
        }

        /// <summary>
        /// When there is no next mediaitem then -1 will be return.
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion        
        protected internal int NextMediaItemIndex
        {
            get
            {
                int index = -1;
                lock (lockVAR)
                {
                    // sanity check if item exists
                    if (!CheckIndexValidInPlaylist(nextMediaItemIndex))
                    {
                        // reset it, points to wrong mediaitem
                        nextMediaItemIndex = -1;
                    }
                    index = nextMediaItemIndex;
                }

                return index;
            }
        }

        /// <summary>
        /// When there is no previous mediaitem then -1 will be return. (when for example the list is empty)
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal int PreviousMediaItemIndex
        {
            get
            {
                int index = -1;
                lock (lockVAR)
                {
                    // sanity check if item exists
                    if (!CheckIndexValidInPlaylist(previousMediaItemIndex))
                    {
                        // reset it, points to wrong mediaitem
                        previousMediaItemIndex = -1;
                    }
                    index = previousMediaItemIndex;
                }

                return index;
            }
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal int CurrentMediaItemIndex
        {
            get
            {
                int index = -1;
                lock (lockVAR)
                {
                    // sanity check is item exists
                    if (currentMediaItemIndex >= 0 && currentMediaItemIndex < playlist.Count)
                    {
                        // reset it, points to wrong mediaitem
                        index = currentMediaItemIndex;
                    }
                }

                return index;
            }
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal bool ChangeCurrentMediaItemIndex(int newIndex, bool addCurrentToPreviousHistory)
        {
            bool result = false;

            // Should only be changed by "Mediaplayer" class!!
            // Warning lock could already be active here (see "CurrentMediaItem")
            lock (lockVAR)
            {
                // check is index is valid
                if (CheckIndexValidInPlaylist(newIndex))
                {
                    if (currentMediaItemIndex != newIndex ||
                        (currentMediaItemIndex == newIndex && (repeatMode == PlaylistRepeatMode.RepeatSingle || repeatMode == PlaylistRepeatMode.RepeatPlaylist)))
                    {
                        int oldnextMediaItemIndex = NextMediaItemIndex;
                        int oldpreviousMediaItemIndex = previousMediaItemIndex;
                        int oldcurrentMediaItemIndex = currentMediaItemIndex;

                        if (addCurrentToPreviousHistory)
                        {
                            if (previousHistoryList.Count >= MAX_MEDIAITEMS)
                            {
                                // remove oldest
                                previousHistoryList.RemoveAt(0);
                            }
                            previousHistoryList.Add(currentMediaItemIndex);
                        }
                        currentMediaItemIndex = newIndex;    
                        previousMediaItemIndex = CalcPreviousMediaItem(currentMediaItemIndex);
                        nextMediaItemIndex = CalcNextMediaItem(false);

                        result = true;

                        // Fire event that current mediaitem has changed!!
                        // Fire event that next mediaitem has changed!!
                        // Fire event that previous mediaitem has changed!!
                        if (OnCurrentMediaItemChanged != null)
                        {
                            OnCurrentMediaItemChanged(this, SafeSelectClonedMediaItem(oldcurrentMediaItemIndex), SafeSelectClonedMediaItem(currentMediaItemIndex));
                        }
                        if (OnPreviousMediaItemChanged != null)
                        {
                            OnPreviousMediaItemChanged(this, SafeSelectClonedMediaItem(oldpreviousMediaItemIndex), SafeSelectClonedMediaItem(previousMediaItemIndex));
                        }
                        if (OnNextMediaItemChanged != null)
                        {
                            OnNextMediaItemChanged(this, SafeSelectClonedMediaItem(oldnextMediaItemIndex), SafeSelectClonedMediaItem(nextMediaItemIndex));
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// parameter "setRandomToCurrentMediaItem" is only used when ShuffleMode is on
        /// </summary>
        private int CalcNextMediaItem(int asCurrentIndex, bool setRandomToCurrentMediaItem = false)
        {
            int index = -1;

            // calculate next mediaitem based on repeat & shuffle mode
            if (repeatMode == PlaylistRepeatMode.RepeatSingle)
            {
                // shufflemode is disabled 
                index = asCurrentIndex;
            }
            else if (shuffleMode)
            {
                index = CalcShuffleMode_NextMediaItem(setRandomToCurrentMediaItem);
            }
            else if (asCurrentIndex >= (playlist.Count - 1))
            {
                if (repeatMode == PlaylistRepeatMode.RepeatPlaylist)
                {
                    index = 0;
                }
            }
            else
            {
                index = asCurrentIndex + 1;
            }

            return index;
        }

        private int CalcNextMediaItem(bool setRandomToCurrentMediaItem = false)
        {
            lock (lockVAR)
            {
                return CalcNextMediaItem(currentMediaItemIndex, setRandomToCurrentMediaItem);
            }
        }

        private int CalcShuffleMode_NextMediaItem(bool setRandomToCurrentMediaItem = false)
        {
            int index = -1;

            // Create list of mediaitems to select from
            int listCount = 0;
            int[] list = new int[playlist.Count];
            while (listCount <= 0)
            {
                listCount = 0;
                int counter = 0;
                foreach (MediaItem item in playlist)
                {
                    if ((counter != currentMediaItemIndex || (counter == currentMediaItemIndex && setRandomToCurrentMediaItem)) && !item._ShuffleDone)
                    {
                        list[listCount] = counter;
                        listCount++;
                    }
                    counter++;
                } //foreach
                if (listCount <= 0)
                {
                    // Reset ShuffleDone and try again when in repeatPlaylist mode
                    foreach (MediaItem item in playlist)
                    {
                        item._ShuffleDone = false;
                    } //foreach

                    if (repeatMode != PlaylistRepeatMode.RepeatPlaylist)
                    {
                        // no new nextitem
                        index = -1;
                        break;
                    }
                }
            } //while

            // kies random nieuw next item!
            if (listCount > 0)
            {
                index = randomizer.Next(0, listCount - 1);
                // select the correct item
                index = list[index];
                playlist[index]._ShuffleDone = true;
            }

            if (index != -1 && setRandomToCurrentMediaItem)
            {
                currentMediaItemIndex = index;
                // now we need to calculate is again for the NextMediaItem
                index = CalcShuffleMode_NextMediaItem(false);
            }

            return index;
        }

        private int CalcPreviousMediaItem(int asCurrentIndex)
        {
            int index = -1;

            // calculate next mediaitem based on repeat & shuffle mode
            if (repeatMode == PlaylistRepeatMode.RepeatSingle)
            {
                // shufflemode is disabled 
                index = asCurrentIndex;
            }
            else if (shuffleMode)
            {
                // look at history which was the previous
                if (previousHistoryList.Count > 0)
                {
                    index = previousHistoryList[previousHistoryList.Count - 1];
                    previousHistoryList.RemoveAt(previousHistoryList.Count - 1);
                }
            }
            else if (asCurrentIndex <= 0)
            {
                if (repeatMode == PlaylistRepeatMode.RepeatPlaylist)
                {
                    index = playlist.Count - 1;
                }
            }
            else
            {
                index = asCurrentIndex - 1;
            }

            return index;
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal bool ChangeCurrentMediaItemGUID(Guid newMediaItem, bool addCurrentToPreviousHistory)
        {
            lock (lockVAR)
            {
                int i = 0;
                foreach (MediaItem item in playlist)
                {
                    if (newMediaItem.Equals(item.GUID))
                    {
                        // found 
                        return ChangeCurrentMediaItemIndex(i, addCurrentToPreviousHistory);
                    }
                    i++;
                } //foreach
            } //lock

            return false;
        }


        /// <summary>
        /// Can be null when there is no next item.
        ///  
        /// returned MediaItem is a cloned object!
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion        
        protected internal MediaItem NextMediaItem
        {
            get
            {
                MediaItem item = null;
                lock (lockVAR)
                {
                    int index = NextMediaItemIndex;
                    if (index >= 0)
                    {
                        item = (MediaItem)playlist[index].Clone();
                    }
                }

                return item;
            }
        }

        /// <summary>
        /// Can be null when there is no previous item.
        ///  
        /// returned MediaItem is a cloned object!
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion        
        protected internal MediaItem PreviousMediaItem
        {
            get
            {
                MediaItem item = null;
                lock (lockVAR)
                {
                    int index = PreviousMediaItemIndex;
                    if (index >= 0)
                    {
                        item = (MediaItem)playlist[index].Clone();
                    }
                }

                return item;
            }
        }

        /// <summary>
        /// Can be null when there is no currentmediaitem
        ///  
        /// returned MediaItem is a cloned object!
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion        
        protected internal MediaItem CurrentMediaItem
        {
            get
            {
                MediaItem item = null;
                lock (lockVAR)
                {
                    int index = CurrentMediaItemIndex;
                    if (index >= 0)
                    {
                        item = (MediaItem)playlist[index].Clone();
                    }
                }

                return item;
            }
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal bool ChangeCurrentMediaItem(MediaItem newItem, bool addCurrentToPreviousHistory)
        {
            // Should only be changed by "Mediaplayer" class!!
            lock (lockVAR)
            {
                bool found = true;
                int index = -1;

                if (newItem != null)
                {
                    // First find selected item
                    found = false;
                    foreach (MediaItem item in playlist)
                    {
                        index++;
                        if (item.GUID.Equals(newItem.GUID))
                        {
                            // found
                            found = true;
                            break;
                        }
                    } //foreach
                }
                if (!found)
                {
                    index = -1;
                }

                // Let this property handle it all
                return ChangeCurrentMediaItemIndex(index, addCurrentToPreviousHistory);
            } //lock
        }

        /// <summary>
        /// Get NonCloned version of MediaItem, be careful with multithread issues
        /// </summary>
        /// <returns></returns>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal MediaItem GetOrginalMediaItem(Guid guid)
        {
            lock (lockVAR)
            {
                foreach (MediaItem item in playlist)
                {
                    if (guid.Equals(item.GUID))
                    {
                        return item;
                    }
                } //foreach
            }

            return null;
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal int GetMediaItemIndex(Guid guid)
        {
            lock (lockVAR)
            {
                int index = 0;
                foreach (MediaItem item in playlist)
                {
                    if (guid.Equals(item.GUID))
                    {
                        return index;
                    }
                    index++;
                } //foreach
            }

            return -1;
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal int GetMediaItemIndex(MediaItem mediaItem)
        {
            return GetMediaItemIndex(mediaItem.GUID);
        }

        /// <summary>
        /// Clear the playlist
        /// </summary>
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal void Clear()
        {
            lock (lockVAR)
            {
                string savedPlaylistName = playlistName;

                InitVars();
                playlistNumber--; // restore counter (possible because we use a lock here!)
                this.playlistName = savedPlaylistName;
            }
        }

        /// <summary>
        /// Count of MediaItems in this playlist
        /// </summary>
        public int Count
        {
            get
            {
                lock (lockVAR)
                {
                    return playlist.Count;
                } //lock
            }
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal void ClearPreviousHistory()
        {
            lock (lockVAR)
            {
                previousHistoryList.Clear();
            }
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)] // hide it for code completion
        protected internal int PreviousHistoryCount
        {
            get
            {
                lock (lockVAR)
                {
                    return previousHistoryList.Count;
                }
            }
        }
        
        /// <summary>
        /// Indexer to get MediaItems
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public MediaItem this[int index]
        {
            get
            {
                lock (lockVAR)
                {
                    if (index >= 0 && index < playlist.Count)
                    {
                        return playlist[index];
                    }
                } //lock

                return null;
            }
        }

        /// <summary>
        /// interator over playlist mediaitems
        /// </summary>
        public IEnumerator GetEnumerator()
        {
            lock (lockVAR)
            {
                foreach (MediaItem item in playlist)
                {
                    // Yield each mediaitem from the playlist
                    yield return item;
                } //foreach
            } //lock
        }

        #endregion


        #region Internal routines

        private bool CheckIndexValidInPlaylist(int index)
        {
            lock (lockVAR)
            {
                return (index >= 0 && index < playlist.Count);
            }
        }

        private MediaItem SafeSelectMediaItem(int index)
        {
            lock (lockVAR)
            {
                if (CheckIndexValidInPlaylist(index))
                {
                    return playlist[index];
                }
            }

            return null;
        }

        private MediaItem SafeSelectClonedMediaItem(int index)
        {
            lock (lockVAR)
            {
                if (CheckIndexValidInPlaylist(index))
                {
                    return (MediaItem)playlist[index].Clone();
                }
            }

            return null;
        }


        #endregion
    }



    public enum PlaylistPosition
    {
        First,
        Next,
        Last
    }

    public enum PlaylistRepeatMode
    {
        RepeatNone = 0,
        RepeatSingle,
        RepeatPlaylist
    }


    // Playlist delegates
    public delegate void PL_OnMediaItemChanged(object sender, MediaItem oldMediaItem, MediaItem newMediaItem);
}
