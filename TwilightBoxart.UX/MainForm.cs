using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using KirovAir.Core.Utilities;
using TwilightBoxart.Helpers;

namespace TwilightBoxart.UX
{
    public partial class MainForm : Form
    {
        private readonly BoxartCrawler _crawler;
        private bool _isInitialized;
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

        private void Go()
        {
            _crawler.DownloadArt(txtSdRoot.Text, txtBoxart.Text, (int)numWidth.Value, (int)numHeight.Value, chkKeepAspectRatio.Checked);
            _isRunning = false;
            this.UIThread(SetUx);
        }

        // UI STUFF
        private void SetUx()
        {
            if (!_isInitialized)
                return;

            btnBrowseBoxart.Enabled = chkManualBoxartLocation.Checked;

            if (!chkManualBoxartLocation.Checked && !string.IsNullOrEmpty(txtSdRoot.Text))
            {
                txtBoxart.Text = _config.GetBoxartPath(txtSdRoot.Text);
            }

            txtBoxart.ReadOnly = !chkManualBoxartLocation.Checked;

            numHeight.Enabled = rbtCustom.Checked;
            numWidth.Enabled = rbtCustom.Checked;

            btnStart.Enabled = !string.IsNullOrEmpty(txtSdRoot.Text) && !string.IsNullOrEmpty(txtBoxart.Text) && _isInitialized && !_isRunning;
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

            Task.Run(() =>
            {
                try
                {
                    _crawler.InitializeDb();
                }
                catch (Exception ex)
                {
                    var errorMsg = "Warning: Could not initialize NoIntro DB! Only DS boxart will be downloaded!" + Environment.NewLine +
                                   "Try to delete NoIntro.db and restart TwilightBoxart.";
                    Log($"{errorMsg}  Error: {ex}");
                    this.UIThread(() => MessageBox.Show(errorMsg, "Oh no!"));
                }

                _isInitialized = true;
                this.UIThread(SetUx);
            });


            if (!_config.DisableUpdates)
            {
                Task.Run(UpdateCheck);
            }
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
            _isRunning = true;
            SetUx();
            Task.Run(Go);
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
            numWidth.Value = 256;
            numHeight.Value = 192;
            SetUx();
        }

        private void rbtDefault_CheckedChanged(object sender, EventArgs e)
        {
            numWidth.Value = 128;
            numHeight.Value = 115;
            SetUx();
        }

        private void chkKeepAspectRatio_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkKeepAspectRatio.Checked)
            {
                MessageBox.Show("Warning: Disabling this might give ugly results when searching for mixed boxart types (For example: SNES and NDS) as they will be resized to the exact defined measurements.", "Keep aspect ratio");
            }
        }
    }
}
