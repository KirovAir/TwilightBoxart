using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TwilightBoxart.UX
{
    public partial class MainForm : Form
    {
        private readonly BoxartCrawler _crawler;
        private bool _isInitialized;
        private bool _isRunning;

        public MainForm()
        {
            InitializeComponent();
            var progress = new Progress<string>(Log);
            _crawler = new BoxartCrawler(progress);
        }

        private void DetectSd()
        {
            var path = "";
            var allDrives = new List<DriveInfo>();
            try
            {
                allDrives = DriveInfo.GetDrives().Where(c => c.DriveType == DriveType.Removable).ToList();
            }
            catch { }

            if (allDrives.Count > 0)
            {
                path = allDrives[0].RootDirectory.FullName;
                foreach (var drive in allDrives)
                {
                    if (Directory.Exists(Path.Combine(drive.RootDirectory.FullName, "_nds")))
                    {
                        path = drive.RootDirectory.FullName;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(path))
            {
                txtSdRoot.Text = path;
                SetUx();
            }
        }

        // UI STUFF
        private void SetUx()
        {
            var defaults = new BoxartConfig();

            btnBrowseBoxart.Enabled = chkManualBoxartLocation.Checked;

            if (!chkManualBoxartLocation.Checked && !string.IsNullOrEmpty(txtSdRoot.Text))
            {
                txtBoxart.Text = Path.Combine(txtSdRoot.Text, defaults.BoxartPath);
            }

            numHeight.Visible = chkBoxartSize.Checked;
            numWidth.Visible = chkBoxartSize.Checked;
            lblSize1.Visible = chkBoxartSize.Checked;
            lblSize2.Visible = chkBoxartSize.Checked;

            if (!chkBoxartSize.Checked)
            {
                numWidth.Value = defaults.BoxartWidth;
                numHeight.Value = defaults.BoxartHeight;
            }

            btnStart.Enabled = !string.IsNullOrEmpty(txtSdRoot.Text) && !string.IsNullOrEmpty(txtBoxart.Text) && _isInitialized && !_isRunning;
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            _isRunning = true;
            SetUx();
            Task.Run(Go);
        }

        private void Go()
        {
            _crawler.DownloadArt(txtSdRoot.Text, txtBoxart.Text, (int)numWidth.Value, (int)numHeight.Value);
            _isRunning = false;
            this.UIThread(SetUx);
        }

        private void btnBrowseSd_Click(object sender, EventArgs e)
        {
            if (folderBrowseDlg.ShowDialog() != DialogResult.OK) return;

            txtSdRoot.Text = folderBrowseDlg.SelectedPath;
            SetUx();
        }

        private void btnDetect_Click(object sender, EventArgs e)
        {
            DetectSd();
        }

        private void chkManualBoxartLocation_CheckedChanged(object sender, EventArgs e)
        {
            SetUx();
        }

        private void btnBrowseBoxart_Click(object sender, EventArgs e)
        {
            if (folderBrowseDlg.ShowDialog() == DialogResult.OK)
            {
                txtBoxart.Text = folderBrowseDlg.SelectedPath;
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            DetectSd();
            Task.Run(() =>
            {
                try
                {
                    _crawler.InitializeDb();
                }
                catch (Exception ex)
                {
                    Log("Warning: Could not initialize NoIntro DB! Only DS Roms will work. Error: " + ex);
                }

                _isInitialized = true;
                this.UIThread(SetUx);
            });
        }

        private void Log(string text)
        {
            this.UIThread(() => txtLog.AppendText($"{DateTime.Now:HH:mm:ss} - {text}{Environment.NewLine}"));
        }

        private void chkBoxartSize_CheckedChanged(object sender, EventArgs e)
        {
            SetUx();
        }
    }
}
