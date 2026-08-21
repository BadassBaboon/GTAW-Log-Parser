using System;
using System.IO;
using System.Diagnostics;
using GTAWParser.Shared;
using Parser.Controllers;
using Parser.Localization;
using System.Windows.Forms;

namespace Parser.UI
{
    public partial class Main : Form
    {
        private string _previousLog = string.Empty;

        /// <summary>
        /// Initializes the main user form
        /// </summary>
        public Main()
        {
            InitializeComponent();

            LoadSettings();
            SetupServerList();
        }

        /// <summary>
        /// Adds menu options under "Server" on the menu
        /// strip for each Language in LocalizationController
        /// </summary>
        private void SetupServerList()
        {
            string currentLanguage = LocalizationController.GetLanguageFromCode(LocalizationController.GetLanguage());
            for (int i = 0; i < ((LocalizationController.Language[])Enum.GetValues(typeof(LocalizationController.Language))).Length; ++i)
            {
                LocalizationController.Language language = (LocalizationController.Language)i;
                ToolStripItem newLanguage = ServerToolStripMenuItem.DropDownItems.Add(language.ToString());
                newLanguage.Click += (s, e) =>
                {
                    if (((ToolStripMenuItem)newLanguage).Checked)
                        return;

                    if (MessageBox.Show(Strings.SwitchServer, Strings.Restart, MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) != DialogResult.Yes) return;
                    LocalizationController.SetLanguage(language, code =>
                    {
                        Properties.Settings.Default.LanguageCode = code;
                        Properties.Settings.Default.Save();
                    });

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        Arguments = $"{ProgramController.ParameterPrefix}restart",
                        UseShellExecute = true
                    });
                    Application.Exit();
                };

                if (currentLanguage == language.ToString())
                    ((ToolStripMenuItem)ServerToolStripMenuItem.DropDownItems[i]).Checked = true;
            }
        }

        /// <summary>
        /// Saves the main settings
        /// </summary>
        private void SaveSettings()
        {
            Properties.Settings.Default.RemoveTimestamps = RemoveTimestamps.Checked;
            Properties.Settings.Default.Save();
        }

        /// <summary>
        /// Loads the main settings
        /// </summary>
        private void LoadSettings()
        {
            Version.Text = string.Format(Strings.VersionInfo, ProgramController.Version, ProgramController.IsBetaVersion ? Strings.BetaShort : string.Empty);
            DirectoryPath.Text = "FiveM Local NUI Chat";
            DirectoryPath.ReadOnly = true;
            Browse.Enabled = false;

            RemoveTimestamps.Checked = Properties.Settings.Default.RemoveTimestamps;
            RemoveTimestamps.CheckedChanged += RemoveTimestamps_CheckedChanged;

            if (Properties.Settings.Default.FirstStart)
            {
                Properties.Settings.Default.FirstStart = false;
                Properties.Settings.Default.Save();
            }
        }

        private void DirectoryPath_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
        }

        private void DirectoryPath_MouseClick(object sender, MouseEventArgs e)
        {
        }

        private void DirectoryPath_TextChanged(object sender, EventArgs e)
        {
        }

        private void Browse_Click(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// Attempts to parse the current chat log
        /// </summary>
        private void Parse_Click(object sender, EventArgs e)
        {
            string parsed = ProgramController.ParseChatLog(RemoveTimestamps.Checked);
            if (!string.IsNullOrEmpty(parsed))
            {
                _previousLog = parsed;
                Parsed.Text = parsed;
            }
        }

        private void RemoveTimestamps_CheckedChanged(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Parsed.Text))
                return;

            if (RemoveTimestamps.Checked)
            {
                _previousLog = Parsed.Text;
                Parsed.Text = ChatLogParser.StripTimestamps(_previousLog);
            }
            else if (!string.IsNullOrWhiteSpace(_previousLog))
            {
                Parsed.Text = _previousLog;
            }
        }

        /// <summary>
        /// Displays a save file dialog to save the
        /// contents of the main text box to the disk
        /// </summary>
        private void SaveParsed_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Parsed.Text))
                return;

            try
            {
                SaveFileDialog.FileName = "chatlog.txt";
                SaveFileDialog.Filter = @"Text File | *.txt";

                if (SaveFileDialog.ShowDialog() != DialogResult.OK) return;
                using (StreamWriter sw = new StreamWriter(SaveFileDialog.OpenFile()))
                {
                    sw.Write(Parsed.Text.Replace("\n", Environment.NewLine));
                }
            }
            catch
            {
                MessageBox.Show(Strings.SaveError, Strings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Copies the contents of the
        /// main text box to the clipboard
        /// </summary>
        private void CopyParsedToClipboard_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(Parsed.Text))
                Clipboard.SetText(Parsed.Text.Replace("\n", Environment.NewLine));
        }

        /// <summary>
        /// Saves the settings before the main form closes
        /// </summary>
        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettings();
        }

        /// <summary>
        /// Displays some information about the program
        /// </summary>
        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show(string.Format(Strings.About, ProgramController.Version, ProgramController.IsBetaVersion ? Strings.Beta : string.Empty, ProgramController.ResourceDirectory), Strings.Information, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void FiveMToolsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string currentPath = FiveMDetector.DetectFiveMDirectory();

            MessageBox.Show(
                $"FiveM Directory: {currentPath}\n\nPlease launch GTAWAssistant for the interactive FiveM ReShade Fix interface.",
                "FiveM Tools",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
