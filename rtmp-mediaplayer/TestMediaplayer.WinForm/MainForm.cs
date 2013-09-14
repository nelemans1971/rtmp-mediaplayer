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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CDR.LibRTMP;
using CDR.LibRTMP.Media;

namespace TestMediaPlayer_WinForm
{
    public partial class MainForm : Form
    {
        private Mediaplayer mediaplayer = null;

        private bool progressIsDragged = false;

        public MainForm()
        {
            InitializeComponent();

            // put key for bass.net here if you have them (oterwhise you'll see a nag screen)
            mediaplayer = new Mediaplayer("", "");

            lbPlaylist.Items.Clear();
            lConnectStatus.Text = "";
            lFilename.Text = "";
            bTogglePlay.Text = "Play";
            tbLog.Text = "";
            lPrevious.Text = "";
            lNext.Text = "";
            lPreBuf.Visible = false;

            NewPlaylist();
            tbVolume.Value = mediaplayer.Volume;
            UpdateScreenPlaylist();

            mediaplayer.OnServerConnect += new MP_OnServer(DoOnServerConnect);
            mediaplayer.OnServerDisconnect += new MP_OnServer(DoOnServerDisconnect);

            mediaplayer.OnStateChangeMediaplayer += new MP_OnStateChangeMediaplayer(MP_OnStateChangeMediaplayer);
            mediaplayer.OnControleButtonStateChange += new MP_OnControleButtonStateChange(MP_OnControleButtonStateChange);

            mediaplayer.OnCurrentMediaItemChanged += new PL_OnMediaItemChanged(DoOnCurrentMediaItemChanged);
            mediaplayer.OnPreviousMediaItemChanged += new PL_OnMediaItemChanged(DoOnPreviousMediaItemChanged);
            mediaplayer.OnNextMediaItemChanged += new PL_OnMediaItemChanged(DoOnNextMediaItemChanged);

            mediaplayer.OnPlaylistStart += new MP_OnPlaylist(DoOnPlaylistStart);
            mediaplayer.OnPlaylistEnd += new MP_OnPlaylist(DoOnPlaylistEnd);

            mediaplayer.OnMediaItemStartPlay += new MP_OnMediaItem(DoOnMediaItemStartPlay);
            mediaplayer.OnMediaItemEndPlay += new MP_OnMediaItem(DoOnMediaItemEndPlay);
            mediaplayer.OnMediaItemSeekStart += new MP_OnMediaItem(DoOnMediaItemSeekStart);
            mediaplayer.OnMediaItemSeekEnd += new MP_OnMediaItem(DoOnMediaItemSeekEnd);
            mediaplayer.OnPreBuffer += new MP_OnPreBuffer(MP_OnPreBuffer);

            mediaplayer.OnTick += new MP_OnTick(DoOnTick);

            mediaplayer.TriggerMediaItemEvents();
            mediaplayer.TriggerButtonStateEvent();

            mediaplayer.RTMPServerLink = new ServerLink("rtmp://127.0.0.1:1935/vod");
            mediaplayer.Connect();
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (mediaplayer != null)
            {
                mediaplayer.Close(); // not needed netConnection.Close() will also take care of it
            }
        }

        private void bClose_Click(object sender, EventArgs e)
        {
            if (mediaplayer != null)
            {
                mediaplayer.Close();
                mediaplayer = null;
            }

            // Close form
            this.Close();
        }

        private void AddLogLine(string line)
        {
            tbLog.Text = line + "\r\n" + tbLog.Text;
        }

        private void NewPlaylist()
        {
            if (mediaplayer != null)
            {
                mediaplayer.ClearPlaylist();

                MediaItem item;

                item = new MediaItem("MP3:Comfort_Fit_-_03_-_Sorry.mp3");
                item.MetaDurationInMS = ((3 * 60) + 21) * 1000;
                mediaplayer.InsertMediaItem(PlaylistPosition.Last, item);

                item = new MediaItem("MP3:Kriss_-_03_-_jazz_club.mp3");
                item.MetaDurationInMS = ((5 * 60) + 25) * 1000;
                mediaplayer.InsertMediaItem(PlaylistPosition.Last, item);

                item = new MediaItem("MP3:Monopole_-_02_-_Stereo-vision_radio.mp3");
                item.MetaDurationInMS = ((8 * 60) + 16) * 1000;
                mediaplayer.InsertMediaItem(PlaylistPosition.Last, item);

                item = new MediaItem("MP3:Paper_Navy_-_08_-_Swan_Song.mp3");
                item.MetaDurationInMS = ((3 * 56) + 18) * 1000;
                mediaplayer.InsertMediaItem(PlaylistPosition.Last, item);
            }
        }

        private void UpdateScreenPlaylist()
        {
            if (mediaplayer != null)
            {
                Guid GUID = Guid.Empty;
                if (lbPlaylist.SelectedIndex >= 0)
                {
                    GUID = (lbPlaylist.Items[lbPlaylist.SelectedIndex] as MediaItem).GUID;
                }

                lbPlaylist.Items.Clear();
                int count = 0;
                foreach (MediaItem item in mediaplayer.CurrentPlaylist)
                {
                    lbPlaylist.Items.Add(item);
                    if (item.GUID.Equals(GUID))
                    {
                        lbPlaylist.SelectedIndex = count;
                    }
                    count++;
                } //foreach
            }
        }

        private void DoOnServerConnect(object sender)
        {
            lConnectStatus.Text = "CONNECTED";
            lConnectStatus.BackColor = Color.Green;
        }

        private void DoOnServerDisconnect(object sender)
        {
            lConnectStatus.Text = "DISCONNECTED";
            lConnectStatus.BackColor = Color.Red;
        }

        private void MP_OnStateChangeMediaplayer(object sender, MediaplayerState state)
        {
            switch (state)
            {
                case MediaplayerState.Disconnected:
                    AddLogLine("===========DISCONNECTED STATE===");
                    break;
                case MediaplayerState.Connecting:
                    AddLogLine("===========CONNECTING STATE=====");
                    break;

                case MediaplayerState.Stop:
                    AddLogLine("===========STOP STATE===========");
                    break;
                case MediaplayerState.Playing:
                    AddLogLine("===========PLAYING STATE========");
                    break;
                case MediaplayerState.Pause:
                    AddLogLine("===========PAUSE STATE==========");
                    break;

                default:
                    AddLogLine("===========UNKNOWN STATE========");
                    break;
            } //switch
        }

        private void MP_OnControleButtonStateChange(object sender, MPButtonState previousButton, MPButtonState stopButton, MPButtonState playButton, MPButtonState pauseButton, MPButtonState nextButton)
        {
            bPrevious.Enabled = (previousButton == MPButtonState.Active);
            bNext.Enabled = (nextButton == MPButtonState.Active);
            bTogglePlay.Enabled = (stopButton == MPButtonState.Active || playButton == MPButtonState.Active || pauseButton == MPButtonState.Active);
        }
        
        private void DoOnCurrentMediaItemChanged(object sender, MediaItem oldMediaItem, MediaItem newMediaItem)
        {
            if (newMediaItem == null)
            {
                AddLogLine("Current Media = None");
                lFilename.Text = "";

            }
            else
            {
                AddLogLine("Current Media = " + newMediaItem.MediaFile);
                lFilename.Text = newMediaItem.MediaFile;
            }
        }

        private void DoOnPreviousMediaItemChanged(object sender, MediaItem oldMediaItem, MediaItem newMediaItem)
        {
            if (newMediaItem == null)
            {
                AddLogLine("Previous Media = None");
                lPrevious.Text = "";
            }
            else
            {
                AddLogLine("Previous Media = " + newMediaItem.MediaFile);
                lPrevious.Text = newMediaItem.MediaFile;
            }
        }

        private void DoOnNextMediaItemChanged(object sender, MediaItem oldMediaItem, MediaItem newMediaItem)
        {
            if (newMediaItem == null)
            {
                AddLogLine("Next Media = None");
                lNext.Text = "";
            }
            else
            {
                AddLogLine("Next Media = " + newMediaItem.MediaFile);
                lNext.Text = newMediaItem.MediaFile;
            }
        }

        private void DoOnPlaylistStart(object sender, Playlist playlist)
        {
            AddLogLine("");
            AddLogLine("OnPlaylistStart. --=" + playlist.PlaylistName + "=--");
        }

        private void DoOnPlaylistEnd(object sender, Playlist playlist)
        {
            AddLogLine("OnPlaylistEnd. --=" + playlist.PlaylistName + "=--");
            AddLogLine("");

            // We are is stop state!
            bTogglePlay.Text = "Play";
        }

        private void DoOnMediaItemStartPlay(object sender, MediaItem item)
        {
            lFilename.Text = item.MediaFile;
            AddLogLine("");
            AddLogLine("MediaItem Start Play = " + item.MediaFile);

            int count = 0;
            foreach (MediaItem mediaItem in mediaplayer.CurrentPlaylist)
            {
                if (item.GUID.Equals(mediaItem.GUID))
                {
                    lbPlaylist.SelectedIndex = count;
                    break;
                }
                count++;
            } //foreach
        }

        private void DoOnMediaItemSeekStart(object sender, MediaItem item)
        {
            AddLogLine("SeekStart (" + item.MediaFile + ")");
        }
        private void DoOnMediaItemSeekEnd(object sender, MediaItem item)
        {
            AddLogLine("SeekEnd (" + item.MediaFile + ")");
        }

        private void DoOnMediaItemEndPlay(object sender, MediaItem item)
        {
            AddLogLine("MediaItem End Play = " + item.MediaFile);
            AddLogLine("");
        }

        private void MP_OnPreBuffer(object sender, MediaItem mediaItem, PreBufferState state)
        {
            switch (state)
            {
                case PreBufferState.PrebufferingStarted:
                    lPreBuf.BackColor = Color.Orange;
                    lPreBuf.Visible = true;
                    break;
                case PreBufferState.PrebufferingReady:
                    lPreBuf.BackColor = Color.Green;
                    lPreBuf.Visible = true;
                    break;
                case PreBufferState.PrebufferingEndedAndPlaying:
                case PreBufferState.PrebufferingEndedAndCanceled:
                case PreBufferState.Unknown:
                default:
                    lPreBuf.Visible = false;
                    break;

            } //switch
        }


        private void DoOnTick(object sender)
        {
            try
            {
                int p = 0;
                string timePlayed = "00:00";
                string timeLeft = "00:00";

                Mediaplayer mp = sender as Mediaplayer;
                if (mp != null)
                {
                    double duration = mp.Duration; // is in seconds
                    double pos = 0.0;
                    if (!progressIsDragged && !mp.IsSeeking)
                    {
                        pos = mp.Position;
                    }
                    else
                    {
                        pos = (tbProgress.Value * duration) / tbProgress.Maximum;
                    }

                    if (pos >= 0 && pos <= duration) // valid position
                    {
                        p = Convert.ToInt32((pos * tbProgress.Maximum) / duration);
                        timePlayed = Position2TimeStr(pos);
                        timeLeft = Position2TimeStr(duration - pos);
                    }
                }

                if (tbProgress.Value != p)
                {
                    tbProgress.Value = p;
                }
                lTimeToGo.Text = timePlayed;
                lTimeLeft.Text = timeLeft;
            }
            catch (Exception e)
            {
                AddLogLine("Exception TestMediaPlayerSimple.WinForm.DoOnTick: " + e.ToString());
            }
        }

        private string Position2TimeStr(double time)
        {
            string min = Convert.ToInt32(Math.Floor(time / 60.0)).ToString().PadLeft(2, '0');
            string sec = Convert.ToInt32(time % 60.0).ToString().PadLeft(2, '0');
            return min + ":" + sec;
        }

        private void bTogglePlay_Click(object sender, EventArgs e)
        {
            if (mediaplayer != null)
            {
                if (mediaplayer.IsStopped || mediaplayer.IsPausing)
                {
                    if (mediaplayer.IsStopped)
                    {
                        // Select MediaItem and start playing
                        if (lbPlaylist.SelectedIndex < 0 && lbPlaylist.Items.Count > 0)
                        {
                            lbPlaylist.SelectedIndex = 0;
                        }
                        if (lbPlaylist.SelectedIndex >= 0)
                        {
                            mediaplayer.ChangeCurrentMediaItemIndex(lbPlaylist.SelectedIndex, false);
                        }
                    }

                    mediaplayer.Play();
                    bTogglePlay.Text = "Pause";
                }
                else if (mediaplayer.IsPlaying)
                {
                    mediaplayer.Pause();
                    bTogglePlay.Text = "Play";
                }
            }
        }

        private void tbProgress_MouseDown(object sender, MouseEventArgs e)
        {
            if (mediaplayer == null)
            {
                return;
            }

            progressIsDragged = true;
        }

        private void tbProgress_MouseUp(object sender, MouseEventArgs e)
        {
            if (mediaplayer == null)
            {
                return;
            }

            try
            {
                // set new location
                if (mediaplayer.IsPlaying)
                {
                    long posInMS = Convert.ToInt64((mediaplayer.DurationInMS * tbProgress.Value) / tbProgress.Maximum);

                    mediaplayer.Seek(posInMS);
                }
            }
            finally
            {
                // importnt do as last
                progressIsDragged = false;
            }  
        }

        private void tbVolume_ValueChanged(object sender, EventArgs e)
        {
            if (mediaplayer != null)
            {
                mediaplayer.Volume = Convert.ToByte(tbVolume.Value);
            }
        }

        private void bNext_Click(object sender, EventArgs e)
        {
            if (mediaplayer != null)
            {
                mediaplayer.Next();
            }
        }

        private void bPrevious_Click(object sender, EventArgs e)
        {
            if (mediaplayer != null)
            {
                mediaplayer.Previous();
            }
        }

        private void bDelete_Click(object sender, EventArgs e)
        {
            if (mediaplayer != null && lbPlaylist.SelectedIndex >= 0)
            {
                mediaplayer.RemoveMediaItem((MediaItem)lbPlaylist.Items[lbPlaylist.SelectedIndex]);
                UpdateScreenPlaylist();
            }
        }

        private void bInsert_Click(object sender, EventArgs e)
        {
            if (mediaplayer != null && lbPlaylist.SelectedIndex >= 0)
            {
                MediaItem item = new MediaItem("MP3:Kriss_-_03_-_jazz_club.mp3");
                item.MetaDurationInMS = ((5 * 60) + 25) * 1000;
                mediaplayer.InsertMediaItem(PlaylistPosition.Next, item);

                UpdateScreenPlaylist();
            }
        }

        private void lbPlaylist_MouseClick(object sender, MouseEventArgs e)
        {
            if (mediaplayer != null && mediaplayer.IsStopped && lbPlaylist.SelectedIndex >= 0)
            {
                mediaplayer.ChangeCurrentMediaItemIndex(lbPlaylist.SelectedIndex, false);
            }
        }

        private void lbPlaylist_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (mediaplayer != null && lbPlaylist.SelectedIndex >= 0)
            {
                bool addToPreviousHistory = (mediaplayer.IsPlaying || mediaplayer.IsPausing);
                mediaplayer.Stop();
                mediaplayer.ChangeCurrentMediaItemIndex(lbPlaylist.SelectedIndex, addToPreviousHistory);
                mediaplayer.Play();
            }
        }

    }
}
