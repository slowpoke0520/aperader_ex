using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using System.IO;
using System.Diagnostics;
using System.Windows.Threading;

using ApeRadar.Models;
using ApeRadar.Utils;
using ApeRadar.Utils.Sorters;

using Newtonsoft.Json.Linq;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Globalization;

namespace ApeRadar
{
    partial class MainWindow : Window
    {
        private string currentBattleFilename = "";
        private string currentBattleID = "";
        private DateTimeOffset currentBattleStartTime = DateTimeOffset.MinValue;

        private void SwitchLanguage(Language language)
        {
            Application.Current.Resources.MergedDictionaries[0] = LanguageExt.GetResourceDictionaryByLanguage(language);
            LabelGamePathIsSetOrNot.GetBindingExpression(ContentProperty).UpdateTarget();

            ComboBoxLanguage.SelectionChanged -= ComboBoxLanguage_SelectionChanged;
            ComboBoxLanguage.Items.Clear();
            ComboBoxLanguage.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemLanguageAuto"), Value = "AUTO" });
            ComboBoxLanguage.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemLanguageEnglish"), Value = "EN_US" });
            ComboBoxLanguage.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemLanguageSimplifiedChinese"), Value = "ZH_CN" });
            ComboBoxLanguage.SelectedValue = Properties.Settings.Default.Language;
            ComboBoxLanguage.SelectionChanged += ComboBoxLanguage_SelectionChanged;

            ComboBoxChartType.SelectionChanged -= ComboBoxChartType_SelectionChanged;
            ComboBoxChartType.Items.Clear();
            ComboBoxChartType.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemChartTypeShipTypeAndShipTierAndWinrateDescending"), Value = "0" });
            ComboBoxChartType.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemChartTypeShipTierAndShipTypeAndWinrateDescending"), Value = "1" });
            ComboBoxChartType.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemChartTypeShipTypeAndWinrateDescending"), Value = "2" });
            ComboBoxChartType.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemChartTypeShipTierAndWinrateDescending"), Value = "3" });
            ComboBoxChartType.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemChartTypeWinrateDescending"), Value = "4" });
            ComboBoxChartType.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemChartTypeWinrateAscending"), Value = "5" });
            ComboBoxChartType.SelectedValue = Properties.Settings.Default.WinrateChartType;
            ComboBoxChartType.SelectionChanged += ComboBoxChartType_SelectionChanged;

            ComboBoxMirrored.SelectionChanged -= ComboBoxMirrored_SelectionChanged;
            ComboBoxMirrored.Items.Clear();
            ComboBoxMirrored.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemMirroredDisplayDisabled"), Value = "False" });
            ComboBoxMirrored.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemMirroredDisplayEnabled"), Value = "True" });
            ComboBoxMirrored.SelectedValue = Properties.Settings.Default.EnemiesDisplayMirrored.ToString();
            ComboBoxMirrored.SelectionChanged += ComboBoxMirrored_SelectionChanged;

            ComboBoxSortBy.SelectionChanged -= ComboBoxSortBy_SelectionChanged;
            ComboBoxSortBy.Items.Clear();
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByShipTypeAndShipTierAndWinrateDescending"), Value = "0" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByShipTierAndShipTypeAndWinrateDescending"), Value = "1" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByShipTypeAndWinrateDescending"), Value = "2" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByShipTierAndWinrateDescending"), Value = "3" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByWinrateDescending"), Value = "4" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByWinrateAscending"), Value = "5" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByBattlesDescending"), Value = "6" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByBattlesAscending"), Value = "7" });
            ComboBoxSortBy.SelectedValue = Properties.Settings.Default.PlayerListSortBy;
            ComboBoxSortBy.SelectionChanged += ComboBoxSortBy_SelectionChanged;

            ComboBoxPlayerNamesVisibility.SelectionChanged -= ComboBoxPlayerNamesVisibility_SelectionChanged;
            ComboBoxPlayerNamesVisibility.Items.Clear();
            ComboBoxPlayerNamesVisibility.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemPlayerNamesVisible"), Value = "True" });
            ComboBoxPlayerNamesVisibility.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemPlayerNamesHidden"), Value = "False" });
            ComboBoxPlayerNamesVisibility.SelectedValue = Properties.Settings.Default.PlayerNamesVisibility.ToString();
            ComboBoxPlayerNamesVisibility.SelectionChanged += ComboBoxPlayerNamesVisibility_SelectionChanged;
        }

        private void SwitchSorting(int sorting)
        {
            if (this.DataContext is not Battlefield battlefield)
            {
                return;
            }
            ListCollectionView? alliesCollectionView = CollectionViewSource.GetDefaultView(battlefield.Allies) as ListCollectionView;
            ListCollectionView? enemiesCollectionView = CollectionViewSource.GetDefaultView(battlefield.Enemies) as ListCollectionView;

            alliesCollectionView!.CustomSort = enemiesCollectionView!.CustomSort = sorting switch
            {
                0 => new CustomSorterByShipTypeAndShipTierAndWinrateDescending(),
                1 => new CustomSorterByShipTierAndShipTypeAndWinrateDescending(),
                2 => new CustomSorterByShipTypeAndWinrateDescending(),
                3 => new CustomSorterByShipTierAndWinrateDescending(),
                4 => new CustomSorterByWinrateDescending(),
                5 => new CustomSorterByWinrateAscending(),
                6 => new CustomSorterByBattlesDescending(),
                7 => new CustomSorterByBattlesAscending(),
                _ => new CustomSorterByShipTypeAndShipTierAndWinrateDescending(),
            };
        }

        private void SwitchWinrateChartType(int chartType)
        {
            if (this.DataContext is not Battlefield battlefield)
            {
                return;
            }
            WinrateChart.Series = ChartUtils.GetWinrateChartSeries(battlefield, chartType);
            WinrateChart.Sections = ChartUtils.GetWinrateChartSections(battlefield, chartType);
        }

        private void RefreshDataGridColumns(bool mirrored)
        {
            DataGridAlliesList.Columns.Clear();
            DataGridAlliesList.Columns.Add(TryFindResource("AlliesNameColumn") as DataGridTemplateColumn);
            DataGridAlliesList.Columns.Add(TryFindResource("AlliesStatisticsColumn") as DataGridTemplateColumn);
            DataGridAlliesList.Columns.Add(TryFindResource("AlliesTagColumn") as DataGridTemplateColumn);
            if (mirrored)
            {
                DataGridEnemiesList.Columns.Clear();
                DataGridEnemiesList.Columns.Add(TryFindResource("EnemiesTagColumn") as DataGridTemplateColumn);
                DataGridEnemiesList.Columns.Add(TryFindResource("EnemiesStatisticsColumnMirrored") as DataGridTemplateColumn);
                DataGridEnemiesList.Columns.Add(TryFindResource("EnemiesNameColumnMirrored") as DataGridTemplateColumn);
            }
            else
            {
                DataGridEnemiesList.Columns.Clear();
                DataGridEnemiesList.Columns.Add(TryFindResource("EnemiesNameColumn") as DataGridTemplateColumn);
                DataGridEnemiesList.Columns.Add(TryFindResource("EnemiesStatisticsColumn") as DataGridTemplateColumn);
                DataGridEnemiesList.Columns.Add(TryFindResource("EnemiesTagColumn") as DataGridTemplateColumn);
            }
        }

        private void ForceUpdateDataGridColumnWidth()
        {
            foreach (DataGridColumn col in DataGridAlliesList.Columns)
            {
                col.Width = 0;
            }
            foreach (DataGridColumn col in DataGridEnemiesList.Columns)
            {
                col.Width = 0;
            }
            DataGridAlliesList.UpdateLayout();
            DataGridEnemiesList.UpdateLayout();
            foreach (DataGridColumn col in DataGridAlliesList.Columns)
            {
                col.Width = DataGridLength.Auto;
            }
            foreach (DataGridColumn col in DataGridEnemiesList.Columns)
            {
                col.Width = DataGridLength.Auto;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            if (Properties.Settings.Default.DebugMode)
            {
                LogUtils.SetLogLevel(log4net.Core.Level.Debug);
            }
            else
            {
                LogUtils.SetLogLevel(log4net.Core.Level.Info);
            }

            LogUtils.WriteDebug("Debug Mode");
            LogUtils.WriteInfo($"SoftwareVersion: {Properties.Settings.Default.SoftwareVersion}");
            LogUtils.WriteInfo($"SoftwareDate: {Properties.Settings.Default.SoftwareDate}");

            LogUtils.WriteInfo($"OS Version: {Environment.OSVersion.VersionString}");
            Version currentVersion = Environment.OSVersion.Version;
            if (currentVersion.CompareTo(new Version("6.2")) < 0)
            {
                LogUtils.WriteInfo($"OS is Windows 7 or earlier. Some emoji chars may be displayed incorrectly.");
            }

            if (!Properties.Settings.Default.SettingsUpgradeDone)
            {
                LogUtils.WriteInfo($"Settings Upgrade Done");
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.SettingsUpgradeDone = true;
            }

            //solve old version settings migration problem
            try
            {
                LanguageExt.GetLanguageByName(Properties.Settings.Default.Language);
            }
            catch
            {
                Properties.Settings.Default.Language = LanguageExt.GetNameByLanguage(Models.Language.AUTO);
            }

            try
            {
                LanguageExt.GetLanguageByName(Properties.Settings.Default.ShipNameLanguage);
            }
            catch
            {
                Properties.Settings.Default.ShipNameLanguage = LanguageExt.GetNameByLanguage(Models.Language.AUTO);
            }

            try
            {
                ServerExt.GetServerByName(Properties.Settings.Default.Server);
            }
            catch
            {
                Properties.Settings.Default.Server = ServerExt.GetNameByServer(Server.AUTO);
            }

            try
            {
                ServerExt.GetServerByName(Properties.Settings.Default.SecondaryServer);
            }
            catch
            {
                Properties.Settings.Default.SecondaryServer = ServerExt.GetNameByServer(Server.RU);
            }

            Properties.Settings.Default.Save();

            ((App)Application.Current).WindowPlace.Register(this);

            NotificationMessageUtils.InitializeNotificationMessageDataGrid(DataGridNotificationMessages);
            NetworkUtils.InitializeHttpClient();

            ShipInfoUtils.ReadShipInfoFile(@".\Resources\Json\ships.json");
            LogUtils.WriteInfo($"ShipInfoFileVersion: {ShipInfoUtils.GetShipInfoVersion()}");
            LogUtils.WriteInfo($"ShipInfoFileDate: {ShipInfoUtils.GetShipInfoDate()}");

            PRUtils.LoadExpectedValues(@".\Resources\Json\expected_values.json");

            SoftwareUpdateUtils.CleanOldVersionFiles();

            if (Properties.Settings.Default.CheckForUpdatesOnStartup)
            {
                _ = CheckForStartupUpdates();
            }

            WinrateChart.TooltipTextPaint = new SolidColorPaint { Color = SKColors.Black, FontFamily = WinrateChart.FontFamily.Source };
            WinrateChart.XAxes = new Axis[] { new Axis { IsVisible = false } };
            WinrateChart.YAxes = new Axis[] { new Axis { Labeler = d => { return d.ToString("p1"); }, CrosshairPaint = new SolidColorPaint(SKColors.Gray) } };
            KDEChart.XAxes = new Axis[] { new Axis { Labeler = d => { return d.ToString("p1"); }, SeparatorsPaint = new SolidColorPaint(SKColors.LightGray), CrosshairPaint = new SolidColorPaint(SKColors.Gray) } };
            KDEChart.YAxes = new Axis[] { new Axis { IsVisible = false } };

            SwitchLanguage(LanguageExt.GetLanguageByName(Properties.Settings.Default.Language));
            RefreshDataGridColumns(Properties.Settings.Default.EnemiesDisplayMirrored);

            DispatcherTimer timer = new()
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += new EventHandler(Timer_Tick);
            timer.Start();
            LogUtils.WriteInfo("Timer Start");
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            string latestFileName = FileUtils.GetLatestTempArenaInfoFile(true);
            if (latestFileName != "")
            {
                await ReadPlayersListAndGetDataFromServer(latestFileName);
            }
        }

        private async Task ReadPlayersListAndGetDataFromServer(string filename, bool forceRefresh = false, string? forceRefreshPlayerID = null, Server? forceRefreshPlayerServer = null)
        {
            LogUtils.WriteInfo("Reading Players List");
            LogUtils.WriteInfo($"gamePath={Properties.Settings.Default.GamePath}");
            LogUtils.WriteInfo($"filename={filename}");

            int maximumRetryAttempts = Properties.Settings.Default.MaximumRetryAttemptsOnError;
            const int delayTimeBetweenRetryAttempts = 1000;

            for (int i = 0; i <= maximumRetryAttempts; i++)
            {
                try
                {
                    BtnRefresh.IsEnabled = false;
                    BtnOpen.IsEnabled = false;

                    Server server = ServerExt.GetServerByName(Properties.Settings.Default.Server);

                    LogUtils.WriteInfo($"server={ServerExt.GetNameByServer(server)}");
                    if (server == Server.AUTO)
                    {
                        server = ServerExt.AutoDetectServer($@"{Properties.Settings.Default.GamePath}\profile\clientrunner.log");
                        LogUtils.WriteInfo($"detectedServer={ServerExt.GetNameByServer(server)}");
                    }

                    //secondary server: enemy players come from different server, for cross-server CW only
                    Server secondaryServer = ServerExt.GetServerByName(Properties.Settings.Default.SecondaryServer);
                    LogUtils.WriteInfo($"secondaryServer={ServerExt.GetNameByServer(secondaryServer)}");
                    string? primaryForceRefreshPlayerID = forceRefreshPlayerServer == server ? forceRefreshPlayerID : null;
                    string? secondaryForceRefreshPlayerID = forceRefreshPlayerServer == secondaryServer ? forceRefreshPlayerID : null;

                    JObject JObjectWatchList = WatchListUtils.ReadWatchList(@".\WatchList.json");
                    JObject JObjectTempArenaInfo = FileUtils.ReadTempArenaInfoFile(filename);

                    string battleType = JObjectTempArenaInfo["matchGroup"]!.Value<string>()!;
                    DateTimeOffset battleStartTime = DateTimeOffset.ParseExact(JObjectTempArenaInfo["dateTime"]!.Value<string>()!, "dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);

                    int playerCount = JObjectTempArenaInfo["vehicles"]!.Count();
                    LogUtils.WriteInfo($"playerCount={playerCount}");

                    List<Player> playerList;
                    List<Task<List<Player>>> taskList = new();

                    NotificationMessageUtils.CreateMessage(MessageType.INFO, FindResource("NotificationMessageRetrivingData") as string);

                    //Vortex API: new features supported, all servers supported
                    //WG Public API: new features not supported, RU & CN server not supported
                    //WG Public API With Yuyuko Proxy: same as WG Public API, but may be more stable under certain network environments in China
                    APIType apiType = APITypeExt.GetAPITypeByName(Properties.Settings.Default.APITypeSelection);
                    if (server != Server.RU && server != Server.CN && (!Properties.Settings.Default.SecondaryServerEnabled || secondaryServer != Server.RU  && secondaryServer != Server.CN) && (apiType == APIType.WG_PUBLIC || apiType == APIType.WG_PUBLIC_WITH_YUYUKO_PROXY))
                    {
                        //if secondary server is enabled, get results from two servers separately and merge them
                        if (Properties.Settings.Default.SecondaryServerEnabled)
                        {
                            taskList.Add(ApiUtils.WgPublicApiGetPlayersStatistics(playerCount, 1, JObjectTempArenaInfo, server, apiType == APIType.WG_PUBLIC_WITH_YUYUKO_PROXY, forceRefresh, primaryForceRefreshPlayerID));
                            taskList.Add(ApiUtils.WgPublicApiGetPlayersStatistics(playerCount, 2, JObjectTempArenaInfo, secondaryServer, apiType == APIType.WG_PUBLIC_WITH_YUYUKO_PROXY, forceRefresh, secondaryForceRefreshPlayerID));
                        }
                        else
                        {
                            taskList.Add(ApiUtils.WgPublicApiGetPlayersStatistics(playerCount, 0, JObjectTempArenaInfo, server, apiType == APIType.WG_PUBLIC_WITH_YUYUKO_PROXY, forceRefresh, primaryForceRefreshPlayerID));
                        }
                    }
                    else
                    {
                        if (Properties.Settings.Default.SecondaryServerEnabled)
                        {
                            taskList.Add(ApiUtils.VortexApiGetPlayersStatistics(playerCount, 1, JObjectTempArenaInfo, server, forceRefresh, primaryForceRefreshPlayerID));
                            taskList.Add(ApiUtils.VortexApiGetPlayersStatistics(playerCount, 2, JObjectTempArenaInfo, secondaryServer, forceRefresh, secondaryForceRefreshPlayerID));
                        }
                        else
                        {
                            taskList.Add(ApiUtils.VortexApiGetPlayersStatistics(playerCount, 0, JObjectTempArenaInfo, server, forceRefresh, primaryForceRefreshPlayerID));
                        }
                    }

                    List<Player>[] results = await Task.WhenAll(taskList);
                    playerList = results.SelectMany(result => result).ToList();

                    //check if player is on the watchlist
                    foreach (Player p in playerList)
                    {
                        if (p.ID != "-1" && JObjectWatchList[ServerExt.GetNameByServer(p.Server)]!.SelectToken(p.ID) != null)
                        {
                            p.WatchStatus = WatchStatusExt.GetStatusByName(JObjectWatchList[ServerExt.GetNameByServer(p.Server)]![p.ID]!["status"]!.Value<string>()!);
                            p.Note = WatchListUtils.GetPlayerNote(JObjectWatchList, p.Server, p.ID);
                            p.IsCustomMarked = WatchListUtils.GetPlayerCustomMarker(JObjectWatchList, p.Server, p.ID);
                        }
                    }

                    string battleID = $"{battleStartTime:O}|{JObjectTempArenaInfo["mapName"]?.Value<string>()}|{JObjectTempArenaInfo["playerName"]?.Value<string>()}";
                    EncounterHistoryUtils.ApplyRecentEncounterMarkers(playerList, battleID, battleStartTime);

                    //battlefield is the main model containing ally and enemy player list
                    Battlefield battlefield = new(battleType, battleStartTime, playerList);

                    //remind the player of saved notes when encountering players with notes
                    foreach (Player p in playerList)
                    {
                        if (!string.IsNullOrEmpty(p.Note))
                        {
                            NotificationMessageUtils.CreateMessage(MessageType.INFO, $"{FindResource("NotificationMessageNoteEncountered") as string}{p.Name}{FindResource("NotificationMessageNoteEncounteredMiddle") as string}{p.Note}");
                        }
                    }

                    //push feature under dev
                    if (Properties.Settings.Default.YuyukoAPIPushEnabled)
                    {
                        ApiUtils.YuyukoApiPushBattlefieldInfo(battlefield);
                    }

                    ApplyBattlefieldToUI(battlefield);
                    PlayerDataCache.Save();
                    EncounterHistoryUtils.RecordBattle(playerList, battleID, battleStartTime);
                    currentBattleFilename = filename;
                    currentBattleID = battleID;
                    currentBattleStartTime = battleStartTime;

                    //refresh stale cached players in background without blocking the UI
                    List<Player> stalePlayers = playerList.Where(p => p.IsDataStale).ToList();
                    if (stalePlayers.Count > 0)
                    {
                        NotificationMessageUtils.CreateMessage(MessageType.INFO, $"{stalePlayers.Count}{FindResource("NotificationMessageDataUsingCache") as string}");
                        _ = RefreshStalePlayersInBackground(JObjectTempArenaInfo, playerCount, server, secondaryServer, apiType, battlefield);
                    }

                    NotificationMessageUtils.CreateMessage(MessageType.INFO, FindResource("NotificationMessageDataRetrieved") as string);
                    return;
                }
                catch (Exception ex)
                {
                    LogUtils.WriteError("", ex);
                    _ = ex.Message switch
                    {
                        "FileFormatIncorrect" => NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageFileError") as string),
                        "ServerAutoDetectionFailed" => NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageServerAutoDetectionFailed") as string),
                        "HttpRequestFailed" => NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageConnectionError") as string),
                        "JsonStringNotValid" => NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageJsonError") as string),
                        _ => NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageOtherError") as string),
                    };
                    if (ex.Message == "FileFormatIncorrect" || ex.Message == "ServerAutoDetectionFailed")
                    {
                        return;
                    }
                    else
                    {
                        if (i < maximumRetryAttempts)
                        {
                            await Task.Delay(delayTimeBetweenRetryAttempts);
                            NotificationMessageUtils.CreateMessage(MessageType.INFO, $"{FindResource("NotificationMessageRetrying")}{i + 1}{FindResource("NotificationMessageAttempt")}");
                        }
                        else if (maximumRetryAttempts > 0)
                        {
                            NotificationMessageUtils.CreateMessage(MessageType.INFO, FindResource("NotificationMessageMaximumAttemptsReached") as string);
                        }
                    }
                }
                finally
                {
                    BtnRefresh.IsEnabled = true;
                    BtnOpen.IsEnabled = true;
                }
            }
        }

        private bool isBackgroundRefreshing = false;

        private void ApplyBattlefieldToUI(Battlefield battlefield)
        {
            this.DataContext = battlefield;

            TxtOutputText.Text = TextUtils.GenerateGeneralStatisticsOutputText(battlefield);
            if (Properties.Settings.Default.OutputTextAutoCopy && Properties.Settings.Default.OutputTextUnlock)
            {
                Clipboard.SetDataObject(TxtOutputText.Text);
            }

            SwitchSorting(Properties.Settings.Default.PlayerListSortBy);
            SwitchWinrateChartType(Properties.Settings.Default.WinrateChartType);
            KDEChart.Series = ChartUtils.GetKDEChartSeries(battlefield);
        }

        //re-fetch expired cached players in the background, then update the UI in place
        private async Task RefreshStalePlayersInBackground(JObject JObjectTempArenaInfo, int playerCount, Server server, Server secondaryServer, APIType apiType, Battlefield currentBattlefield)
        {
            if (isBackgroundRefreshing)
            {
                return;
            }
            isBackgroundRefreshing = true;
            try
            {
                List<Task<List<Player>>> taskList = new();
                if (server != Server.RU && server != Server.CN && (!Properties.Settings.Default.SecondaryServerEnabled || secondaryServer != Server.RU && secondaryServer != Server.CN) && (apiType == APIType.WG_PUBLIC || apiType == APIType.WG_PUBLIC_WITH_YUYUKO_PROXY))
                {
                    if (Properties.Settings.Default.SecondaryServerEnabled)
                    {
                        taskList.Add(ApiUtils.WgPublicApiGetPlayersStatistics(playerCount, 1, JObjectTempArenaInfo, server, apiType == APIType.WG_PUBLIC_WITH_YUYUKO_PROXY, true));
                        taskList.Add(ApiUtils.WgPublicApiGetPlayersStatistics(playerCount, 2, JObjectTempArenaInfo, secondaryServer, apiType == APIType.WG_PUBLIC_WITH_YUYUKO_PROXY, true));
                    }
                    else
                    {
                        taskList.Add(ApiUtils.WgPublicApiGetPlayersStatistics(playerCount, 0, JObjectTempArenaInfo, server, apiType == APIType.WG_PUBLIC_WITH_YUYUKO_PROXY, true));
                    }
                }
                else
                {
                    if (Properties.Settings.Default.SecondaryServerEnabled)
                    {
                        taskList.Add(ApiUtils.VortexApiGetPlayersStatistics(playerCount, 1, JObjectTempArenaInfo, server, true));
                        taskList.Add(ApiUtils.VortexApiGetPlayersStatistics(playerCount, 2, JObjectTempArenaInfo, secondaryServer, true));
                    }
                    else
                    {
                        taskList.Add(ApiUtils.VortexApiGetPlayersStatistics(playerCount, 0, JObjectTempArenaInfo, server, true));
                    }
                }

                List<Player>[] results = await Task.WhenAll(taskList);
                List<Player> refreshedList = results.SelectMany(result => result).ToList();

                //only apply the result if the user is still viewing the same battle
                if (!ReferenceEquals(this.DataContext, currentBattlefield))
                {
                    return;
                }

                int updatedCount = 0;
                foreach (Player newP in refreshedList)
                {
                    Player? old = currentBattlefield.Allies.FirstOrDefault(x => x.Server == newP.Server && x.ID == newP.ID);
                    old ??= currentBattlefield.Enemies.FirstOrDefault(x => x.Server == newP.Server && x.ID == newP.ID);
                    if (old != null)
                    {
                        old.CopyFrom(newP);
                        updatedCount++;
                    }
                }

                if (updatedCount > 0)
                {
                    List<Player> combinedPlayerList = currentBattlefield.Allies.Concat(currentBattlefield.Enemies).ToList();
                    Battlefield battlefield = new(currentBattlefield.BattleType, currentBattlefield.BattleStartTime, combinedPlayerList);
                    ApplyBattlefieldToUI(battlefield);
                    PlayerDataCache.Save();
                    NotificationMessageUtils.CreateMessage(MessageType.INFO, FindResource("NotificationMessageBackgroundUpdateComplete") as string);
                }
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("background data refresh failed", ex);
                NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageOtherError") as string);
            }
            finally
            {
                isBackgroundRefreshing = false;
            }
        }

        private void BtnConfig_Click(object sender, RoutedEventArgs e)
        {
            ConfigWindow configWindow = new()
            {
                Owner = this
            };
            configWindow.ShowDialog();
            ForceUpdateDataGridColumnWidth();
        }

        private async void BtnSoftwareUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnSoftwareUpdate.IsEnabled = false;
            try
            {
                if (await SoftwareUpdateUtils.CheckForSoftwareUpdates() == false)
                {
                    System.Windows.MessageBox.Show(FindResource("MsgBoxSoftwareUpdateNotFound") as string, FindResource("MsgBoxUpdate") as string, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                BtnSoftwareUpdate.IsEnabled = true;
            }
        }

        private static async Task CheckForStartupUpdates()
        {
            await SoftwareUpdateUtils.CheckForSoftwareUpdates();
            await SoftwareUpdateUtils.CheckForShipListUpdates();
        }

        private void ComboBoxLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.Language = ComboBoxLanguage.SelectedValue.ToString()!;
            Properties.Settings.Default.Save();
            SwitchLanguage(LanguageExt.GetLanguageByName(Properties.Settings.Default.Language));
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            string latestFileName = FileUtils.GetLatestTempArenaInfoFile(false);
            if (latestFileName != "")
            {
                //manual refresh fetches the current battle again without deleting other cached battles
                await ReadPlayersListAndGetDataFromServer(latestFileName, true);
            }
        }

        private async void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.OpenFileDialog dialog = new();
            dialog.Filter = "*.json, *.wowsreplay|*.json;*.wowsreplay";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await ReadPlayersListAndGetDataFromServer(dialog.FileName);
            }
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

        private void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            int gridH = (int)MainWindowGrid.ActualHeight;
            int gridW = (int)MainWindowGrid.ActualWidth;
            RenderTargetBitmap bitmap = new(gridW, gridH, 96, 96, PixelFormats.Default);
            Rect rect = new(0, 0, gridW, gridH);
            DrawingVisual drawingVisual = new();
            using (DrawingContext drawingContext = drawingVisual.RenderOpen())
            {
                drawingContext.DrawRectangle(Background, null, rect);
            }
            bitmap.Render(drawingVisual);
            bitmap.Render(MainWindowGrid);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string datetimestr = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_ffff");
            if (!Directory.Exists(@".\Screenshot\"))
            {
                Directory.CreateDirectory(@".\Screenshot\");
            }
            string filePath = $@".\Screenshot\{datetimestr}.png";
            using FileStream fs = new(filePath, FileMode.Create);
            encoder.Save(fs);
            NotificationMessageUtils.CreateMessage(MessageType.INFO, $"{FindResource("NotificationMessageScreenshotIsSavedTo") as string}{filePath}{FindResource("NotificationMessagePeriod") as string}");
        }

        //drag and drop a replay file on the window to open it
        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                object? files = e.Data.GetData(DataFormats.FileDrop);
                if (files is not null)
                {
                    if ((files as string[])!.Length == 1)
                    {
                        await ReadPlayersListAndGetDataFromServer((files as string[])![0]);
                    }
                }
            }
        }

        private void ContextMenuCopyPlayerStatistics_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            Clipboard.SetDataObject(TextUtils.GenerateParticularPlayerStatisticsOutputText(p!));
        }

        private async void ContextMenuRefreshPlayer_Click(object sender, RoutedEventArgs e)
        {
            Player? p = (sender as MenuItem)?.DataContext as Player;
            if (p == null || !p.CanRefreshData || currentBattleFilename == "" || !BtnRefresh.IsEnabled)
            {
                return;
            }

            LogUtils.WriteInfo($"Manual player data refresh: Name={p.Name}, ID={p.ID}, Server={ServerExt.GetNameByServer(p.Server)}");
            await ReadPlayersListAndGetDataFromServer(currentBattleFilename, false, p.ID, p.Server);
        }

        private void ContextMenuFixedTeammate_Click(object sender, RoutedEventArgs e)
        {
            Player? p = (sender as MenuItem)?.DataContext as Player;
            if (p == null || !p.CanBeFixedTeammate)
            {
                return;
            }

            EncounterHistoryUtils.ToggleFixedTeammate(p);
            if (DataContext is Battlefield battlefield && currentBattleID != "")
            {
                EncounterHistoryUtils.ApplyRecentEncounterMarkers(battlefield.Allies.Concat(battlefield.Enemies), currentBattleID, currentBattleStartTime);
                RefreshPlayerList();
            }
        }

        private void ContextMenuCustomMarker_Click(object sender, RoutedEventArgs e)
        {
            Player? p = (sender as MenuItem)?.DataContext as Player;
            if (p == null || p.ID == "-1")
            {
                return;
            }

            p.IsCustomMarked = !p.IsCustomMarked;
            WatchListUtils.SaveCustomMarker(p, @".\WatchList.json");
            RefreshPlayerList();
        }

        private void ContextMenuAddToWatchListPositive_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.WatchStatus = WatchStatus.POSITIVE;
            WatchListUtils.SaveWatchList(p, @".\WatchList.json");
        }

        private void ContextMenuAddToWatchListNegtive_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.WatchStatus = WatchStatus.NEGTIVE;
            WatchListUtils.SaveWatchList(p, @".\WatchList.json");
        }

        private void ContextMenuAddToWatchListCheater_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.WatchStatus = WatchStatus.CHEATER;
            WatchListUtils.SaveWatchList(p, @".\WatchList.json");
        }

        private void ContextMenuRemoveFromWatchList_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.WatchStatus = WatchStatus.NONE;
            WatchListUtils.SaveWatchList(p, @".\WatchList.json");
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
                RefreshPlayerList();
            }
        }

        private void ContextMenuClearNote_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.Note = "";
            WatchListUtils.SaveWatchListNote(p, @".\WatchList.json");
            RefreshPlayerList();
        }

        private void RefreshPlayerList()
        {
            //force the datagrid to refresh to show the note icon change
            object tmpDataContext = this.DataContext;
            this.DataContext = null;
            this.DataContext = tmpDataContext;
        }

        //the ToolTipPlayerDetails.LayoutTransform is bind to the ViewboxPlayerList.Tag
        //so the tooltip will scale to fit the size of the datagrid when changing window size
        //a dumb way but it works
        private void ViewboxPlayerList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (VisualTreeHelper.GetChild(ViewboxPlayerList, 0) is ContainerVisual cv)
            {
                ViewboxPlayerList.Tag = cv.Transform;
            }
        }

        private void ComboBoxChartType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.WinrateChartType = Convert.ToInt32(ComboBoxChartType.SelectedValue);
            Properties.Settings.Default.Save();
            SwitchWinrateChartType(Properties.Settings.Default.WinrateChartType);
        }

        private void ComboBoxMirrored_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.EnemiesDisplayMirrored = Convert.ToBoolean(ComboBoxMirrored.SelectedValue);
            Properties.Settings.Default.Save();
            RefreshDataGridColumns(Properties.Settings.Default.EnemiesDisplayMirrored);
        }

        private void ComboBoxSortBy_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.PlayerListSortBy = Convert.ToInt32(ComboBoxSortBy.SelectedValue);
            Properties.Settings.Default.Save();
            SwitchSorting(Properties.Settings.Default.PlayerListSortBy);
        }

        private void ComboBoxPlayerNamesVisibility_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.PlayerNamesVisibility = Convert.ToBoolean(ComboBoxPlayerNamesVisibility.SelectedValue);
            Properties.Settings.Default.Save();

            //force the datagrid to refresh. a dumb way but it works
            object tmpDataContext = this.DataContext;
            this.DataContext = null;
            this.DataContext = tmpDataContext;
        }

        private void HyperLinkApeRadarWebsite_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start("explorer.exe", e.Uri.AbsoluteUri);
        }
    }
}
