using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using ApeRadar.Models;
using ApeRadar.Utils;
using ApeRadar.ViewModels;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace ApeRadar
{
    partial class ConfigWindow : Window
    {
        private readonly bool initializeRuntime;
        private readonly string initialMainInterfaceStyle;

        private void LoadSettings()
        {
            ComboBoxGamePath.Text = Properties.Settings.Default.GamePath;
            TxtOutputTextTemplateGeneralStatistics.Text = Properties.Settings.Default.OutputTextTemplateGeneralStatistics;
            TxtOutputTextTemplateParticularPlayerStatistics.Text = Properties.Settings.Default.OutputTextTemplateParticularPlayerStatistics;
            ChkBoxSecondaryServerEnabled.IsChecked = Properties.Settings.Default.SecondaryServerEnabled;
            ComboBoxSecondaryServer.SelectedValue = Properties.Settings.Default.SecondaryServer;
            SliderMaximumRetryAttemptsOnError.Value = Properties.Settings.Default.MaximumRetryAttemptsOnError;
            ComboBoxWinrateTypeSelect.SelectedIndex = Properties.Settings.Default.WinrateTypeUsed;
            ComboBoxColorStyle.SelectedIndex = Properties.Settings.Default.ColorStyle;
            TxtApeIcon.Text = Properties.Settings.Default.ApeIcon;
            TxtUnicumIcon.Text = Properties.Settings.Default.UnicumIcon;
            TxtHiddenIcon.Text = Properties.Settings.Default.HiddenIcon;
            TxtWatchIcon.Text = Properties.Settings.Default.WatchIcon;
            SliderPlayerColumnFontSize.Value = Properties.Settings.Default.PlayerColumnFontSize;
            SliderStatisticsColumnFontSize.Value = Properties.Settings.Default.StatisticsColumnFontSize;
            SliderDetailedStatisticsFontSize.Value = Properties.Settings.Default.DetailedStatisticsFontSize;
            SliderOutputTextFontSize.Value = Properties.Settings.Default.OutputTextFontSize;
            ComboBoxAccountWinrateVisibility.SelectedIndex = Properties.Settings.Default.AccountWinrateVisibility;
            ComboBoxWeightedWinrateVisibility.SelectedIndex = Properties.Settings.Default.WeightedWinrateVisibility;
            ComboBoxShipWinrateVisibility.SelectedIndex = Properties.Settings.Default.ShipWinrateVisibility;
            ComboBoxAccountAvgExpVisibility.SelectedIndex = Properties.Settings.Default.AccountAvgExpVisibility;
            ComboBoxShipAvgExpVisibility.SelectedIndex = Properties.Settings.Default.ShipAvgExpVisibility;
            ComboBoxShipAvgDmgVisibility.SelectedIndex = Properties.Settings.Default.ShipAvgDmgVisibility;
            ComboBoxTagVisibility.SelectedIndex = Properties.Settings.Default.TagVisibility;
            ComboBoxPRVisibility.SelectedIndex = Properties.Settings.Default.PRVisibility;
            ChkBoxShortMode.IsChecked = Properties.Settings.Default.OutputTextShortMode;
            ChkBoxExcludeYourself.IsChecked = Properties.Settings.Default.OutputTextExcludeSelf;
            ChkBoxTextOutputUnlocked.Checked -= ChkBoxTextOutputUnlocked_Checked;
            ChkBoxTextOutputUnlocked.IsChecked = Properties.Settings.Default.OutputTextUnlock;
            ChkBoxTextOutputUnlocked.Checked += ChkBoxTextOutputUnlocked_Checked;
            ChkBoxAutoCopy.IsChecked = Properties.Settings.Default.OutputTextAutoCopy && Properties.Settings.Default.OutputTextUnlock;
            ComboBoxAPIType.SelectedValue = Properties.Settings.Default.APITypeSelection;
            TxtWgApplicationId.Text = Properties.Settings.Default.WgApplicationId;
            ChkBoxEnableYuyukoAPIPush.IsChecked = Properties.Settings.Default.YuyukoAPIPushEnabled;
            ChkBoxEnableDebugMode.IsChecked = Properties.Settings.Default.DebugMode;
            TxtApeWinrateThreshold.Text = Properties.Settings.Default.ApeWinrateThreshold.ToString("f1");
            TxtUnicumWinrateThreshold.Text = Properties.Settings.Default.UnicumWinrateThreshold.ToString("f1");
            TxtApeBattleCountThreshold.Text = Properties.Settings.Default.ApeBattleCountThreshold.ToString();
            TxtUnicumBattleCountThreshold.Text = Properties.Settings.Default.UnicumBattleCountThreshold.ToString();
            TxtWeightedWinrateAccountSoloWeightMultiplier.Text = Properties.Settings.Default.WeightedWinrateAccountSoloWeightMultiplier.ToString("f1");
            TxtWeightedWinrateAccountDiv2WeightMultiplier.Text = Properties.Settings.Default.WeightedWinrateAccountDiv2WeightMultiplier.ToString("f1");
            TxtWeightedWinrateAccountDiv3WeightMultiplier.Text = Properties.Settings.Default.WeightedWinrateAccountDiv3WeightMultiplier.ToString("f1");
            TxtWeightedWinrateShipMaxWeight.Text = Properties.Settings.Default.WeightedWinrateShipMaxWeight.ToString("f1");
            TxtWeightedWinrateShipBattlesAtMaxWeight.Text = Properties.Settings.Default.WeightedWinrateShipBattlesAtMaxWeight.ToString();
            TxtDelimiter.Text = Properties.Settings.Default.OutputTextDelimiter;
            ComboBoxServer.SelectedValue = Properties.Settings.Default.Server;
            ComboBoxShipNameLanguage.SelectedValue = Properties.Settings.Default.ShipNameLanguage;
            ComboBoxSoftwareUpdateChannel.SelectedValue = SoftwareReleaseSelector.NormalizeChannelSetting(Properties.Settings.Default.SoftwareUpdateChannel);
            ComboBoxMainInterfaceStyle.SelectedValue = NormalizeMainInterfaceStyle(Properties.Settings.Default.MainInterfaceStyle);
            ChkBoxCheckForUpdatesOnStartup.IsChecked = Properties.Settings.Default.CheckForUpdatesOnStartup;
            ChkBoxShowExperimentalReplayMetrics.IsChecked = Properties.Settings.Default.ShowExperimentalReplayMetrics;
            ChkBoxShowTierPerformanceStats.IsChecked = Properties.Settings.Default.ShowTierPerformanceStats;
            ChkBoxShowAccountRosterColumn.IsChecked = Properties.Settings.Default.ShowAccountRosterColumn;
            ChkBoxShowShipRosterColumn.IsChecked = Properties.Settings.Default.ShowShipRosterColumn;
            ChkBoxShowPerformanceRosterColumn.IsChecked = Properties.Settings.Default.ShowPerformanceRosterColumn;
            ChkBoxShowRecentEncounterBadges.IsChecked = Properties.Settings.Default.ShowRecentEncounterBadges;
            ChkBoxShowFixedTeammateBadges.IsChecked = Properties.Settings.Default.ShowFixedTeammateBadges;
            ChkBoxShowCachedDataBadges.IsChecked = Properties.Settings.Default.ShowCachedDataBadges;
            ChkBoxShowShipTypeIcon.IsChecked = Properties.Settings.Default.ShowShipTypeIcon;
            ComboBoxRosterDisplayDensity.SelectedValue = RosterDisplayDensityExtensions.Parse(Properties.Settings.Default.RosterDisplayDensity).ToSettingValue();
            ChkBoxShowLegacyPerformanceTag.IsChecked = Properties.Settings.Default.ShowLegacyPerformanceTag;
            ComboBoxRosterPerformanceMetric.SelectedValue = RosterPerformanceMetricExtensions.Parse(Properties.Settings.Default.RosterPerformanceMetric).ToSettingValue();
            LabelShipListVersionDateStr.Content = $"{ShipInfoUtils.GetShipInfoVersion()} ({ShipInfoUtils.GetShipInfoDate()})";
            if (PRUtils.GetExpectedValuesTime() <= 0)
            {
                PRUtils.LoadExpectedValues(@".\Resources\Json\expected_values.json");
            }
            LabelPRDataVersionDateStr.Content = PRUtils.GetExpectedValuesDateString();
            UpdateSoftwareUpdateStatus();
        }

        private void UpdateSoftwareUpdateStatus(SoftwareUpdateCheckResult? result = null)
        {
            if (result == null)
            {
                string initialFormat = TryFindResource("SoftwareUpdateStatusInitial") as string ?? "Current version: {0}. Not checked yet.";
                TxtSoftwareUpdateStatus.Text = string.Format(initialFormat, Properties.Settings.Default.SoftwareVersion);
                TxtSoftwareUpdateStatus.ToolTip = null;
                return;
            }

            string resourceKey = result.Status switch
            {
                SoftwareUpdateCheckStatus.UpToDate => "SoftwareUpdateStatusUpToDate",
                SoftwareUpdateCheckStatus.UpdateAvailable => "SoftwareUpdateStatusAvailable",
                SoftwareUpdateCheckStatus.UpdateStarted => "SoftwareUpdateStatusStarted",
                SoftwareUpdateCheckStatus.AlreadyRunning => "SoftwareUpdateStatusAlreadyRunning",
                SoftwareUpdateCheckStatus.NetworkError => "SoftwareUpdateStatusNetworkError",
                SoftwareUpdateCheckStatus.RateLimited => "SoftwareUpdateStatusRateLimited",
                SoftwareUpdateCheckStatus.InvalidFeed => "SoftwareUpdateStatusInvalidFeed",
                SoftwareUpdateCheckStatus.MissingAsset => "SoftwareUpdateStatusMissingAsset",
                SoftwareUpdateCheckStatus.HashInvalid => "SoftwareUpdateStatusHashInvalid",
                SoftwareUpdateCheckStatus.Cancelled => "SoftwareUpdateStatusCancelled",
                _ => "SoftwareUpdateStatusInvalidFeed"
            };
            string format = TryFindResource(resourceKey) as string ?? "Current {0}; available {1}; checked {2}.";
            TxtSoftwareUpdateStatus.Text = string.Format(format,
                result.CurrentVersion,
                result.AvailableVersion ?? "-",
                result.CheckedAt.ToLocalTime().ToString("g"),
                result.PublishedAt?.ToLocalTime().ToString("g") ?? "-");
            TxtSoftwareUpdateStatus.ToolTip = string.IsNullOrWhiteSpace(result.ReleaseNotes) ? null : result.ReleaseNotes;
        }

        private int SaveSettings()
        {
            try
            {
                if (double.TryParse(TxtApeWinrateThreshold.Text, out double ApeWinrateThreshold) & double.TryParse(TxtUnicumWinrateThreshold.Text, out double UnicumWinrateThreshold) & int.TryParse(TxtApeBattleCountThreshold.Text, out int ApeBattleCountThreshold) & int.TryParse(TxtUnicumBattleCountThreshold.Text, out int UnicumBattleCountThreshold) & double.TryParse(TxtWeightedWinrateAccountSoloWeightMultiplier.Text, out double WeightedWinrateAccountSoloWeightMultiplier) & double.TryParse(TxtWeightedWinrateAccountDiv2WeightMultiplier.Text, out double WeightedWinrateAccountDiv2WeightMultiplier) & double.TryParse(TxtWeightedWinrateAccountDiv3WeightMultiplier.Text, out double WeightedWinrateAccountDiv3WeightMultiplier) & double.TryParse(TxtWeightedWinrateShipMaxWeight.Text, out double WeightedWinrateShipMaxWeight) & int.TryParse(TxtWeightedWinrateShipBattlesAtMaxWeight.Text, out int WeightedWinrateShipBattlesAtMaxWeight))
                {
                    if (ApeWinrateThreshold < 0 || ApeWinrateThreshold > 100 || UnicumWinrateThreshold < 0 || UnicumWinrateThreshold > 100 || ApeBattleCountThreshold < 0 || UnicumBattleCountThreshold < 0 || WeightedWinrateAccountSoloWeightMultiplier < 0 || WeightedWinrateAccountSoloWeightMultiplier > 10000 || WeightedWinrateAccountDiv2WeightMultiplier < 0 || WeightedWinrateAccountDiv2WeightMultiplier > 10000 || WeightedWinrateAccountDiv3WeightMultiplier < 0 || WeightedWinrateAccountDiv3WeightMultiplier > 10000 || WeightedWinrateAccountSoloWeightMultiplier + WeightedWinrateAccountDiv2WeightMultiplier + WeightedWinrateAccountDiv3WeightMultiplier == 0 || WeightedWinrateShipMaxWeight < 0 || WeightedWinrateShipMaxWeight > 100 || WeightedWinrateShipBattlesAtMaxWeight < 0)
                    {
                        return -1;
                    }

                    if (!Directory.Exists(ComboBoxGamePath.Text) || !File.Exists($@"{ComboBoxGamePath.Text}\WorldOfWarships.exe"))
                    {
                        if (System.Windows.MessageBox.Show(TryFindResource("MsgBoxGamePathErrorConfirmation") as string, TryFindResource("MsgBoxConfirmation") as string, MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.No)
                        {
                            return -2;
                        }
                    }

                    Properties.Settings.Default.GamePath = ComboBoxGamePath.Text;
                    Properties.Settings.Default.OutputTextTemplateGeneralStatistics = TxtOutputTextTemplateGeneralStatistics.Text;
                    Properties.Settings.Default.OutputTextTemplateParticularPlayerStatistics = TxtOutputTextTemplateParticularPlayerStatistics.Text;
                    Properties.Settings.Default.SecondaryServerEnabled = ChkBoxSecondaryServerEnabled.IsChecked ?? false;
                    Properties.Settings.Default.SecondaryServer = ComboBoxSecondaryServer.SelectedValue.ToString();
                    Properties.Settings.Default.MaximumRetryAttemptsOnError = Convert.ToInt32(SliderMaximumRetryAttemptsOnError.Value);
                    Properties.Settings.Default.WinrateTypeUsed = ComboBoxWinrateTypeSelect.SelectedIndex;
                    Properties.Settings.Default.ColorStyle = ComboBoxColorStyle.SelectedIndex;
                    Properties.Settings.Default.ApeIcon = TxtApeIcon.Text;
                    Properties.Settings.Default.UnicumIcon = TxtUnicumIcon.Text;
                    Properties.Settings.Default.HiddenIcon = TxtHiddenIcon.Text;
                    Properties.Settings.Default.WatchIcon = TxtWatchIcon.Text;
                    Properties.Settings.Default.PlayerColumnFontSize = SliderPlayerColumnFontSize.Value;
                    Properties.Settings.Default.StatisticsColumnFontSize = SliderStatisticsColumnFontSize.Value;
                    Properties.Settings.Default.DetailedStatisticsFontSize = SliderDetailedStatisticsFontSize.Value;
                    Properties.Settings.Default.OutputTextFontSize = SliderOutputTextFontSize.Value;
                    Properties.Settings.Default.AccountWinrateVisibility = ComboBoxAccountWinrateVisibility.SelectedIndex;
                    Properties.Settings.Default.WeightedWinrateVisibility = ComboBoxWeightedWinrateVisibility.SelectedIndex;
                    Properties.Settings.Default.ShipWinrateVisibility = ComboBoxShipWinrateVisibility.SelectedIndex;
                    Properties.Settings.Default.AccountAvgExpVisibility = ComboBoxAccountAvgExpVisibility.SelectedIndex;
                    Properties.Settings.Default.ShipAvgExpVisibility = ComboBoxShipAvgExpVisibility.SelectedIndex;
                    Properties.Settings.Default.ShipAvgDmgVisibility = ComboBoxShipAvgDmgVisibility.SelectedIndex;
                    Properties.Settings.Default.TagVisibility = ComboBoxTagVisibility.SelectedIndex;
                    Properties.Settings.Default.PRVisibility = ComboBoxPRVisibility.SelectedIndex;
                    Properties.Settings.Default.OutputTextShortMode = ChkBoxShortMode.IsChecked ?? false;
                    Properties.Settings.Default.OutputTextExcludeSelf = ChkBoxExcludeYourself.IsChecked ?? false;
                    Properties.Settings.Default.OutputTextAutoCopy = ChkBoxAutoCopy.IsChecked ?? false;
                    Properties.Settings.Default.APITypeSelection = ComboBoxAPIType.SelectedValue.ToString();
                    Properties.Settings.Default.WgApplicationId = TxtWgApplicationId.Text.Trim();
                    Properties.Settings.Default.YuyukoAPIPushEnabled = ChkBoxEnableYuyukoAPIPush.IsChecked ?? false;
                    Properties.Settings.Default.DebugMode = ChkBoxEnableDebugMode.IsChecked ?? false;
                    Properties.Settings.Default.ApeWinrateThreshold = ApeWinrateThreshold;
                    Properties.Settings.Default.UnicumWinrateThreshold = UnicumWinrateThreshold;
                    Properties.Settings.Default.ApeBattleCountThreshold = ApeBattleCountThreshold;
                    Properties.Settings.Default.UnicumBattleCountThreshold = UnicumBattleCountThreshold;
                    Properties.Settings.Default.WeightedWinrateAccountSoloWeightMultiplier = WeightedWinrateAccountSoloWeightMultiplier;
                    Properties.Settings.Default.WeightedWinrateAccountDiv2WeightMultiplier = WeightedWinrateAccountDiv2WeightMultiplier;
                    Properties.Settings.Default.WeightedWinrateAccountDiv3WeightMultiplier = WeightedWinrateAccountDiv3WeightMultiplier;
                    Properties.Settings.Default.WeightedWinrateShipMaxWeight = WeightedWinrateShipMaxWeight;
                    Properties.Settings.Default.WeightedWinrateShipBattlesAtMaxWeight = WeightedWinrateShipBattlesAtMaxWeight;
                    Properties.Settings.Default.OutputTextDelimiter = TxtDelimiter.Text;
                    Properties.Settings.Default.Server = ComboBoxServer.SelectedValue.ToString();
                    Properties.Settings.Default.ShipNameLanguage = ComboBoxShipNameLanguage.SelectedValue.ToString();
                    Properties.Settings.Default.SoftwareUpdateChannel = ComboBoxSoftwareUpdateChannel.SelectedValue?.ToString()
                        ?? SoftwareReleaseSelector.StableSettingValue;
                    Properties.Settings.Default.MainInterfaceStyle = NormalizeMainInterfaceStyle(ComboBoxMainInterfaceStyle.SelectedValue?.ToString());
                    Properties.Settings.Default.CheckForUpdatesOnStartup = ChkBoxCheckForUpdatesOnStartup.IsChecked ?? false;
                    Properties.Settings.Default.ShowExperimentalReplayMetrics = ChkBoxShowExperimentalReplayMetrics.IsChecked ?? false;
                    Properties.Settings.Default.ShowTierPerformanceStats = ChkBoxShowTierPerformanceStats.IsChecked ?? false;
                    Properties.Settings.Default.ShowAccountRosterColumn = ChkBoxShowAccountRosterColumn.IsChecked ?? true;
                    Properties.Settings.Default.ShowShipRosterColumn = ChkBoxShowShipRosterColumn.IsChecked ?? true;
                    Properties.Settings.Default.ShowPerformanceRosterColumn = ChkBoxShowPerformanceRosterColumn.IsChecked ?? true;
                    Properties.Settings.Default.ShowRecentEncounterBadges = ChkBoxShowRecentEncounterBadges.IsChecked ?? true;
                    Properties.Settings.Default.ShowFixedTeammateBadges = ChkBoxShowFixedTeammateBadges.IsChecked ?? true;
                    Properties.Settings.Default.ShowCachedDataBadges = ChkBoxShowCachedDataBadges.IsChecked ?? true;
                    Properties.Settings.Default.ShowShipTypeIcon = ChkBoxShowShipTypeIcon.IsChecked ?? false;
                    Properties.Settings.Default.RosterDisplayDensity = RosterDisplayDensityExtensions.Parse(ComboBoxRosterDisplayDensity.SelectedValue?.ToString()).ToSettingValue();
                    Properties.Settings.Default.ShowLegacyPerformanceTag = ChkBoxShowLegacyPerformanceTag.IsChecked ?? false;
                    Properties.Settings.Default.RosterPerformanceMetric = RosterPerformanceMetricExtensions.Parse(ComboBoxRosterPerformanceMetric.SelectedValue?.ToString()).ToSettingValue();
                    Properties.Settings.Default.OutputTextUnlock = ChkBoxTextOutputUnlocked.IsChecked ?? false;
                    Properties.Settings.Default.Save();
                    ShipTypePresentation.RefreshOpenWindows();
                    if (Properties.Settings.Default.DebugMode)
                    {
                        LogUtils.SetLogLevel(log4net.Core.Level.Debug);
                    }
                    else
                    {
                        LogUtils.SetLogLevel(log4net.Core.Level.Info);
                    }
                    return 0;
                }
                else
                {
                    return -1;
                }
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("", ex);
                return -1;
            }
        }

        private void RefreshWatchList()
        {
            JObject JObjectWatchList = WatchListUtils.ReadWatchList(@".\WatchList.json");

            List<Player> WatchListPositive = new();
            List<Player> WatchListNegtive = new();
            List<Player> WatchListCheater = new();
            List<Player> WatchListNote = new();
            foreach (JProperty JPropertyServer in JObjectWatchList.Children())
            {
                if (JPropertyServer.Value.HasValues)
                {
                    foreach (JProperty JPropertyPlayer in JPropertyServer.Value.Children())
                    {
                        WatchStatus status = WatchStatusExt.GetStatusByName(JPropertyPlayer.Value["status"]!.Value<string>()!);
                        string note = JPropertyPlayer.Value["note"]?.Value<string>() ?? "";
                        Player player = new(JPropertyPlayer.Value["name"]!.Value<string>()!, JPropertyPlayer.Name, ServerExt.GetServerByName(JPropertyServer.Name), status)
                        {
                            Note = note
                        };
                        if (status == WatchStatus.POSITIVE)
                        {
                            WatchListPositive.Add(player);
                        }
                        else if (status == WatchStatus.NEGTIVE)
                        {
                            WatchListNegtive.Add(player);
                        }
                        else if (status == WatchStatus.CHEATER)
                        {
                            WatchListCheater.Add(player);
                        }
                        else if (!string.IsNullOrEmpty(note))
                        {
                            WatchListNote.Add(player);
                        }
                    }
                }
            }
            DataGridWatchListPositive.ItemsSource = WatchListPositive;
            DataGridWatchListNegtive.ItemsSource = WatchListNegtive;
            DataGridWatchListCheater.ItemsSource = WatchListCheater;
            DataGridWatchListNote.ItemsSource = WatchListNote;
        }

        private void AutoDetectGamePath()
        {
            ComboBoxGamePath.Items.Clear();
            RegistryKey hkcuUninstall = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall")!;
            RegistryKey hklmUninstall = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall")!;

            foreach (string subKeyName in hkcuUninstall.GetSubKeyNames())
            {
                RegistryKey key = hkcuUninstall.OpenSubKey(subKeyName)!;
                string publisher = (key.GetValue("Publisher") ?? "").ToString()!;
                if (publisher == "Wargaming.net" || publisher == "Wargaming Group Limited" || publisher == "360.cn" || publisher == "Lesta Games")
                {
                    string installLocation = (key.GetValue("InstallLocation") ?? "").ToString()!;
                    if (Directory.Exists(installLocation))
                    {
                        if (File.Exists($@"{installLocation}\WorldOfWarships.exe"))
                        {
                            ComboBoxGamePath.Items.Add(installLocation);
                        }
                    }
                }
            }
            foreach (string subKeyName in hklmUninstall.GetSubKeyNames())
            {
                RegistryKey key = hklmUninstall.OpenSubKey(subKeyName)!;
                string publisher = (key.GetValue("Publisher") ?? "").ToString()!;
                if (publisher == "Wargaming.net" || publisher == "Wargaming Group Limited" || publisher == "360.cn" || publisher == "Lesta Games")
                {
                    string installLocation = (key.GetValue("InstallLocation") ?? "").ToString()!;
                    if (Directory.Exists(installLocation))
                    {
                        if (File.Exists($@"{installLocation}\WorldOfWarships.exe"))
                        {
                            ComboBoxGamePath.Items.Add(installLocation);
                        }
                    }
                }
            }
        }

        public ConfigWindow() : this(initializeRuntime: true)
        {
        }

        internal ConfigWindow(bool initializeRuntime)
        {
            this.initializeRuntime = initializeRuntime;
            initialMainInterfaceStyle = NormalizeMainInterfaceStyle(Properties.Settings.Default.MainInterfaceStyle);
            InitializeComponent();
            if (!initializeRuntime) return;
            LoadSettings();
            AutoDetectGamePath();
            RefreshWatchList();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog dialog = new();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ComboBoxGamePath.Text = dialog.SelectedPath;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string selectedInterfaceStyle = NormalizeMainInterfaceStyle(ComboBoxMainInterfaceStyle.SelectedValue?.ToString());
            int result = SaveSettings();
            if (result == 0)
            {
                if (!string.Equals(selectedInterfaceStyle, initialMainInterfaceStyle, StringComparison.Ordinal))
                {
                    System.Windows.MessageBox.Show(
                        TryFindResource("MsgBoxMainInterfaceRestart") as string ?? "The main interface style will be applied after ApeRadar restarts.",
                        TryFindResource("MsgBoxConfirmation") as string,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                this.Close();
            }
            else if (result == -1)
            {
                System.Windows.MessageBox.Show(TryFindResource("MsgBoxInputError") as string, TryFindResource("MsgBoxError") as string, MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }

        }

        private void BtnDefault_Click(object sender, RoutedEventArgs e)
        {
            ApplyDefaultsForSelectedPage();
        }

        internal void ApplyDefaultsForSelectedPage()
        {
            switch (ConfigTabs.SelectedIndex)
            {
                case 0:
                    ComboBoxGamePath.Text = Default<string>(nameof(Properties.Settings.GamePath));
                    ChkBoxSecondaryServerEnabled.IsChecked = Default<bool>(nameof(Properties.Settings.SecondaryServerEnabled));
                    ComboBoxSecondaryServer.SelectedValue = Default<string>(nameof(Properties.Settings.SecondaryServer));
                    SliderMaximumRetryAttemptsOnError.Value = Default<int>(nameof(Properties.Settings.MaximumRetryAttemptsOnError));
                    ComboBoxServer.SelectedValue = Default<string>(nameof(Properties.Settings.Server));
                    ComboBoxShipNameLanguage.SelectedValue = Default<string>(nameof(Properties.Settings.ShipNameLanguage));
                    break;
                case 1:
                    ComboBoxColorStyle.SelectedIndex = Default<int>(nameof(Properties.Settings.ColorStyle));
                    TxtApeIcon.Text = Default<string>(nameof(Properties.Settings.ApeIcon));
                    TxtUnicumIcon.Text = Default<string>(nameof(Properties.Settings.UnicumIcon));
                    TxtHiddenIcon.Text = Default<string>(nameof(Properties.Settings.HiddenIcon));
                    TxtWatchIcon.Text = Default<string>(nameof(Properties.Settings.WatchIcon));
                    SliderPlayerColumnFontSize.Value = Default<double>(nameof(Properties.Settings.PlayerColumnFontSize));
                    SliderStatisticsColumnFontSize.Value = Default<double>(nameof(Properties.Settings.StatisticsColumnFontSize));
                    SliderDetailedStatisticsFontSize.Value = Default<double>(nameof(Properties.Settings.DetailedStatisticsFontSize));
                    SliderOutputTextFontSize.Value = Default<double>(nameof(Properties.Settings.OutputTextFontSize));
                    ComboBoxAccountWinrateVisibility.SelectedIndex = Default<int>(nameof(Properties.Settings.AccountWinrateVisibility));
                    ComboBoxWeightedWinrateVisibility.SelectedIndex = Default<int>(nameof(Properties.Settings.WeightedWinrateVisibility));
                    ComboBoxShipWinrateVisibility.SelectedIndex = Default<int>(nameof(Properties.Settings.ShipWinrateVisibility));
                    ComboBoxAccountAvgExpVisibility.SelectedIndex = Default<int>(nameof(Properties.Settings.AccountAvgExpVisibility));
                    ComboBoxShipAvgExpVisibility.SelectedIndex = Default<int>(nameof(Properties.Settings.ShipAvgExpVisibility));
                    ComboBoxShipAvgDmgVisibility.SelectedIndex = Default<int>(nameof(Properties.Settings.ShipAvgDmgVisibility));
                    ComboBoxTagVisibility.SelectedIndex = Default<int>(nameof(Properties.Settings.TagVisibility));
                    ComboBoxPRVisibility.SelectedIndex = Default<int>(nameof(Properties.Settings.PRVisibility));
                    ChkBoxShowShipTypeIcon.IsChecked = Default<bool>(nameof(Properties.Settings.ShowShipTypeIcon));
                    ComboBoxRosterDisplayDensity.SelectedValue = RosterDisplayDensityExtensions.Parse(Default<string>(nameof(Properties.Settings.RosterDisplayDensity))).ToSettingValue();
                    ChkBoxShowLegacyPerformanceTag.IsChecked = Default<bool>(nameof(Properties.Settings.ShowLegacyPerformanceTag));
                    ComboBoxRosterPerformanceMetric.SelectedValue = RosterPerformanceMetricExtensions.Parse(Default<string>(nameof(Properties.Settings.RosterPerformanceMetric))).ToSettingValue();
                    ChkBoxShowAccountRosterColumn.IsChecked = Default<bool>(nameof(Properties.Settings.ShowAccountRosterColumn));
                    ChkBoxShowShipRosterColumn.IsChecked = Default<bool>(nameof(Properties.Settings.ShowShipRosterColumn));
                    ChkBoxShowTierPerformanceStats.IsChecked = Default<bool>(nameof(Properties.Settings.ShowTierPerformanceStats));
                    ChkBoxShowPerformanceRosterColumn.IsChecked = Default<bool>(nameof(Properties.Settings.ShowPerformanceRosterColumn));
                    ChkBoxShowRecentEncounterBadges.IsChecked = Default<bool>(nameof(Properties.Settings.ShowRecentEncounterBadges));
                    ChkBoxShowFixedTeammateBadges.IsChecked = Default<bool>(nameof(Properties.Settings.ShowFixedTeammateBadges));
                    ChkBoxShowCachedDataBadges.IsChecked = Default<bool>(nameof(Properties.Settings.ShowCachedDataBadges));
                    break;
                case 2:
                    ComboBoxWinrateTypeSelect.SelectedIndex = Default<int>(nameof(Properties.Settings.WinrateTypeUsed));
                    TxtApeWinrateThreshold.Text = Default<double>(nameof(Properties.Settings.ApeWinrateThreshold)).ToString("f1", CultureInfo.CurrentCulture);
                    TxtUnicumWinrateThreshold.Text = Default<double>(nameof(Properties.Settings.UnicumWinrateThreshold)).ToString("f1", CultureInfo.CurrentCulture);
                    TxtApeBattleCountThreshold.Text = Default<int>(nameof(Properties.Settings.ApeBattleCountThreshold)).ToString(CultureInfo.CurrentCulture);
                    TxtUnicumBattleCountThreshold.Text = Default<int>(nameof(Properties.Settings.UnicumBattleCountThreshold)).ToString(CultureInfo.CurrentCulture);
                    TxtWeightedWinrateAccountSoloWeightMultiplier.Text = Default<double>(nameof(Properties.Settings.WeightedWinrateAccountSoloWeightMultiplier)).ToString("f1", CultureInfo.CurrentCulture);
                    TxtWeightedWinrateAccountDiv2WeightMultiplier.Text = Default<double>(nameof(Properties.Settings.WeightedWinrateAccountDiv2WeightMultiplier)).ToString("f1", CultureInfo.CurrentCulture);
                    TxtWeightedWinrateAccountDiv3WeightMultiplier.Text = Default<double>(nameof(Properties.Settings.WeightedWinrateAccountDiv3WeightMultiplier)).ToString("f1", CultureInfo.CurrentCulture);
                    TxtWeightedWinrateShipMaxWeight.Text = Default<double>(nameof(Properties.Settings.WeightedWinrateShipMaxWeight)).ToString("f1", CultureInfo.CurrentCulture);
                    TxtWeightedWinrateShipBattlesAtMaxWeight.Text = Default<int>(nameof(Properties.Settings.WeightedWinrateShipBattlesAtMaxWeight)).ToString(CultureInfo.CurrentCulture);
                    break;
                case 3:
                    TxtOutputTextTemplateGeneralStatistics.Text = Default<string>(nameof(Properties.Settings.OutputTextTemplateGeneralStatistics));
                    TxtDelimiter.Text = Default<string>(nameof(Properties.Settings.OutputTextDelimiter));
                    ChkBoxShortMode.IsChecked = Default<bool>(nameof(Properties.Settings.OutputTextShortMode));
                    ChkBoxExcludeYourself.IsChecked = Default<bool>(nameof(Properties.Settings.OutputTextExcludeSelf));
                    ChkBoxTextOutputUnlocked.Checked -= ChkBoxTextOutputUnlocked_Checked;
                    ChkBoxTextOutputUnlocked.IsChecked = Default<bool>(nameof(Properties.Settings.OutputTextUnlock));
                    ChkBoxTextOutputUnlocked.Checked += ChkBoxTextOutputUnlocked_Checked;
                    ChkBoxAutoCopy.IsChecked = Default<bool>(nameof(Properties.Settings.OutputTextAutoCopy)) && (ChkBoxTextOutputUnlocked.IsChecked ?? false);
                    break;
                case 4:
                    TxtOutputTextTemplateParticularPlayerStatistics.Text = Default<string>(nameof(Properties.Settings.OutputTextTemplateParticularPlayerStatistics));
                    break;
                case 6:
                    ComboBoxSoftwareUpdateChannel.SelectedValue = SoftwareReleaseSelector.NormalizeChannelSetting(Default<string>(nameof(Properties.Settings.SoftwareUpdateChannel)));
                    ComboBoxMainInterfaceStyle.SelectedValue = NormalizeMainInterfaceStyle(Default<string>(nameof(Properties.Settings.MainInterfaceStyle)));
                    ComboBoxAPIType.SelectedValue = Default<string>(nameof(Properties.Settings.APITypeSelection));
                    TxtWgApplicationId.Text = Default<string>(nameof(Properties.Settings.WgApplicationId));
                    ChkBoxEnableYuyukoAPIPush.IsChecked = Default<bool>(nameof(Properties.Settings.YuyukoAPIPushEnabled));
                    ChkBoxEnableDebugMode.IsChecked = Default<bool>(nameof(Properties.Settings.DebugMode));
                    ChkBoxCheckForUpdatesOnStartup.IsChecked = Default<bool>(nameof(Properties.Settings.CheckForUpdatesOnStartup));
                    ChkBoxShowExperimentalReplayMetrics.IsChecked = Default<bool>(nameof(Properties.Settings.ShowExperimentalReplayMetrics));
                    break;
            }
        }

        private static T Default<T>(string propertyName)
        {
            object? value = Properties.Settings.Default.Properties[propertyName]?.DefaultValue;
            if (value is T typed) return typed;
            string text = value?.ToString() ?? "";
            if (typeof(T) == typeof(string)) return (T)(object)text;
            return (T)(TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(text)
                ?? throw new InvalidOperationException($"Setting {propertyName} has no default value."));
        }

        private void ConfigTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BtnDefault != null) BtnDefault.IsEnabled = ConfigTabs.SelectedIndex != 5;
        }

        internal static string NormalizeMainInterfaceStyle(string? value) =>
            string.Equals(value, "Legacy", StringComparison.OrdinalIgnoreCase) ? "Legacy" : "Dashboard";

        private void ConfigWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (initializeRuntime) LoadSettings();
        }

        private void ChkBoxTextOutputUnlocked_Checked(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show(TryFindResource("MsgBoxOutputTextboxUnlockConfirmation") as string, TryFindResource("MsgBoxConfirmation") as string, MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.No)
            {
                ChkBoxTextOutputUnlocked.IsChecked = false;
            };
        }

        private void ChkBoxTextOutputUnlocked_Unchecked(object sender, RoutedEventArgs e)
        {
            ChkBoxAutoCopy.IsChecked = false;
        }

        private void BtnInsertGeneralStatistics_Click(object sender, RoutedEventArgs e)
        {
            int tempIndex = TxtOutputTextTemplateGeneralStatistics.CaretIndex;
            TxtOutputTextTemplateGeneralStatistics.Text = TxtOutputTextTemplateGeneralStatistics.Text.Insert(tempIndex, $"{{{ComboBoxTagInsertGeneralStatistics.SelectedValue}}}");
            TxtOutputTextTemplateGeneralStatistics.CaretIndex = tempIndex;
        }

        private void BtnInsertParticularPlayerStatistics_Click(object sender, RoutedEventArgs e)
        {
            int tempIndex = TxtOutputTextTemplateParticularPlayerStatistics.CaretIndex;
            TxtOutputTextTemplateParticularPlayerStatistics.Text = TxtOutputTextTemplateParticularPlayerStatistics.Text.Insert(tempIndex, $"{{{ComboBoxTagInsertParticularPlayerStatistics.SelectedValue}}}");
            TxtOutputTextTemplateParticularPlayerStatistics.CaretIndex = tempIndex;
        }


        private void ContextMenuAddToWatchListPositive_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.WatchStatus = WatchStatus.POSITIVE;
            WatchListUtils.SaveWatchList(p, @".\WatchList.json");
            RefreshWatchList();
        }

        private void ContextMenuAddToWatchListNegtive_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.WatchStatus = WatchStatus.NEGTIVE;
            WatchListUtils.SaveWatchList(p, @".\WatchList.json");
            RefreshWatchList();
        }

        private void ContextMenuAddToWatchListCheater_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.WatchStatus = WatchStatus.CHEATER;
            WatchListUtils.SaveWatchList(p, @".\WatchList.json");
            RefreshWatchList();
        }

        private void ContextMenuRemoveFromWatchList_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.WatchStatus = WatchStatus.NONE;
            WatchListUtils.SaveWatchList(p, @".\WatchList.json");
            RefreshWatchList();
        }

        private void ContextMenuEditNote_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            NoteEditWindow noteEditWindow = new(p!.Name)
            {
                Owner = this,
                NoteText = p.Note
            };
            if (noteEditWindow.ShowDialog() == true)
            {
                p.Note = noteEditWindow.NoteText;
                WatchListUtils.SaveWatchListNote(p, @".\WatchList.json");
                RefreshWatchList();
            }
        }

        private void ContextMenuClearNote_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.Note = "";
            WatchListUtils.SaveWatchListNote(p, @".\WatchList.json");
            RefreshWatchList();
        }

        private void ContextMenuCheckOnWoWSNumbers_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            Process.Start("explorer.exe", $"{ServerExt.GetWoWSNumbersUrlStringByServer(p!.Server)}/player/{p.ID}%2C{p.Name}/");
        }

        private void ContextMenuCheckOnWoWSOfficialSite_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            Process.Start("explorer.exe", $"https://profile.{ServerExt.GetFullUrlStringByServer(p!.Server)}/statistics/{p.ID}/");
        }

        private async void BtnCheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckForUpdates.IsEnabled = false;
            string checkingFormat = TryFindResource("SoftwareUpdateStatusChecking") as string ?? "Checking from {0}…";
            TxtSoftwareUpdateStatus.Text = string.Format(checkingFormat, Properties.Settings.Default.SoftwareVersion);
            try
            {
                SoftwareUpdateCheckResult result = await SoftwareUpdateUtils.CheckForSoftwareUpdates();
                UpdateSoftwareUpdateStatus(result);
                if (result.Status == SoftwareUpdateCheckStatus.UpToDate)
                {
                    System.Windows.MessageBox.Show(System.Windows.Application.Current.FindResource("MsgBoxSoftwareUpdateNotFound") as string, System.Windows.Application.Current.FindResource("MsgBoxUpdate") as string, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                BtnCheckForUpdates.IsEnabled = true;
            }
        }

        private void BtnViewChangelog_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SoftwareUpdateUtils.ReleaseNotesUrl,
                UseShellExecute = true
            });
        }

        private async void BtnUpdateShipList_Click(object sender, RoutedEventArgs e)
        {
            BtnUpdateShipList.IsEnabled = false;
            if (await SoftwareUpdateUtils.CheckForShipListUpdates() == false)
            {
                System.Windows.MessageBox.Show(System.Windows.Application.Current.FindResource("MsgBoxShipListUpdateNotFound") as string, System.Windows.Application.Current.FindResource("MsgBoxUpdate") as string, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            BtnUpdateShipList.IsEnabled = true;
            LabelShipListVersionDateStr.Content = $"{ShipInfoUtils.GetShipInfoVersion()} ({ShipInfoUtils.GetShipInfoDate()})";
        }

        private async void BtnUpdatePRData_Click(object sender, RoutedEventArgs e)
        {
            BtnUpdatePRData.IsEnabled = false;
            try
            {
                NotificationMessageUtils.CreateMessage(MessageType.INFO, FindResource("NotificationMessagePRDataUpdateDownloading") as string);
                await NetworkUtils.HttpDownloadFile(PRUtils.ExpectedValuesDownloadUrl, @".\Resources\Json\expected_values.json");
                PRUtils.LoadExpectedValues(@".\Resources\Json\expected_values.json");
                LabelPRDataVersionDateStr.Content = PRUtils.GetExpectedValuesDateString();
                NotificationMessageUtils.CreateMessage(MessageType.INFO, FindResource("NotificationMessagePRDataUpdateComplete") as string);
            }
            catch
            {
                NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageUpdateConnectionError") as string);
            }
            BtnUpdatePRData.IsEnabled = true;
        }

        private void BtnClearCache_Click(object sender, RoutedEventArgs e)
        {
            PlayerDataCache.Clear();
            PlayerIDCache.Clear();
            NotificationMessageUtils.CreateMessage(MessageType.INFO, FindResource("NotificationMessageCacheCleared") as string);
        }
    }
}
