using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TwilightBoxart.Helpers;

namespace TwilightBoxart.UX
{
    public partial class MainForm : Form
    {
        private readonly BoxartCrawler _crawler;
        private bool _isRunning;
        private readonly BoxartConfig _config = new BoxartConfig();

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
                    if (Directory.Exists(Path.Combine(drive.RootDirectory.FullName, BoxartConfig.MagicDir)))
                    {
                        path = drive.RootDirectory.FullName;
                        break;
                    }
                }
            }
            else
            {
                Log("No SD card(s) found.");
            }

            if (!string.IsNullOrEmpty(path))
            {
                txtSdRoot.Text = path;
                SetUx();
            }
        }

        private void Log(string text)
        {
            this.UIThread(() => txtLog.AppendText($"{DateTime.Now:HH:mm:ss} - {text}{Environment.NewLine}"));
        }

        // UI STUFF
        private void SetUx()
        {
            btnBrowseBoxart.Enabled = chkManualBoxartLocation.Checked;

            if (!chkManualBoxartLocation.Checked && !string.IsNullOrEmpty(txtSdRoot.Text))
            {
                txtBoxart.Text = _config.GetBoxartPath(txtSdRoot.Text);
            }

            txtBoxart.ReadOnly = !chkManualBoxartLocation.Checked;

            numHeight.Enabled = rbtCustom.Checked;
            numWidth.Enabled = rbtCustom.Checked;

            rbtBorderWhite.Enabled = chkBorder.Checked;
            rbtBorderBlack.Enabled = chkBorder.Checked;
            rbtBorderDSi.Enabled = chkBorder.Checked;
            rbtBorder3DS.Enabled = chkBorder.Checked;

            chkBorderThick.Enabled = rbtBorderWhite.Checked || rbtBorderBlack.Checked;

            btnStart.Enabled = !string.IsNullOrEmpty(txtSdRoot.Text) && !string.IsNullOrEmpty(txtBoxart.Text);
            btnStart.Text = _isRunning ? "Stop" : "Start";
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            Log($"{Text} - Version {BoxartConfig.Version}."); // Display version in log.

            try
            {
                if (File.Exists(BoxartConfig.FileName))
                {
                    _config.Load();
                    if (_config.BoxartWidth > 0 && _config.BoxartHeight > 0)
                    {
                        numWidth.Value = _config.BoxartWidth;
                        numHeight.Value = _config.BoxartHeight;
                    }
                    Log($"Loaded {BoxartConfig.FileName}.");
                }
            }
            catch
            {
                Log($"Error while loading {BoxartConfig.FileName}. Using defaults.");
            }

            if (!string.IsNullOrEmpty(_config.SdRoot))
            {
                txtSdRoot.Text = _config.SdRoot;
            }
            else
            {
                DetectSd();
            }

            if (!_config.DisableUpdates)
            {
                Task.Run(UpdateCheck);
            }

            SetUx();
        }

        public void UpdateCheck()
        {
            try
            {
                var update = GithubClient.GetNewRelease(BoxartConfig.Repository, BoxartConfig.Version);

                if (update != null)
                {
                    var result = MessageBox.Show(update.UpdateText,
                        "A new update is available!",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        OSHelper.OpenBrowser(BoxartConfig.RepositoryReleasesUrl);
                    }
                }
            }
            catch (Exception e)
            {
                Log($"An error occured while looking for updates. {e.Message}");
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (!_isRunning)
            {
                _isRunning = true;
                SetUx();
                Task.Run(Go);
            }
            else
            {
                btnStart.Enabled = false;
                _crawler.Stop();
            }
        }

        private void Go()
        {
            var config = new BoxartConfig
            {
                SdRoot = txtSdRoot.Text,
                BoxartPath = txtBoxart.Text,
                BoxartWidth = (int) numWidth.Value,
                BoxartHeight = (int) numHeight.Value,
                KeepAspectRatio = chkKeepAspectRatio.Checked,
                OverwriteExisting = chkOverwriteExisting.Checked,
                BoxartBorderColor = rbtBorderBlack.Checked ? 0xFF000000 : 0xFFFFFFFF, // Todo Check
                BoxartBorderThickness = chkBorderThick.Checked ? 2 : 1,
                BoxartBorderStyle = chkBorder.Checked ? rbtBorder3DS.Checked ? BoxartBorderStyle.Nintendo3DS : rbtBorderDSi.Checked ? BoxartBorderStyle.NintendoDSi : BoxartBorderStyle.Line : BoxartBorderStyle.None
            };

            _crawler.DownloadArt(config).Wait();

            _isRunning = false;
            this.UIThread(SetUx);
        }

        private void btnBrowseSd_Click(object sender, EventArgs e)
        {
            if (folderBrowseDlg.ShowDialog() != DialogResult.OK) return;

            txtSdRoot.Text = folderBrowseDlg.SelectedPath;
            SetUx();
        }

        private void btnBrowseBoxart_Click(object sender, EventArgs e)
        {
            if (folderBrowseDlg.ShowDialog() == DialogResult.OK)
            {
                txtBoxart.Text = folderBrowseDlg.SelectedPath;
            }
        }

        private void btnDetect_Click(object sender, EventArgs e)
        {
            DetectSd();
        }

        private void chkManualBoxartLocation_CheckedChanged(object sender, EventArgs e)
        {
            SetUx();
        }

        private void btnGithub_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(BoxartConfig.Credits + Environment.NewLine + Environment.NewLine + "Visit Github now?", "Hello",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) == DialogResult.OK)
            {
                OSHelper.OpenBrowser(BoxartConfig.RepositoryUrl);
            }
        }

        private void rbtCustom_CheckedChanged(object sender, EventArgs e)
        {
            SetUx();
        }

        private void rbtLarge_CheckedChanged(object sender, EventArgs e)
        {
            numWidth.Value = 210;   // 200
            numHeight.Value = 146;  // 180
            SetUx();
        }

        private void rbtDefault_CheckedChanged(object sender, EventArgs e)
        {
            numWidth.Value = 128;
            numHeight.Value = 115;
            SetUx();
        }

        private void rbtFullscreen_CheckedChanged(object sender, EventArgs e)
        {
            numWidth.Value = 256;
            numHeight.Value = 192;
            SetUx();
        }

        private void chkKeepAspectRatio_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkKeepAspectRatio.Checked)
            {
                MessageBox.Show("Warning: Disabling this might give ugly results when searching for mixed boxart types (For example: SNES and NDS) as they will be resized to the exact defined measurements.", "Keep aspect ratio");
            }
        }

        private void chkBorder_CheckedChanged(object sender, EventArgs e)
        {
            SetUx();
        }

        private void rbtDSi_CheckedChanged(object sender, EventArgs e)
        {
            SetUx();
        }

        private void rbtBorder3DS_CheckedChanged(object sender, EventArgs e)
        {
            SetUx();
        }
    }
}
