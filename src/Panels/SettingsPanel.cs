using Pokebot.Models;
using Pokebot.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Pokebot.Panels
{
    public partial class SettingsPanel : UserControl
    {
        public delegate void SettingsConfigChangedEventHandler(SettingsConfig settingsConfig);
        public event SettingsConfigChangedEventHandler? SettingsConfigChanged;

        public delegate void PauseToggleEventHandler();
        public event PauseToggleEventHandler? PauseClicked;

        public SettingsConfig SettingsConfig { get; }

        // English plus every Messages.<code>.resx embedded in the DLL
        private readonly IReadOnlyList<LanguageOption> _languages = Languages.GetAvailable();

        public SettingsPanel()
        {
            InitializeComponent();
            ApplyTranslations();
            SettingsConfig = SettingsConfig.Load();

            _accelerateCheckbox.Checked = SettingsConfig.Speed;
            _soundCheckbox.Checked = SettingsConfig.Sound;
            _discordWebhookText.Text = SettingsConfig.DiscordWebhook;
            _discordUserID.Text = SettingsConfig.DiscordUserID;
            _delayUpDown.Value = (decimal)SettingsConfig.DelayBetweenActions;
            _delayTooltip.SetToolTip(_delayLabel, Messages.Tooltip_Delay);

            // Populate language dropdown. Add handler AFTER setting SelectedIndex so
            // the initial assignment does not trigger the changed event.
            _languageComboBox.Items.AddRange(_languages.ToArray<object>());
            var savedLanguage = _languages.FirstOrDefault(x => x.Code.Equals(SettingsConfig.Language, StringComparison.OrdinalIgnoreCase));
            _languageComboBox.SelectedIndex = savedLanguage != null ? _languageComboBox.Items.IndexOf(savedLanguage) : 0;
            _languageComboBox.SelectedIndexChanged += _languageComboBox_SelectedIndexChanged;
        }

        private void ApplyTranslations()
        {
            _accelerateCheckbox.Text = Messages.Settings_Speed;
            _soundCheckbox.Text = Messages.Settings_Sound;
            _pauseCheckbox.Text = Messages.Settings_Pause;
            _delayLabel.Text = Messages.Settings_DelayLabel;
            _languageLabel.Text = Messages.Settings_LanguageLabel;
        }

        private void _accelerateCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            SettingsConfig.Speed = _accelerateCheckbox.Checked;
            SettingsConfigChanged?.Invoke(SettingsConfig);
            SettingsConfig.Save();
        }

        private void _soundCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            SettingsConfig.Sound = _soundCheckbox.Checked;
            SettingsConfigChanged?.Invoke(SettingsConfig);
            SettingsConfig.Save();
        }

        private void _pauseCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            PauseClicked?.Invoke();
        }

        private void _discordWebhookText_TextChanged(object sender, EventArgs e)
        {
            SettingsConfig.DiscordWebhook = _discordWebhookText.Text;
            SettingsConfigChanged?.Invoke(SettingsConfig);
            SettingsConfig.Save();
        }

        private void _discordUserID_TextChanged(object sender, EventArgs e)
        {
            SettingsConfig.DiscordUserID = _discordUserID.Text;
            SettingsConfigChanged?.Invoke(SettingsConfig);
            SettingsConfig.Save();
        }

        private void _delayUpDown_ValueChanged(object sender, EventArgs e)
        {
            SettingsConfig.DelayBetweenActions = (double)_delayUpDown.Value;
            SettingsConfigChanged?.Invoke(SettingsConfig);
            SettingsConfig.Save();
        }

        private void _languageComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!(_languageComboBox.SelectedItem is LanguageOption language))
            {
                return;
            }

            SettingsConfig.Language = language.Code;
            SettingsConfig.Save();
            SettingsConfig.ApplyLanguage(language.Code);

            // Shown in the language just picked
            MessageBox.Show(
                Messages.Settings_LanguageRestartMessage,
                Messages.Settings_LanguageRestartTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
    }
}
