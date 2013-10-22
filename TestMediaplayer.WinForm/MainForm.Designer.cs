namespace TestMediaPlayer_WinForm
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.lbPlaylist = new System.Windows.Forms.ListBox();
            this.cmsPLay = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.playFromThisPointToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.cancelToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.label1 = new System.Windows.Forms.Label();
            this.bTogglePlay = new System.Windows.Forms.Button();
            this.bClose = new System.Windows.Forms.Button();
            this.tbProgress = new System.Windows.Forms.TrackBar();
            this.lTimeToGo = new System.Windows.Forms.Label();
            this.lTimeLeft = new System.Windows.Forms.Label();
            this.tbVolume = new System.Windows.Forms.TrackBar();
            this.label4 = new System.Windows.Forms.Label();
            this.tbLog = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.lConnectStatus = new System.Windows.Forms.Label();
            this.lFilename = new System.Windows.Forms.Label();
            this.bNext = new System.Windows.Forms.Button();
            this.bPrevious = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.lPrevious = new System.Windows.Forms.Label();
            this.lNext = new System.Windows.Forms.Label();
            this.lPreBuf = new System.Windows.Forms.Label();
            this.bDelete = new System.Windows.Forms.Button();
            this.bInsert = new System.Windows.Forms.Button();
            this.cmsPLay.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.tbProgress)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbVolume)).BeginInit();
            this.SuspendLayout();
            // 
            // lbPlaylist
            // 
            this.lbPlaylist.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lbPlaylist.ContextMenuStrip = this.cmsPLay;
            this.lbPlaylist.FormattingEnabled = true;
            this.lbPlaylist.Location = new System.Drawing.Point(11, 136);
            this.lbPlaylist.Name = "lbPlaylist";
            this.lbPlaylist.Size = new System.Drawing.Size(293, 121);
            this.lbPlaylist.TabIndex = 0;
            this.lbPlaylist.MouseClick += new System.Windows.Forms.MouseEventHandler(this.lbPlaylist_MouseClick);
            this.lbPlaylist.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.lbPlaylist_MouseDoubleClick);
            // 
            // cmsPLay
            // 
            this.cmsPLay.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.playFromThisPointToolStripMenuItem,
            this.cancelToolStripMenuItem});
            this.cmsPLay.Name = "cmsPLay";
            this.cmsPLay.Size = new System.Drawing.Size(167, 48);
            // 
            // playFromThisPointToolStripMenuItem
            // 
            this.playFromThisPointToolStripMenuItem.Name = "playFromThisPointToolStripMenuItem";
            this.playFromThisPointToolStripMenuItem.Size = new System.Drawing.Size(166, 22);
            this.playFromThisPointToolStripMenuItem.Text = "Play from this point";
            // 
            // cancelToolStripMenuItem
            // 
            this.cancelToolStripMenuItem.Name = "cancelToolStripMenuItem";
            this.cancelToolStripMenuItem.Size = new System.Drawing.Size(166, 22);
            this.cancelToolStripMenuItem.Text = "Cancel";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(11, 120);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(39, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Playlist";
            // 
            // bTogglePlay
            // 
            this.bTogglePlay.Location = new System.Drawing.Point(121, 87);
            this.bTogglePlay.Name = "bTogglePlay";
            this.bTogglePlay.Size = new System.Drawing.Size(75, 23);
            this.bTogglePlay.TabIndex = 2;
            this.bTogglePlay.Text = "Play/Pause";
            this.bTogglePlay.UseVisualStyleBackColor = true;
            this.bTogglePlay.Click += new System.EventHandler(this.bTogglePlay_Click);
            // 
            // bClose
            // 
            this.bClose.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.bClose.Location = new System.Drawing.Point(121, 504);
            this.bClose.Name = "bClose";
            this.bClose.Size = new System.Drawing.Size(75, 23);
            this.bClose.TabIndex = 5;
            this.bClose.Text = "Close";
            this.bClose.UseVisualStyleBackColor = true;
            this.bClose.Click += new System.EventHandler(this.bClose_Click);
            // 
            // tbProgress
            // 
            this.tbProgress.Location = new System.Drawing.Point(48, 54);
            this.tbProgress.Maximum = 100;
            this.tbProgress.Name = "tbProgress";
            this.tbProgress.Size = new System.Drawing.Size(216, 42);
            this.tbProgress.TabIndex = 6;
            this.tbProgress.TickStyle = System.Windows.Forms.TickStyle.None;
            this.tbProgress.MouseDown += new System.Windows.Forms.MouseEventHandler(this.tbProgress_MouseDown);
            this.tbProgress.MouseUp += new System.Windows.Forms.MouseEventHandler(this.tbProgress_MouseUp);
            // 
            // lTimeToGo
            // 
            this.lTimeToGo.AutoSize = true;
            this.lTimeToGo.Location = new System.Drawing.Point(8, 58);
            this.lTimeToGo.Name = "lTimeToGo";
            this.lTimeToGo.Size = new System.Drawing.Size(34, 13);
            this.lTimeToGo.TabIndex = 7;
            this.lTimeToGo.Text = "00:00";
            // 
            // lTimeLeft
            // 
            this.lTimeLeft.AutoSize = true;
            this.lTimeLeft.Location = new System.Drawing.Point(270, 58);
            this.lTimeLeft.Name = "lTimeLeft";
            this.lTimeLeft.Size = new System.Drawing.Size(34, 13);
            this.lTimeLeft.TabIndex = 8;
            this.lTimeLeft.Text = "00:00";
            // 
            // tbVolume
            // 
            this.tbVolume.Location = new System.Drawing.Point(48, 259);
            this.tbVolume.Maximum = 100;
            this.tbVolume.Name = "tbVolume";
            this.tbVolume.Size = new System.Drawing.Size(87, 42);
            this.tbVolume.TabIndex = 9;
            this.tbVolume.TickStyle = System.Windows.Forms.TickStyle.None;
            this.tbVolume.Value = 100;
            this.tbVolume.ValueChanged += new System.EventHandler(this.tbVolume_ValueChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(8, 263);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(45, 13);
            this.label4.TabIndex = 10;
            this.label4.Text = "Volume:";
            // 
            // tbLog
            // 
            this.tbLog.Location = new System.Drawing.Point(11, 307);
            this.tbLog.Multiline = true;
            this.tbLog.Name = "tbLog";
            this.tbLog.Size = new System.Drawing.Size(294, 114);
            this.tbLog.TabIndex = 13;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(12, 291);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(28, 13);
            this.label3.TabIndex = 14;
            this.label3.Text = "Log:";
            // 
            // lConnectStatus
            // 
            this.lConnectStatus.AutoSize = true;
            this.lConnectStatus.BackColor = System.Drawing.Color.Red;
            this.lConnectStatus.Location = new System.Drawing.Point(11, 13);
            this.lConnectStatus.Name = "lConnectStatus";
            this.lConnectStatus.Size = new System.Drawing.Size(22, 13);
            this.lConnectStatus.TabIndex = 15;
            this.lConnectStatus.Text = "-----";
            // 
            // lFilename
            // 
            this.lFilename.Location = new System.Drawing.Point(56, 38);
            this.lFilename.Name = "lFilename";
            this.lFilename.Size = new System.Drawing.Size(199, 13);
            this.lFilename.TabIndex = 16;
            this.lFilename.Text = "-------";
            this.lFilename.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // bNext
            // 
            this.bNext.Location = new System.Drawing.Point(211, 87);
            this.bNext.Name = "bNext";
            this.bNext.Size = new System.Drawing.Size(59, 23);
            this.bNext.TabIndex = 17;
            this.bNext.Text = "Next";
            this.bNext.UseVisualStyleBackColor = true;
            this.bNext.Click += new System.EventHandler(this.bNext_Click);
            // 
            // bPrevious
            // 
            this.bPrevious.Location = new System.Drawing.Point(48, 87);
            this.bPrevious.Name = "bPrevious";
            this.bPrevious.Size = new System.Drawing.Size(59, 23);
            this.bPrevious.TabIndex = 18;
            this.bPrevious.Text = "Previous";
            this.bPrevious.UseVisualStyleBackColor = true;
            this.bPrevious.Click += new System.EventHandler(this.bPrevious_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 440);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(32, 13);
            this.label2.TabIndex = 19;
            this.label2.Text = "Next:";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(8, 424);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(51, 13);
            this.label5.TabIndex = 20;
            this.label5.Text = "Previous:";
            // 
            // lPrevious
            // 
            this.lPrevious.Location = new System.Drawing.Point(56, 424);
            this.lPrevious.Name = "lPrevious";
            this.lPrevious.Size = new System.Drawing.Size(249, 13);
            this.lPrevious.TabIndex = 21;
            this.lPrevious.Text = "PreviousMediaItem";
            // 
            // lNext
            // 
            this.lNext.Location = new System.Drawing.Point(56, 440);
            this.lNext.Name = "lNext";
            this.lNext.Size = new System.Drawing.Size(249, 13);
            this.lNext.TabIndex = 22;
            this.lNext.Text = "NextMediaItem";
            // 
            // lPreBuf
            // 
            this.lPreBuf.AutoSize = true;
            this.lPreBuf.BackColor = System.Drawing.Color.Red;
            this.lPreBuf.Location = new System.Drawing.Point(270, 0);
            this.lPreBuf.Name = "lPreBuf";
            this.lPreBuf.Size = new System.Drawing.Size(50, 13);
            this.lPreBuf.TabIndex = 23;
            this.lPreBuf.Text = "PREBUF";
            // 
            // bDelete
            // 
            this.bDelete.Location = new System.Drawing.Point(245, 263);
            this.bDelete.Name = "bDelete";
            this.bDelete.Size = new System.Drawing.Size(59, 23);
            this.bDelete.TabIndex = 24;
            this.bDelete.Text = "Delete";
            this.bDelete.UseVisualStyleBackColor = true;
            this.bDelete.Click += new System.EventHandler(this.bDelete_Click);
            // 
            // bInsert
            // 
            this.bInsert.Location = new System.Drawing.Point(180, 263);
            this.bInsert.Name = "bInsert";
            this.bInsert.Size = new System.Drawing.Size(59, 23);
            this.bInsert.TabIndex = 25;
            this.bInsert.Text = "Insert";
            this.bInsert.UseVisualStyleBackColor = true;
            this.bInsert.Click += new System.EventHandler(this.bInsert_Click);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(317, 539);
            this.Controls.Add(this.bInsert);
            this.Controls.Add(this.bDelete);
            this.Controls.Add(this.lPreBuf);
            this.Controls.Add(this.lNext);
            this.Controls.Add(this.lPrevious);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.bPrevious);
            this.Controls.Add(this.bNext);
            this.Controls.Add(this.lFilename);
            this.Controls.Add(this.lConnectStatus);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.tbLog);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.lTimeLeft);
            this.Controls.Add(this.lTimeToGo);
            this.Controls.Add(this.bClose);
            this.Controls.Add(this.bTogglePlay);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.lbPlaylist);
            this.Controls.Add(this.tbProgress);
            this.Controls.Add(this.tbVolume);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.Text = "LibRTMP Test";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.MainForm_FormClosed);
            this.cmsPLay.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.tbProgress)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbVolume)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ListBox lbPlaylist;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button bTogglePlay;
        private System.Windows.Forms.Button bClose;
        private System.Windows.Forms.TrackBar tbProgress;
        private System.Windows.Forms.Label lTimeToGo;
        private System.Windows.Forms.Label lTimeLeft;
        private System.Windows.Forms.TrackBar tbVolume;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox tbLog;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ContextMenuStrip cmsPLay;
        private System.Windows.Forms.ToolStripMenuItem playFromThisPointToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem cancelToolStripMenuItem;
        private System.Windows.Forms.Label lConnectStatus;
        private System.Windows.Forms.Label lFilename;
        private System.Windows.Forms.Button bNext;
        private System.Windows.Forms.Button bPrevious;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label lPrevious;
        private System.Windows.Forms.Label lNext;
        private System.Windows.Forms.Label lPreBuf;
        private System.Windows.Forms.Button bDelete;
        private System.Windows.Forms.Button bInsert;
    }
}

