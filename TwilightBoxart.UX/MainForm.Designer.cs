namespace TwilightBoxart.UX
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.btnBrowseSd = new System.Windows.Forms.Button();
            this.txtSdRoot = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.txtBoxart = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.btnBrowseBoxart = new System.Windows.Forms.Button();
            this.chkManualBoxartLocation = new System.Windows.Forms.CheckBox();
            this.btnDetect = new System.Windows.Forms.Button();
            this.folderBrowseDlg = new System.Windows.Forms.FolderBrowserDialog();
            this.btnStart = new System.Windows.Forms.Button();
            this.numWidth = new System.Windows.Forms.NumericUpDown();
            this.numHeight = new System.Windows.Forms.NumericUpDown();
            this.lblSize2 = new System.Windows.Forms.Label();
            this.lblSize1 = new System.Windows.Forms.Label();
            this.btnGithub = new System.Windows.Forms.Button();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.chkKeepAspectRatio = new System.Windows.Forms.CheckBox();
            this.rbtDefault = new System.Windows.Forms.RadioButton();
            this.lblSize = new System.Windows.Forms.Label();
            this.rbtLarge = new System.Windows.Forms.RadioButton();
            this.rbtCustom = new System.Windows.Forms.RadioButton();
            ((System.ComponentModel.ISupportInitialize)(this.numWidth)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numHeight)).BeginInit();
            this.SuspendLayout();
            // 
            // btnBrowseSd
            // 
            this.btnBrowseSd.Location = new System.Drawing.Point(349, 18);
            this.btnBrowseSd.Margin = new System.Windows.Forms.Padding(2);
            this.btnBrowseSd.Name = "btnBrowseSd";
            this.btnBrowseSd.Size = new System.Drawing.Size(76, 30);
            this.btnBrowseSd.TabIndex = 2;
            this.btnBrowseSd.Text = "Browse...";
            this.btnBrowseSd.UseVisualStyleBackColor = true;
            this.btnBrowseSd.Click += new System.EventHandler(this.btnBrowseSd_Click);
            // 
            // txtSdRoot
            // 
            this.txtSdRoot.BackColor = System.Drawing.SystemColors.ControlLightLight;
            this.txtSdRoot.Location = new System.Drawing.Point(6, 24);
            this.txtSdRoot.Margin = new System.Windows.Forms.Padding(2);
            this.txtSdRoot.Name = "txtSdRoot";
            this.txtSdRoot.Size = new System.Drawing.Size(339, 20);
            this.txtSdRoot.TabIndex = 1;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(3, 9);
            this.label1.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(129, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "SD Root / Roms location:";
            // 
            // txtLog
            // 
            this.txtLog.BackColor = System.Drawing.SystemColors.ControlLightLight;
            this.txtLog.Location = new System.Drawing.Point(6, 145);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(500, 189);
            this.txtLog.TabIndex = 10;
            // 
            // txtBoxart
            // 
            this.txtBoxart.BackColor = System.Drawing.SystemColors.ControlLightLight;
            this.txtBoxart.Location = new System.Drawing.Point(6, 71);
            this.txtBoxart.Margin = new System.Windows.Forms.Padding(2);
            this.txtBoxart.Name = "txtBoxart";
            this.txtBoxart.ReadOnly = true;
            this.txtBoxart.Size = new System.Drawing.Size(339, 20);
            this.txtBoxart.TabIndex = 4;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(3, 56);
            this.label2.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(80, 13);
            this.label2.TabIndex = 5;
            this.label2.Text = "Boxart location:";
            // 
            // btnBrowseBoxart
            // 
            this.btnBrowseBoxart.Enabled = false;
            this.btnBrowseBoxart.Location = new System.Drawing.Point(349, 65);
            this.btnBrowseBoxart.Margin = new System.Windows.Forms.Padding(2);
            this.btnBrowseBoxart.Name = "btnBrowseBoxart";
            this.btnBrowseBoxart.Size = new System.Drawing.Size(76, 30);
            this.btnBrowseBoxart.TabIndex = 5;
            this.btnBrowseBoxart.Text = "Browse...";
            this.btnBrowseBoxart.UseVisualStyleBackColor = true;
            this.btnBrowseBoxart.Click += new System.EventHandler(this.btnBrowseBoxart_Click);
            // 
            // chkManualBoxartLocation
            // 
            this.chkManualBoxartLocation.AutoSize = true;
            this.chkManualBoxartLocation.Location = new System.Drawing.Point(430, 71);
            this.chkManualBoxartLocation.Name = "chkManualBoxartLocation";
            this.chkManualBoxartLocation.Size = new System.Drawing.Size(87, 17);
            this.chkManualBoxartLocation.TabIndex = 6;
            this.chkManualBoxartLocation.Text = "Set Manually";
            this.chkManualBoxartLocation.UseVisualStyleBackColor = true;
            this.chkManualBoxartLocation.CheckedChanged += new System.EventHandler(this.chkManualBoxartLocation_CheckedChanged);
            // 
            // btnDetect
            // 
            this.btnDetect.Location = new System.Drawing.Point(429, 18);
            this.btnDetect.Margin = new System.Windows.Forms.Padding(2);
            this.btnDetect.Name = "btnDetect";
            this.btnDetect.Size = new System.Drawing.Size(76, 30);
            this.btnDetect.TabIndex = 3;
            this.btnDetect.Text = "Detect SD";
            this.toolTip.SetToolTip(this.btnDetect, "Will try to detect your (Twilight++) SD card");
            this.btnDetect.UseVisualStyleBackColor = true;
            this.btnDetect.Click += new System.EventHandler(this.btnDetect_Click);
            // 
            // btnStart
            // 
            this.btnStart.Enabled = false;
            this.btnStart.Location = new System.Drawing.Point(429, 337);
            this.btnStart.Margin = new System.Windows.Forms.Padding(2);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(76, 30);
            this.btnStart.TabIndex = 12;
            this.btnStart.Text = "Start";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            // 
            // numWidth
            // 
            this.numWidth.Enabled = false;
            this.numWidth.Location = new System.Drawing.Point(430, 100);
            this.numWidth.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.numWidth.Name = "numWidth";
            this.numWidth.Size = new System.Drawing.Size(75, 20);
            this.numWidth.TabIndex = 7;
            this.numWidth.Value = new decimal(new int[] {
            128,
            0,
            0,
            0});
            // 
            // numHeight
            // 
            this.numHeight.Enabled = false;
            this.numHeight.Location = new System.Drawing.Point(430, 121);
            this.numHeight.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.numHeight.Name = "numHeight";
            this.numHeight.Size = new System.Drawing.Size(75, 20);
            this.numHeight.TabIndex = 8;
            this.numHeight.Value = new decimal(new int[] {
            115,
            0,
            0,
            0});
            // 
            // lblSize2
            // 
            this.lblSize2.AutoSize = true;
            this.lblSize2.Location = new System.Drawing.Point(346, 123);
            this.lblSize2.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblSize2.Name = "lblSize2";
            this.lblSize2.Size = new System.Drawing.Size(41, 13);
            this.lblSize2.TabIndex = 13;
            this.lblSize2.Text = "Height:";
            // 
            // lblSize1
            // 
            this.lblSize1.AutoSize = true;
            this.lblSize1.Location = new System.Drawing.Point(346, 102);
            this.lblSize1.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblSize1.Name = "lblSize1";
            this.lblSize1.Size = new System.Drawing.Size(38, 13);
            this.lblSize1.TabIndex = 14;
            this.lblSize1.Text = "Width:";
            // 
            // btnGithub
            // 
            this.btnGithub.BackgroundImage = global::TwilightBoxart.UX.Properties.Resources.GitHub_Mark_64px;
            this.btnGithub.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnGithub.Location = new System.Drawing.Point(6, 337);
            this.btnGithub.Margin = new System.Windows.Forms.Padding(2);
            this.btnGithub.Name = "btnGithub";
            this.btnGithub.Size = new System.Drawing.Size(29, 30);
            this.btnGithub.TabIndex = 11;
            this.toolTip.SetToolTip(this.btnGithub, "Visit the Github Repository.");
            this.btnGithub.UseVisualStyleBackColor = true;
            this.btnGithub.Click += new System.EventHandler(this.btnGithub_Click);
            // 
            // chkKeepAspectRatio
            // 
            this.chkKeepAspectRatio.AutoSize = true;
            this.chkKeepAspectRatio.Checked = true;
            this.chkKeepAspectRatio.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkKeepAspectRatio.Location = new System.Drawing.Point(69, 121);
            this.chkKeepAspectRatio.Name = "chkKeepAspectRatio";
            this.chkKeepAspectRatio.Size = new System.Drawing.Size(221, 17);
            this.chkKeepAspectRatio.TabIndex = 15;
            this.chkKeepAspectRatio.Text = "Keep original aspect ratio (recommended)";
            this.toolTip.SetToolTip(this.chkKeepAspectRatio, "Will resize the boxart to the exact defined size (NOT recommended)");
            this.chkKeepAspectRatio.UseVisualStyleBackColor = true;
            this.chkKeepAspectRatio.CheckedChanged += new System.EventHandler(this.chkKeepAspectRatio_CheckedChanged);
            // 
            // rbtDefault
            // 
            this.rbtDefault.AutoSize = true;
            this.rbtDefault.Checked = true;
            this.rbtDefault.Location = new System.Drawing.Point(69, 100);
            this.rbtDefault.Name = "rbtDefault";
            this.rbtDefault.Size = new System.Drawing.Size(58, 17);
            this.rbtDefault.TabIndex = 16;
            this.rbtDefault.TabStop = true;
            this.rbtDefault.Text = "Classic";
            this.rbtDefault.UseVisualStyleBackColor = true;
            this.rbtDefault.CheckedChanged += new System.EventHandler(this.rbtDefault_CheckedChanged);
            // 
            // lblSize
            // 
            this.lblSize.AutoSize = true;
            this.lblSize.Location = new System.Drawing.Point(3, 100);
            this.lblSize.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblSize.Name = "lblSize";
            this.lblSize.Size = new System.Drawing.Size(61, 13);
            this.lblSize.TabIndex = 18;
            this.lblSize.Text = "Boxart size:";
            // 
            // rbtLarge
            // 
            this.rbtLarge.AutoSize = true;
            this.rbtLarge.Location = new System.Drawing.Point(177, 100);
            this.rbtLarge.Name = "rbtLarge";
            this.rbtLarge.Size = new System.Drawing.Size(52, 17);
            this.rbtLarge.TabIndex = 19;
            this.rbtLarge.Text = "Large";
            this.rbtLarge.UseVisualStyleBackColor = true;
            this.rbtLarge.CheckedChanged += new System.EventHandler(this.rbtLarge_CheckedChanged);
            // 
            // rbtCustom
            // 
            this.rbtCustom.AutoSize = true;
            this.rbtCustom.Location = new System.Drawing.Point(281, 100);
            this.rbtCustom.Name = "rbtCustom";
            this.rbtCustom.Size = new System.Drawing.Size(60, 17);
            this.rbtCustom.TabIndex = 21;
            this.rbtCustom.Text = "Custom";
            this.rbtCustom.UseVisualStyleBackColor = true;
            this.rbtCustom.CheckedChanged += new System.EventHandler(this.rbtCustom_CheckedChanged);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(514, 376);
            this.Controls.Add(this.rbtCustom);
            this.Controls.Add(this.rbtLarge);
            this.Controls.Add(this.lblSize);
            this.Controls.Add(this.rbtDefault);
            this.Controls.Add(this.chkKeepAspectRatio);
            this.Controls.Add(this.btnGithub);
            this.Controls.Add(this.lblSize1);
            this.Controls.Add(this.lblSize2);
            this.Controls.Add(this.numHeight);
            this.Controls.Add(this.numWidth);
            this.Controls.Add(this.btnStart);
            this.Controls.Add(this.btnDetect);
            this.Controls.Add(this.chkManualBoxartLocation);
            this.Controls.Add(this.btnBrowseBoxart);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.txtBoxart);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.txtSdRoot);
            this.Controls.Add(this.btnBrowseSd);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(2);
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.Text = "TwilightBoxart";
            this.Load += new System.EventHandler(this.MainForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numWidth)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numHeight)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnBrowseSd;
        private System.Windows.Forms.TextBox txtSdRoot;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.TextBox txtBoxart;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button btnBrowseBoxart;
        private System.Windows.Forms.CheckBox chkManualBoxartLocation;
        private System.Windows.Forms.Button btnDetect;
        private System.Windows.Forms.FolderBrowserDialog folderBrowseDlg;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.NumericUpDown numWidth;
        private System.Windows.Forms.NumericUpDown numHeight;
        private System.Windows.Forms.Label lblSize2;
        private System.Windows.Forms.Label lblSize1;
        private System.Windows.Forms.Button btnGithub;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.CheckBox chkKeepAspectRatio;
        private System.Windows.Forms.RadioButton rbtDefault;
        private System.Windows.Forms.Label lblSize;
        private System.Windows.Forms.RadioButton rbtLarge;
        private System.Windows.Forms.RadioButton rbtCustom;
    }
}

