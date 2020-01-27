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
            this.btnGithub = new System.Windows.Forms.Button();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.chkKeepAspectRatio = new System.Windows.Forms.CheckBox();
            this.chkBorder = new System.Windows.Forms.CheckBox();
            this.chkBorderThick = new System.Windows.Forms.CheckBox();
            this.chkOverwriteExisting = new System.Windows.Forms.CheckBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.rbtFullscreen = new System.Windows.Forms.RadioButton();
            this.rbtCustom = new System.Windows.Forms.RadioButton();
            this.rbtLarge = new System.Windows.Forms.RadioButton();
            this.rbtDefault = new System.Windows.Forms.RadioButton();
            this.lblSize1 = new System.Windows.Forms.Label();
            this.lblSize2 = new System.Windows.Forms.Label();
            this.numHeight = new System.Windows.Forms.NumericUpDown();
            this.numWidth = new System.Windows.Forms.NumericUpDown();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.rbtBorder3DS = new System.Windows.Forms.RadioButton();
            this.rbtBorderDSi = new System.Windows.Forms.RadioButton();
            this.rbtBorderWhite = new System.Windows.Forms.RadioButton();
            this.rbtBorderBlack = new System.Windows.Forms.RadioButton();
            this.colorDialog = new System.Windows.Forms.ColorDialog();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numHeight)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numWidth)).BeginInit();
            this.groupBox2.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnBrowseSd
            // 
            this.btnBrowseSd.Location = new System.Drawing.Point(698, 35);
            this.btnBrowseSd.Margin = new System.Windows.Forms.Padding(4);
            this.btnBrowseSd.Name = "btnBrowseSd";
            this.btnBrowseSd.Size = new System.Drawing.Size(152, 58);
            this.btnBrowseSd.TabIndex = 2;
            this.btnBrowseSd.Text = "Browse...";
            this.btnBrowseSd.UseVisualStyleBackColor = true;
            this.btnBrowseSd.Click += new System.EventHandler(this.btnBrowseSd_Click);
            // 
            // txtSdRoot
            // 
            this.txtSdRoot.BackColor = System.Drawing.SystemColors.ControlLightLight;
            this.txtSdRoot.Location = new System.Drawing.Point(12, 46);
            this.txtSdRoot.Margin = new System.Windows.Forms.Padding(4);
            this.txtSdRoot.Name = "txtSdRoot";
            this.txtSdRoot.Size = new System.Drawing.Size(674, 31);
            this.txtSdRoot.TabIndex = 1;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(6, 17);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(252, 25);
            this.label1.TabIndex = 2;
            this.label1.Text = "SD Root / Roms location:";
            // 
            // txtLog
            // 
            this.txtLog.BackColor = System.Drawing.SystemColors.ControlLightLight;
            this.txtLog.Location = new System.Drawing.Point(8, 404);
            this.txtLog.Margin = new System.Windows.Forms.Padding(6);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(996, 360);
            this.txtLog.TabIndex = 13;
            // 
            // txtBoxart
            // 
            this.txtBoxart.BackColor = System.Drawing.SystemColors.ControlLightLight;
            this.txtBoxart.Location = new System.Drawing.Point(12, 137);
            this.txtBoxart.Margin = new System.Windows.Forms.Padding(4);
            this.txtBoxart.Name = "txtBoxart";
            this.txtBoxart.ReadOnly = true;
            this.txtBoxart.Size = new System.Drawing.Size(674, 31);
            this.txtBoxart.TabIndex = 4;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(6, 108);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(161, 25);
            this.label2.TabIndex = 5;
            this.label2.Text = "Boxart location:";
            // 
            // btnBrowseBoxart
            // 
            this.btnBrowseBoxart.Enabled = false;
            this.btnBrowseBoxart.Location = new System.Drawing.Point(698, 125);
            this.btnBrowseBoxart.Margin = new System.Windows.Forms.Padding(4);
            this.btnBrowseBoxart.Name = "btnBrowseBoxart";
            this.btnBrowseBoxart.Size = new System.Drawing.Size(152, 58);
            this.btnBrowseBoxart.TabIndex = 5;
            this.btnBrowseBoxart.Text = "Browse...";
            this.btnBrowseBoxart.UseVisualStyleBackColor = true;
            this.btnBrowseBoxart.Click += new System.EventHandler(this.btnBrowseBoxart_Click);
            // 
            // chkManualBoxartLocation
            // 
            this.chkManualBoxartLocation.AutoSize = true;
            this.chkManualBoxartLocation.Location = new System.Drawing.Point(860, 137);
            this.chkManualBoxartLocation.Margin = new System.Windows.Forms.Padding(6);
            this.chkManualBoxartLocation.Name = "chkManualBoxartLocation";
            this.chkManualBoxartLocation.Size = new System.Drawing.Size(156, 29);
            this.chkManualBoxartLocation.TabIndex = 6;
            this.chkManualBoxartLocation.Text = "Set Manually";
            this.chkManualBoxartLocation.UseVisualStyleBackColor = true;
            this.chkManualBoxartLocation.CheckedChanged += new System.EventHandler(this.chkManualBoxartLocation_CheckedChanged);
            // 
            // btnDetect
            // 
            this.btnDetect.Location = new System.Drawing.Point(858, 35);
            this.btnDetect.Margin = new System.Windows.Forms.Padding(4);
            this.btnDetect.Name = "btnDetect";
            this.btnDetect.Size = new System.Drawing.Size(152, 58);
            this.btnDetect.TabIndex = 3;
            this.btnDetect.Text = "Detect SD";
            this.toolTip.SetToolTip(this.btnDetect, "Will try to detect your (Twilight++) SD card");
            this.btnDetect.UseVisualStyleBackColor = true;
            this.btnDetect.Click += new System.EventHandler(this.btnDetect_Click);
            // 
            // btnStart
            // 
            this.btnStart.Enabled = false;
            this.btnStart.Location = new System.Drawing.Point(856, 777);
            this.btnStart.Margin = new System.Windows.Forms.Padding(4);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(152, 58);
            this.btnStart.TabIndex = 21;
            this.btnStart.Text = "Start";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            // 
            // btnGithub
            // 
            this.btnGithub.BackgroundImage = global::TwilightBoxart.UX.Properties.Resources.GitHub_Mark_64px;
            this.btnGithub.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnGithub.Location = new System.Drawing.Point(8, 777);
            this.btnGithub.Margin = new System.Windows.Forms.Padding(4);
            this.btnGithub.Name = "btnGithub";
            this.btnGithub.Size = new System.Drawing.Size(58, 58);
            this.btnGithub.TabIndex = 19;
            this.toolTip.SetToolTip(this.btnGithub, "Visit the Github Repository.");
            this.btnGithub.UseVisualStyleBackColor = true;
            this.btnGithub.Click += new System.EventHandler(this.btnGithub_Click);
            // 
            // chkKeepAspectRatio
            // 
            this.chkKeepAspectRatio.AutoSize = true;
            this.chkKeepAspectRatio.Checked = true;
            this.chkKeepAspectRatio.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkKeepAspectRatio.Location = new System.Drawing.Point(12, 73);
            this.chkKeepAspectRatio.Margin = new System.Windows.Forms.Padding(6);
            this.chkKeepAspectRatio.Name = "chkKeepAspectRatio";
            this.chkKeepAspectRatio.Size = new System.Drawing.Size(431, 29);
            this.chkKeepAspectRatio.TabIndex = 12;
            this.chkKeepAspectRatio.Text = "Keep original aspect ratio (recommended)";
            this.toolTip.SetToolTip(this.chkKeepAspectRatio, "Enabling this will keep the original aspect ratio for much better boxart sizes. T" +
        "his will only work on the latest (2020) TwilightMenu++ releases.");
            this.chkKeepAspectRatio.UseVisualStyleBackColor = true;
            this.chkKeepAspectRatio.CheckedChanged += new System.EventHandler(this.chkKeepAspectRatio_CheckedChanged);
            // 
            // chkBorder
            // 
            this.chkBorder.AutoSize = true;
            this.chkBorder.Location = new System.Drawing.Point(10, 34);
            this.chkBorder.Margin = new System.Windows.Forms.Padding(6);
            this.chkBorder.Name = "chkBorder";
            this.chkBorder.Size = new System.Drawing.Size(149, 29);
            this.chkBorder.TabIndex = 14;
            this.chkBorder.Text = "Add border?";
            this.toolTip.SetToolTip(this.chkBorder, "Enabling this will add a small border around the boxart for a more aesthetic look" +
        ".");
            this.chkBorder.UseVisualStyleBackColor = true;
            this.chkBorder.CheckedChanged += new System.EventHandler(this.chkBorder_CheckedChanged);
            // 
            // chkBorderThick
            // 
            this.chkBorderThick.AutoSize = true;
            this.chkBorderThick.Enabled = false;
            this.chkBorderThick.Location = new System.Drawing.Point(803, 33);
            this.chkBorderThick.Margin = new System.Windows.Forms.Padding(6);
            this.chkBorderThick.Name = "chkBorderThick";
            this.chkBorderThick.Size = new System.Drawing.Size(170, 29);
            this.chkBorderThick.TabIndex = 18;
            this.chkBorderThick.Text = "Thicker border";
            this.toolTip.SetToolTip(this.chkBorderThick, "Enabling this make the border 1px thicker.");
            this.chkBorderThick.UseVisualStyleBackColor = true;
            // 
            // chkOverwriteExisting
            // 
            this.chkOverwriteExisting.AutoSize = true;
            this.chkOverwriteExisting.Location = new System.Drawing.Point(553, 793);
            this.chkOverwriteExisting.Margin = new System.Windows.Forms.Padding(6);
            this.chkOverwriteExisting.Name = "chkOverwriteExisting";
            this.chkOverwriteExisting.Size = new System.Drawing.Size(280, 29);
            this.chkOverwriteExisting.TabIndex = 20;
            this.chkOverwriteExisting.Text = "Overwrite existing boxart?";
            this.toolTip.SetToolTip(this.chkOverwriteExisting, "Enabling this will add a small border around the boxart for a more aesthetic look" +
        ".");
            this.chkOverwriteExisting.UseVisualStyleBackColor = true;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.rbtFullscreen);
            this.groupBox1.Controls.Add(this.rbtCustom);
            this.groupBox1.Controls.Add(this.rbtLarge);
            this.groupBox1.Controls.Add(this.rbtDefault);
            this.groupBox1.Controls.Add(this.chkKeepAspectRatio);
            this.groupBox1.Controls.Add(this.lblSize1);
            this.groupBox1.Controls.Add(this.lblSize2);
            this.groupBox1.Controls.Add(this.numHeight);
            this.groupBox1.Controls.Add(this.numWidth);
            this.groupBox1.Location = new System.Drawing.Point(12, 192);
            this.groupBox1.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox1.Size = new System.Drawing.Size(996, 119);
            this.groupBox1.TabIndex = 21;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Boxart Size:";
            // 
            // rbtFullscreen
            // 
            this.rbtFullscreen.AutoSize = true;
            this.rbtFullscreen.Location = new System.Drawing.Point(346, 33);
            this.rbtFullscreen.Margin = new System.Windows.Forms.Padding(6);
            this.rbtFullscreen.Name = "rbtFullscreen";
            this.rbtFullscreen.Size = new System.Drawing.Size(130, 29);
            this.rbtFullscreen.TabIndex = 9;
            this.rbtFullscreen.Text = "Fullscreen";
            this.rbtFullscreen.UseVisualStyleBackColor = true;
            this.rbtFullscreen.CheckedChanged += new System.EventHandler(this.rbtFullscreen_CheckedChanged);
            // 
            // rbtCustom
            // 
            this.rbtCustom.AutoSize = true;
            this.rbtCustom.Location = new System.Drawing.Point(544, 33);
            this.rbtCustom.Margin = new System.Windows.Forms.Padding(6);
            this.rbtCustom.Name = "rbtCustom";
            this.rbtCustom.Size = new System.Drawing.Size(103, 29);
            this.rbtCustom.TabIndex = 10;
            this.rbtCustom.Text = "Custom";
            this.rbtCustom.UseVisualStyleBackColor = true;
            this.rbtCustom.CheckedChanged += new System.EventHandler(this.rbtCustom_CheckedChanged);
            // 
            // rbtLarge
            // 
            this.rbtLarge.AutoSize = true;
            this.rbtLarge.Location = new System.Drawing.Point(182, 33);
            this.rbtLarge.Margin = new System.Windows.Forms.Padding(6);
            this.rbtLarge.Name = "rbtLarge";
            this.rbtLarge.Size = new System.Drawing.Size(85, 29);
            this.rbtLarge.TabIndex = 8;
            this.rbtLarge.Text = "Large";
            this.rbtLarge.UseVisualStyleBackColor = true;
            this.rbtLarge.CheckedChanged += new System.EventHandler(this.rbtLarge_CheckedChanged);
            // 
            // rbtDefault
            // 
            this.rbtDefault.AutoSize = true;
            this.rbtDefault.Checked = true;
            this.rbtDefault.Location = new System.Drawing.Point(12, 33);
            this.rbtDefault.Margin = new System.Windows.Forms.Padding(6);
            this.rbtDefault.Name = "rbtDefault";
            this.rbtDefault.Size = new System.Drawing.Size(100, 29);
            this.rbtDefault.TabIndex = 7;
            this.rbtDefault.TabStop = true;
            this.rbtDefault.Text = "Classic";
            this.rbtDefault.UseVisualStyleBackColor = true;
            this.rbtDefault.CheckedChanged += new System.EventHandler(this.rbtDefault_CheckedChanged);
            // 
            // lblSize1
            // 
            this.lblSize1.AutoSize = true;
            this.lblSize1.Location = new System.Drawing.Point(682, 37);
            this.lblSize1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblSize1.Name = "lblSize1";
            this.lblSize1.Size = new System.Drawing.Size(73, 25);
            this.lblSize1.TabIndex = 27;
            this.lblSize1.Text = "Width:";
            // 
            // lblSize2
            // 
            this.lblSize2.AutoSize = true;
            this.lblSize2.Location = new System.Drawing.Point(682, 79);
            this.lblSize2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblSize2.Name = "lblSize2";
            this.lblSize2.Size = new System.Drawing.Size(80, 25);
            this.lblSize2.TabIndex = 26;
            this.lblSize2.Text = "Height:";
            // 
            // numHeight
            // 
            this.numHeight.Enabled = false;
            this.numHeight.Location = new System.Drawing.Point(848, 73);
            this.numHeight.Margin = new System.Windows.Forms.Padding(6);
            this.numHeight.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.numHeight.Name = "numHeight";
            this.numHeight.Size = new System.Drawing.Size(138, 31);
            this.numHeight.TabIndex = 13;
            this.numHeight.Value = new decimal(new int[] {
            115,
            0,
            0,
            0});
            // 
            // numWidth
            // 
            this.numWidth.Enabled = false;
            this.numWidth.Location = new System.Drawing.Point(846, 33);
            this.numWidth.Margin = new System.Windows.Forms.Padding(6);
            this.numWidth.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.numWidth.Name = "numWidth";
            this.numWidth.Size = new System.Drawing.Size(140, 31);
            this.numWidth.TabIndex = 11;
            this.numWidth.Value = new decimal(new int[] {
            128,
            0,
            0,
            0});
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.rbtBorder3DS);
            this.groupBox2.Controls.Add(this.rbtBorderDSi);
            this.groupBox2.Controls.Add(this.chkBorderThick);
            this.groupBox2.Controls.Add(this.rbtBorderWhite);
            this.groupBox2.Controls.Add(this.rbtBorderBlack);
            this.groupBox2.Controls.Add(this.chkBorder);
            this.groupBox2.Location = new System.Drawing.Point(14, 319);
            this.groupBox2.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox2.Size = new System.Drawing.Size(996, 75);
            this.groupBox2.TabIndex = 22;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Extra:";
            // 
            // rbtBorder3DS
            // 
            this.rbtBorder3DS.AutoSize = true;
            this.rbtBorder3DS.Enabled = false;
            this.rbtBorder3DS.Location = new System.Drawing.Point(194, 32);
            this.rbtBorder3DS.Margin = new System.Windows.Forms.Padding(6);
            this.rbtBorder3DS.Name = "rbtBorder3DS";
            this.rbtBorder3DS.Size = new System.Drawing.Size(143, 29);
            this.rbtBorder3DS.TabIndex = 19;
            this.rbtBorder3DS.Text = "3DS Theme";
            this.rbtBorder3DS.UseVisualStyleBackColor = true;
            this.rbtBorder3DS.Visible = false;
            this.rbtBorder3DS.CheckedChanged += new System.EventHandler(this.rbtBorder3DS_CheckedChanged);
            // 
            // rbtBorderDSi
            // 
            this.rbtBorderDSi.AutoSize = true;
            this.rbtBorderDSi.Checked = true;
            this.rbtBorderDSi.Enabled = false;
            this.rbtBorderDSi.Location = new System.Drawing.Point(366, 32);
            this.rbtBorderDSi.Margin = new System.Windows.Forms.Padding(6);
            this.rbtBorderDSi.Name = "rbtBorderDSi";
            this.rbtBorderDSi.Size = new System.Drawing.Size(136, 29);
            this.rbtBorderDSi.TabIndex = 15;
            this.rbtBorderDSi.TabStop = true;
            this.rbtBorderDSi.Text = "DSi Theme";
            this.rbtBorderDSi.UseVisualStyleBackColor = true;
            this.rbtBorderDSi.CheckedChanged += new System.EventHandler(this.rbtDSi_CheckedChanged);
            // 
            // rbtBorderWhite
            // 
            this.rbtBorderWhite.AutoSize = true;
            this.rbtBorderWhite.Enabled = false;
            this.rbtBorderWhite.Location = new System.Drawing.Point(684, 32);
            this.rbtBorderWhite.Margin = new System.Windows.Forms.Padding(6);
            this.rbtBorderWhite.Name = "rbtBorderWhite";
            this.rbtBorderWhite.Size = new System.Drawing.Size(85, 29);
            this.rbtBorderWhite.TabIndex = 17;
            this.rbtBorderWhite.Text = "White";
            this.rbtBorderWhite.UseVisualStyleBackColor = true;
            // 
            // rbtBorderBlack
            // 
            this.rbtBorderBlack.AutoSize = true;
            this.rbtBorderBlack.Enabled = false;
            this.rbtBorderBlack.Location = new System.Drawing.Point(542, 32);
            this.rbtBorderBlack.Margin = new System.Windows.Forms.Padding(6);
            this.rbtBorderBlack.Name = "rbtBorderBlack";
            this.rbtBorderBlack.Size = new System.Drawing.Size(83, 29);
            this.rbtBorderBlack.TabIndex = 16;
            this.rbtBorderBlack.Text = "Black";
            this.rbtBorderBlack.UseVisualStyleBackColor = true;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 25F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1028, 846);
            this.Controls.Add(this.chkOverwriteExisting);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.btnGithub);
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
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.Text = "TwilightBoxart";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numHeight)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numWidth)).EndInit();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
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
        private System.Windows.Forms.Button btnGithub;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.RadioButton rbtCustom;
        private System.Windows.Forms.RadioButton rbtLarge;
        private System.Windows.Forms.RadioButton rbtDefault;
        private System.Windows.Forms.CheckBox chkKeepAspectRatio;
        private System.Windows.Forms.Label lblSize1;
        private System.Windows.Forms.Label lblSize2;
        private System.Windows.Forms.NumericUpDown numHeight;
        private System.Windows.Forms.NumericUpDown numWidth;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.RadioButton rbtBorderWhite;
        private System.Windows.Forms.RadioButton rbtBorderBlack;
        private System.Windows.Forms.CheckBox chkBorder;
        private System.Windows.Forms.ColorDialog colorDialog;
        private System.Windows.Forms.CheckBox chkBorderThick;
        private System.Windows.Forms.RadioButton rbtBorderDSi;
        private System.Windows.Forms.CheckBox chkOverwriteExisting;
        private System.Windows.Forms.RadioButton rbtFullscreen;
        private System.Windows.Forms.RadioButton rbtBorder3DS;
    }
}

