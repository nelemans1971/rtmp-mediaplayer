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
using System.Threading;
using System.Threading.Tasks;
using CDR.LibRTMP;
using CDR.LibRTMP.Media;

namespace TestMediaPlayer_Console
{
    class TestRun1
    {
        private Mediaplayer mediaplayer = null;

        public void Run()
        {
            Console.SetWindowSize(80, 80);
            Console.Clear();
            Console.SetCursorPosition(0, 10);

            mediaplayer = new Mediaplayer("", "");
            try
            {
                mediaplayer.OnServerConnect += new MP_OnServer(DoOnServerConnect);
                mediaplayer.OnServerDisconnect += new MP_OnServer(DoOnServerDisconnect);

                mediaplayer.OnStateChangeMediaplayer += new MP_OnStateChangeMediaplayer(DoOnStateChangeMediaplayer);

                mediaplayer.OnCurrentMediaItemChanged += new PL_OnMediaItemChanged(DoOnCurrentMediaItemChanged);
                mediaplayer.OnPreviousMediaItemChanged += new PL_OnMediaItemChanged(DoOnPreviousMediaItemChanged);
                mediaplayer.OnNextMediaItemChanged += new PL_OnMediaItemChanged(DoOnNextMediaItemChanged);

                mediaplayer.OnPlaylistStart += new MP_OnPlaylist(DoOnPlaylistStart);
                mediaplayer.OnPlaylistEnd += new MP_OnPlaylist(DoOnPlaylistEnd);

                mediaplayer.OnMediaItemStartPlay += new MP_OnMediaItem(DoOnMediaItemStartPlay);
                mediaplayer.OnMediaItemEndPlay += new MP_OnMediaItem(DoOnMediaItemEndPlay);
                mediaplayer.OnMediaItemSeekStart += new MP_OnMediaItem(DoOnMediaItemSeekStart);
                mediaplayer.OnMediaItemSeekEnd += new MP_OnMediaItem(DoOnMediaItemSeekEnd);
                

                mediaplayer.OnTick += new MP_OnTick(DoOnTick);

                mediaplayer.RTMPServerLink = new ServerLink("rtmp://127.0.0.1:1935/vod");
                mediaplayer.Connect();

                NewPlaylist();

                // Wait until we are connected (needed because we run async)
                while (!mediaplayer.IsConnected && !mediaplayer.LastConnectFailed)
                {
                    Thread.Sleep(100);
                } //while

                //mediaplayer.ChangeCurrentMediaItemIndex(1);

                ExecMenu();
            }
            finally
            {
                // Needed to stop thread and stop the program
                if (mediaplayer != null)
                {
                    mediaplayer.Close();
                }
            }
        }

        private void NewPlaylist()
        {
            if (mediaplayer != null)
            {
                mediaplayer.ClearPlaylist();

                mediaplayer.InsertMediaItem(PlaylistPosition.Last, new MediaItem("MP3:Comfort_Fit_-_03_-_Sorry.mp3"));
                mediaplayer.InsertMediaItem(PlaylistPosition.Last, new MediaItem("MP3:Kriss_-_03_-_jazz_club.mp3"));
                mediaplayer.InsertMediaItem(PlaylistPosition.Last, new MediaItem("MP3:Monopole_-_02_-_Stereo-vision_radio.mp3"));
            }
        }

        private void ExecMenu()
        {
            string ch = "\0";
            do
            {
                ch = Console.ReadKey(true).KeyChar.ToString().ToUpper();
                switch (ch)
                {
                    case "P": //play/pause
                        if (mediaplayer.IsStopped || mediaplayer.IsPausing)
                        {
                            mediaplayer.Play();
                        }
                        else if (mediaplayer.IsPlaying)
                        {
                            mediaplayer.Pause();
                        }
                        break;
                    case "Z": //stop
                        if (mediaplayer.IsPlaying || mediaplayer.IsPausing)
                        {
                            mediaplayer.Stop();
                        }
                        break;

                    case ",":
                    case "<": // previous
                        mediaplayer.Previous();
                        break;
                    case ".":
                    case ">": // next
                        mediaplayer.Next();
                        break;

                    case "S": // Seek
                        if (mediaplayer.IsPlaying)
                        {
                            mediaplayer.Seek(15 * 1000);
                        }
                        break;
                    case "+":
                        if (mediaplayer.Volume < 100)
                        {
                            mediaplayer.Volume = Convert.ToByte(mediaplayer.Volume + 1);
                        }
                        break;
                    case "-":
                        if (mediaplayer.Volume > 0)
                        {
                            mediaplayer.Volume = Convert.ToByte(mediaplayer.Volume - 1);
                        }
                        break;
                } //switch
            } while (ch != "Q");
        }


        private void DoOnServerConnect(object sender)
        {
            Console.WriteLine("CONNECTED");
        }

        private void DoOnServerDisconnect(object sender)
        {
            Console.WriteLine("DISCONNECTED");
        }

        private void DoOnStateChangeMediaplayer(object sender, MediaplayerState state)
        {
            switch (state)
            {
                case MediaplayerState.Disconnected:
                    Console.WriteLine("===========DISCONNECTED STATE===");
                    break;
                case MediaplayerState.Connecting:
                    Console.WriteLine("===========CONNECTING STATE=====");
                    break;

                case MediaplayerState.Stop:
                    Console.WriteLine("===========STOP STATE===========");
                    break;
                case MediaplayerState.Playing:
                    Console.WriteLine("===========PLAYING STATE========");
                    break;
                case MediaplayerState.Pause:
                    Console.WriteLine("===========PAUSE STATE==========");
                    break;
                                    
                default:
                    Console.WriteLine("===========UNKNOWN STATE========");
                    break;
            } //switch
        }

        private void DoOnCurrentMediaItemChanged(object sender, MediaItem oldMediaItem, MediaItem newMediaItem)
        {
            if (newMediaItem == null)
            {
                Console.WriteLine("Current Media = None");
            }
            else
            {
                Console.WriteLine("Current Media = " + newMediaItem.MediaFile);
            }
        }

        private void DoOnPreviousMediaItemChanged(object sender, MediaItem oldMediaItem, MediaItem newMediaItem)
        {
            if (newMediaItem == null)
            {
                Console.WriteLine("Previous Media = None");
            }
            else
            {
                Console.WriteLine("Previous Media = " + newMediaItem.MediaFile);
            }
        }

        private void DoOnNextMediaItemChanged(object sender, MediaItem oldMediaItem, MediaItem newMediaItem)
        {
            if (newMediaItem == null)
            {
                Console.WriteLine("Next Media = None");
            }
            else
            {
                Console.WriteLine("Next Media = " + newMediaItem.MediaFile);
            }
        }

        private void DoOnPlaylistStart(object sender, Playlist playlist)
        {
            Console.WriteLine();
            Console.WriteLine("OnPlaylistStart. --=" + playlist.PlaylistName + "=--");
        }

        private void DoOnPlaylistEnd(object sender, Playlist playlist)
        {
            Console.WriteLine("OnPlaylistEnd. --=" + playlist.PlaylistName + "=--");
            Console.WriteLine();
        }

        private void DoOnMediaItemStartPlay(object sender, MediaItem item)
        {
            Console.WriteLine();
            Console.WriteLine("MediaItem Start Play = " + item.MediaFile);
        }

        private void DoOnMediaItemSeekStart(object sender, MediaItem item)
        {
            Console.WriteLine("SeekStart (" + item.MediaFile + ")");
        }
        private void DoOnMediaItemSeekEnd(object sender, MediaItem item)
        {
            Console.WriteLine("SeekEnd (" + item.MediaFile + ")");
        }

        private void DoOnMediaItemEndPlay(object sender, MediaItem item)
        {
            Console.WriteLine("MediaItem End Play = " + item.MediaFile);
            Console.WriteLine();
        }

        private void DoOnTick(object sender)
        {
            int l = Console.CursorLeft;
            int t = Console.CursorTop;
            try
            {
                Mediaplayer mp = sender as Mediaplayer;
                if (mp != null)
                {
                    Console.SetCursorPosition(0, 0);

                    MediaItem item = mp.CurrentMediaItem;
                    if (item != null)
                    {
                        Console.Write("Playing now : " + item.MediaFile);
                    }
                    else
                    {
                        Console.Write("".PadLeft(Console.WindowWidth));
                    }

                    Console.SetCursorPosition(0, 1);
                    double pos = mp.Position;
                    double duration = mp.Duration;
                    if (pos >= 0 && pos <= duration) // valid position
                    {
                        int p = 0;
                        if (duration > 0)
                        {
                            p = Convert.ToInt32((25 * pos) / duration);
                        }
                        string slider = "".PadLeft(p, '-') + "|" + "".PadLeft(25 - p, '-');
                        Console.Write(string.Format("{0} [{1}] {2} ({3})", Position2TimeStr(pos), slider, Position2TimeStr(duration - pos), Position2TimeStr(duration)));
                    }

                    Console.SetCursorPosition(0, 2);
                    Console.Write("".PadLeft(Console.WindowWidth));

                    Console.SetCursorPosition(0, 3);
                    Console.Write("Next     : ");
                    item = mp.NextMediaItem;
                    if (item != null)
                    {
                        Console.Write(item.MediaFile);
                    }
                    else
                    {
                        Console.Write("".PadLeft(Console.WindowWidth - Console.CursorLeft));
                    }

                    Console.SetCursorPosition(0, 4);
                    Console.Write("Previous : ");
                    item = mp.PreviousMediaItem;
                    if (item != null)
                    {
                        Console.Write(item.MediaFile);
                    }
                    else
                    {
                        Console.Write("".PadLeft(Console.WindowWidth - Console.CursorLeft));
                    }

                    Console.SetCursorPosition(0, 5);
                    Console.Write("State    : " + mp.MediaplayerState.ToString());
                    Console.Write("".PadLeft(40 - Console.CursorLeft));
                    Console.Write("Volume   : " + mp.Volume.ToString());
                    Console.Write("".PadLeft(Console.WindowWidth - Console.CursorLeft));
                }

                Console.SetCursorPosition(0, 6);
                Console.Write("".PadLeft(Console.WindowWidth));
                Console.SetCursorPosition(0, 7);
                Console.Write("".PadLeft(Console.WindowWidth));

                Console.SetCursorPosition(0, 8);
                if (mp.IsPlaying)
                {
                    Console.Write("P=Pause | Z=Stop | <=Previous | >=Next | S=Seek(15) | +-=Volume");
                }
                else if (mp.IsPausing)
                {
                    Console.Write("P=Play | Z=Stop | <=Previous | >=Next | S=Seek(15) | +-=Volume");
                }
                else // stopped
                {
                    Console.Write("P=Play | <=Previous | >=Next | S=Seek(15) | +-=Volume");
                }
                Console.Write("| Q=Quit".PadLeft(Console.WindowWidth - Console.CursorLeft));

                Console.SetCursorPosition(0, 9);
                Console.Write("".PadLeft(Console.WindowWidth,'='));
            }
            finally
            {
                Console.SetCursorPosition(l, t);
            }
        }



        private string Position2TimeStr(double time)
        {
            string min = Convert.ToInt32(Math.Floor(time / 60.0)).ToString().PadLeft(2, '0');
            string sec = Convert.ToInt32(time % 60.0).ToString().PadLeft(2, '0');
            return min + ":" + sec;
        }
    }
}
