using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using MftParser.Localization;
using MftParser.Models;
using MftParser.Ntfs;
using MftParser.Services;
using MftParser.Ui;

namespace MftParser;

public partial class MainWindow : Window {
    const string RangeFormat = "yyyy-MM-dd HH:mm:ss";

    readonly FilterState _filter = new();
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(260) };
    readonly DateTime _boot;
    readonly TextBlock[] _timeLabels = new TextBlock[4];
    readonly TextBlock[] _siTimes = new TextBlock[4];
    readonly TextBlock[] _fnTimes = new TextBlock[4];

    List<VolumeOption> _volumes = new();
    MftResult? _result;
    FilterResult? _last;
    MftEntry? _selected;
    CancellationTokenSource? _parseCancel;
    CancellationTokenSource? _filterCancel;
    string _parsingLabel = "";
    int _stage = -1;
    bool _quiet = true;

    public MainWindow() {
        InitializeComponent();
        _boot = DateTime.Now - TimeSpan.FromMilliseconds(GetTickCount64());
        _debounce.Tick += (_, _) => { _debounce.Stop(); ReadRange(); RunFilter(); };

        BuildTimesGrid();
        LoadVolumes();
        ApplyTexts();
        PresetAll.IsChecked = true;
        ShowDetail(null);
        _quiet = false;
        Loaded += (_, _) => OpenFromArguments();
    }

    void OpenFromArguments() {
        var args = Environment.GetCommandLineArgs();
        if (args.Length < 2) return;

        var target = args[1].Trim().TrimEnd('\\');
        if (target.Length is 1 or 2 && char.IsLetter(target[0]) && (target.Length == 1 || target[1] == ':')) {
            var volume = _volumes.FirstOrDefault(v => v.IsNtfs && char.ToUpperInvariant(v.Letter) == char.ToUpperInvariant(target[0]));
            if (volume != null) StartVolume(volume);
        } else if (File.Exists(target)) {
            StartParse(() => RecordSource.Open(target), System.IO.Path.GetFileName(target), false);
        }
    }

    void LoadVolumes() {
        _volumes = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Select(VolumeOption.From)
            .ToList();

        VolumeList.ItemsSource = _volumes;
        VolumeList.SelectedItem = _volumes.FirstOrDefault(v => v.IsNtfs && v.Letter == 'C') ?? _volumes.FirstOrDefault(v => v.IsNtfs);

        var options = _volumes.Where(v => v.IsNtfs).ToList();
        options.Add(VolumeOption.File());
        VolumeBox.ItemsSource = options;
        VolumeBox.SelectedItem = VolumeList.SelectedItem;
    }

    void ApplyTexts() {
        var quiet = _quiet;
        _quiet = true;

        LanguageFlag.Source = Strings.IsEnglish ? Flags.Mexico : Flags.UnitedStates;
        LanguageText.Text = Strings.LanguageButton;
        LanguageButton.ToolTip = Strings.LanguageTip;
        ThemeText.Text = Strings.ThemeButton(Theme.IsPink);
        ThemeButton.ToolTip = Strings.ThemeTip;
        ThemeSwatch.Fill = new SolidColorBrush(Theme.IsPink ? Color.FromRgb(0x7C, 0x9C, 0xFF) : Color.FromRgb(0xFF, 0x7E, 0xB6));
        SidebarButton.ToolTip = Strings.SidebarTip;

        SearchBox.Tag = Strings.SearchHint;
        ParseButton.Content = Strings.Parse;
        BootText.Text = Strings.BootChip(FileTime.Shown(_boot));
        BootChip.ToolTip = Strings.BootTip;
        TimestompChip.ToolTip = Strings.TimestompTip;

        RangeCaption.Text = Strings.Range;
        FromBox.Tag = Strings.RangeHint;
        ToBox.Tag = Strings.RangeHint;
        PresetBoot.Content = Strings.FromBoot;
        PresetHour.Content = Strings.LastHour;
        PresetDay.Content = Strings.Last24;
        PresetAll.Content = Strings.Everything;
        DateOfText.Text = Strings.DateOf;
        var field = Math.Max(0, FieldBox.SelectedIndex);
        FieldBox.ItemsSource = Strings.DateFields;
        FieldBox.SelectedIndex = field;

        HideNoiseBox.Content = Strings.HideNoise;
        HideNoiseBox.ToolTip = Strings.HideNoiseTip;
        FoldersBox.Content = Strings.ShowFolders;

        AdsRowsBox.Content = Strings.AdsRows;
        AdsRowsBox.ToolTip = Strings.AdsRowsTip;
        UtcBox.Content = Strings.UtcTimes;

        OpenFolderButton.Content = Strings.OpenFolder;
        CopyButton.Content = Strings.Copy;
        SummaryTab.Content = Strings.TabSummary;
        AttributesTab.Content = Strings.TabAttributes;
        RecordTab.Content = Strings.TabRecord;
        AttributesNote.Text = Strings.AttributesNote;
        BuildAttributesHeader();
        for (var i = 0; i < 4; i++) _timeLabels[i].Text = TimeNames[i];
        foreach (var label in _timeLabels) label.Foreground = Res("Muted");
        BuildHexLegend();

        StartTitle.Text = Strings.PickVolume;
        StartHint.Text = _volumes.Any(v => v.IsNtfs) ? Strings.PickVolumeHint : Strings.NoVolumes;
        OpenFileButton.Content = Strings.OpenFile;
        foreach (var volume in _volumes) volume.Refresh();
        VolumeList.Items.Refresh();
        VolumeBox.Items.Refresh();
        UpdateStartButton();
        CancelButton.Content = Strings.Cancel;
        if (LoadingOverlay.Visibility == Visibility.Visible) ShowStage(_stage);

        UpdateHeader();
        UpdateSide();
        UpdateStatus();
        ShowDetail(_selected);
        if (EntriesGrid.ItemsSource != null) EntriesGrid.Items.Refresh();

        _quiet = quiet;
    }

    void BuildTimesGrid() {
        for (var row = 0; row < 4; row++) {
            _timeLabels[row] = Cell(row + 1, 0);
            _timeLabels[row].Foreground = (Brush)FindResource("Muted");
            _timeLabels[row].FontSize = 12;
            _siTimes[row] = Cell(row + 1, 1);
            _fnTimes[row] = Cell(row + 1, 2);
            _siTimes[row].FontFamily = _fnTimes[row].FontFamily = (FontFamily)FindResource("Mono");
            _siTimes[row].FontSize = _fnTimes[row].FontSize = 13;
        }

        TextBlock Cell(int row, int column) {
            var text = new TextBlock { Padding = new Thickness(12, 6, 12, 6) };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, column);
            TimesGrid.Children.Add(text);
            return text;
        }
    }

    static readonly string[] TimeNames = { "Created", "LastModified", "LastRecordChange", "LastAccess" };

    void BuildAttributesHeader() {
        AttributesHeader.Children.Clear();
        var titles = Strings.AttributeColumns;
        for (var i = 0; i < titles.Length; i++) {
            var title = new TextBlock { Text = titles[i], Style = (Style)FindResource("Caption"), Margin = new Thickness(12, 7, 12, 7) };
            Grid.SetColumn(title, i);
            AttributesHeader.Children.Add(title);
        }
    }

    void BuildHexLegend() {
        HexLegend.Children.Clear();
        Add("Accent", Strings.HexHeader);
        Add("Orange", "0x10 $STANDARD_INFORMATION");
        Add("Green", "0x30 $FILE_NAME");
        Add("Violet", "0x80 $DATA");
        Add("Text", Strings.HexOther);
        HexLegend.Children.Add(new TextBlock { Text = Strings.HexNote, FontSize = 12, Foreground = Res("Muted"), Margin = new Thickness(6, 0, 0, 0) });

        void Add(string color, string label) {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
            panel.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = Res(color), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Res("Muted"), Margin = new Thickness(6, 0, 0, 0) });
            HexLegend.Children.Add(panel);
        }
    }

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    void OnTheme(object sender, RoutedEventArgs e) {
        Theme.Toggle();
        ApplyTexts();
    }

    void OnLanguage(object sender, RoutedEventArgs e) {
        Strings.ToggleLanguage();
        ApplyTexts();
    }

    void OnUtc(object sender, RoutedEventArgs e) {
        FileTime.Utc = UtcBox.IsChecked == true;
        if (_quiet) return;
        BootText.Text = Strings.BootChip(FileTime.Shown(_boot));
        var preset = new[] { PresetBoot, PresetHour, PresetDay }.FirstOrDefault(p => p.IsChecked == true);
        if (preset != null) {
            OnPreset(preset, e);
        } else {
            ReadRange();
            RunFilter();
        }
        if (EntriesGrid.ItemsSource != null) EntriesGrid.Items.Refresh();
        ShowDetail(_selected);
    }

    void OnSidebar(object sender, RoutedEventArgs e) {
        var hidden = SideColumn.Width.Value == 0;
        SideColumn.MinWidth = hidden ? 190 : 0;
        SideColumn.Width = new GridLength(hidden ? 250 : 0);
        SideSplitter.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateStartButton() {
        var volume = VolumeList.SelectedItem as VolumeOption;
        StartButton.Content = Strings.ParseVolume(volume?.ShortLabel ?? "");
        StartButton.IsEnabled = volume?.IsNtfs == true;
    }

    void OnVolumeListChanged(object sender, SelectionChangedEventArgs e) => UpdateStartButton();

    void OnVolumeDoubleClick(object sender, MouseButtonEventArgs e) {
        if (VolumeList.SelectedItem is VolumeOption { IsNtfs: true } volume) StartVolume(volume);
    }

    void OnStart(object sender, RoutedEventArgs e) {
        if (VolumeList.SelectedItem is VolumeOption { IsNtfs: true } volume) StartVolume(volume);
    }

    void OnOpenFile(object sender, RoutedEventArgs e) => PickFile();

    void OnVolumePicked(object sender, SelectionChangedEventArgs e) {
        if (_quiet || VolumeBox.SelectedItem is not VolumeOption { IsFile: true }) return;
        _quiet = true;
        VolumeBox.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
        _quiet = false;
        PickFile();
    }

    void OnParse(object sender, RoutedEventArgs e) {
        if (VolumeBox.SelectedItem is VolumeOption { IsNtfs: true } volume) StartVolume(volume);
    }

    void PickFile() {
        var dialog = new OpenFileDialog { Filter = Strings.FileFilter, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        var path = dialog.FileName;
        StartParse(() => RecordSource.Open(path), System.IO.Path.GetFileName(path), false);
    }

    void StartVolume(VolumeOption volume) {
        VolumeList.SelectedItem = volume;
        _quiet = true;
        VolumeBox.SelectedItem = volume;
        _quiet = false;
        StartParse(() => RecordSource.Open(volume.Letter), volume.ShortLabel, true);
    }

    async void StartParse(Func<RecordSource> open, string label, bool live) {
        _parseCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _parseCancel = cancel;
        _parsingLabel = label;

        StartOverlay.Visibility = Visibility.Collapsed;
        StartError.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = Visibility.Visible;
        ParseButton.IsEnabled = false;
        LoadingBar.Value = 0;
        LoadingDetail.Text = "";
        ShowStage(MftReader.StageRead);

        var progress = new Progress<ReadProgress>(p => {
            if (cancel.IsCancellationRequested) return;
            LoadingBar.Value = p.BytesTotal == 0 ? 0 : (double)p.BytesDone / p.BytesTotal;
            LoadingDetail.Text = Strings.ReadingDetail(Format.Size(p.BytesDone), Format.Size(p.BytesTotal), p.Records);
            ShowStage(p.Stage);
        });

        try {
            var result = await Task.Run(() => {
                var source = open();
                try {
                    return MftReader.Read(source, progress, cancel.Token);
                } catch {
                    source.Dispose();
                    throw;
                }
            });

            if (cancel.IsCancellationRequested) {
                result.Source.Dispose();
                return;
            }

            _result?.Source.Dispose();
            _result = result;
            _selected = null;
            _quiet = true;
            if (live) PresetBoot.IsChecked = true;
            else PresetAll.IsChecked = true;
            _quiet = false;
            ReadRange();
            UpdateHeader();
            LoadingOverlay.Visibility = Visibility.Collapsed;
            RunFilter();
        } catch (OperationCanceledException) {
            LoadingOverlay.Visibility = Visibility.Collapsed;
          if (_result == null) StartOverlay.Visibility = Visibility.Visible;
        } catch (Exception ex) {
            if (cancel.IsCancellationRequested) return;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            StartOverlay.Visibility = Visibility.Visible;
            StartError.Text = Describe(ex);
            StartError.Visibility = Visibility.Visible;
        } finally {
            if (_parseCancel == cancel) ParseButton.IsEnabled = true;
        }
    }

    static string Describe(Exception ex) => ex switch {
        UnauthorizedAccessException => Strings.ErrorAccess,
        Win32Exception { NativeErrorCode: 5 } => Strings.ErrorAccess,
        InvalidDataException { Message: "NTFS" } => Strings.ErrorNotNtfs,
        InvalidDataException { Message: "FILE" } => Strings.ErrorNotMft,
        _ => Strings.ErrorGeneric(ex.Message),
    };

    void OnCancel(object sender, RoutedEventArgs e) {
        _parseCancel?.Cancel();
        ParseButton.IsEnabled = true;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        if (_result == null) StartOverlay.Visibility = Visibility.Visible;
    }

    void ShowStage(int stage) {
        _stage = stage;
        LoadingTitle.Text = Strings.Reading(_parsingLabel);
        Step(StepRead, Strings.StepRead, MftReader.StageRead);
        Step(StepPaths, Strings.StepPaths, MftReader.StagePaths);
        Step(StepFlags, Strings.StepFlags, MftReader.StageFlags);

        void Step(TextBlock block, string text, int index) {
            block.Text = text;
            block.Foreground = Res(index < stage ? "Green" : index == stage ? "Text" : "Muted");
        }
    }

    void UpdateHeader() {
        var has = _result != null;
        VolumeChip.Visibility = RecordsChip.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (_result == null) return;
        VolumeText.Text = _result.Source.IsLive ? $"{_result.Source.Label} · NTFS" : _result.Source.Label;
        RecordsText.Text = Strings.RecordsChip(_result.Found);
        BootChip.Visibility = _result.Source.IsLive ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnPreset(object sender, RoutedEventArgs e) {
        var now = DateTime.Now;
        DateTime? from = sender == PresetBoot ? _boot
            : sender == PresetHour ? now.AddHours(-1)
            : sender == PresetDay ? now.AddDays(-1)
            : null;

        var quiet = _quiet;
        _quiet = true;
        FromBox.Text = from == null ? "" : FileTime.Shown(from.Value).ToString(RangeFormat, CultureInfo.InvariantCulture);
        ToBox.Text = "";
        _quiet = quiet;
        if (_quiet) return;
        ReadRange();
        RunFilter();
    }

    void OnRangeTyped(object sender, TextChangedEventArgs e) {
        if (_quiet) return;
        _quiet = true;
        PresetBoot.IsChecked = PresetHour.IsChecked = PresetDay.IsChecked = PresetAll.IsChecked = false;
        _quiet = false;
        Restart();
    }

    void ReadRange() {
        _filter.From = ParseTime(FromBox, 0);
        _filter.To = ParseTime(ToBox, 0);
    }

    static long ParseTime(TextBox box, long fallback) {
        var text = box.Text.Trim();
        box.ClearValue(Control.BorderBrushProperty);
        if (text.Length == 0) return 0;
        if (DateTime.TryParseExact(text, new[] { RangeFormat, "yyyy-MM-dd HH:mm", "yyyy-MM-dd" }, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var time))
            return FileTime.From(time);
        box.BorderBrush = Res("Red");
        return fallback;
    }

    void OnFieldChanged(object sender, SelectionChangedEventArgs e) {
        if (_quiet || FieldBox.SelectedIndex < 0) return;
        _filter.Field = (DateField)FieldBox.SelectedIndex;
        RunFilter();
    }

    void OnColumnFilter(object sender, TextChangedEventArgs e) {
        if (e.OriginalSource is not TextBox box || FindParent<DataGridColumnHeader>(box)?.Column is not { } column) return;
        e.Handled = true;
        var text = box.Text.Trim();
        if (text.Length == 0) _filter.ColumnFilters.Remove(column.SortMemberPath);
        else _filter.ColumnFilters[column.SortMemberPath] = text;
        Restart();
    }

    void OnBoolFilter(object sender, RoutedEventArgs e) {
        if (e.OriginalSource is not CheckBox box || FindParent<DataGridColumnHeader>(box)?.Column is not { } column) return;
        e.Handled = true;
        if (box.IsChecked == null) _filter.ColumnFilters.Remove(column.SortMemberPath);
        else _filter.ColumnFilters[column.SortMemberPath] = box.IsChecked == true ? "True" : "False";
        RunFilter();
    }

    void OnGridMenu(object sender, ContextMenuEventArgs e) {
        if (e.OriginalSource is not DependencyObject source || FindParent<DataGridColumnHeader>(source) == null) {
            EntriesGrid.ContextMenu = null;
            return;
        }

        var menu = new ContextMenu();
        foreach (var column in EntriesGrid.Columns) {
            var item = new MenuItem {
                Header = column.Header,
                IsCheckable = true,
                IsChecked = column.Visibility == Visibility.Visible,
                StaysOpenOnClick = true,
            };
            var target = column;
            item.Click += (_, _) => target.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            menu.Items.Add(item);
        }
        EntriesGrid.ContextMenu = menu;
    }

    void OnQueryChanged(object sender, TextChangedEventArgs e) {
        if (_quiet) return;
        Restart();
    }

    void Restart() {
        _debounce.Stop();
        _debounce.Start();
    }

    void OnToggle(object sender, RoutedEventArgs e) {
        if (_quiet) return;
        RunFilter();
    }

    void OnCategoryChanged(object sender, SelectionChangedEventArgs e) {
        if (_quiet || CategoryList.SelectedItem is not SideItem { IsHeader: false } item) return;
        _filter.Category = item.Key;
        RunFilter();
    }

    void OnTimestompChip(object sender, MouseButtonEventArgs e) {
        _filter.Category = "timestomp";
        UpdateSide();
        RunFilter();
    }

    void OnSorting(object sender, DataGridSortingEventArgs e) {
        e.Handled = true;
        var key = e.Column.SortMemberPath;
        var descending = e.Column.SortDirection != ListSortDirection.Descending;
        if (_filter.SortKey != key) descending = EntryFilter.StartsDescending(key);

        foreach (var column in EntriesGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        _filter.SortKey = key;
        _filter.Descending = descending;
        RunFilter();
    }

    async void RunFilter() {
        if (_result == null) return;

        _filterCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _filterCancel = cancel;

        _filter.Query = SearchBox.Text;
        _filter.HideNoise = HideNoiseBox.IsChecked == true;
        _filter.ShowDirectories = FoldersBox.IsChecked == true;
        _filter.ShowAdsRows = AdsRowsBox.IsChecked == true;
        var state = _filter.Copy();
        var entries = _result.Entries;
        var adsRows = _result.AdsRows;
        WarnText.Text = Strings.Filtering;

        FilterResult found;
        try {
            found = await Task.Run(() => EntryFilter.Apply(entries, adsRows, state, cancel.Token));
        } catch (OperationCanceledException) {
            return;
        }
        if (cancel.IsCancellationRequested) return;

        _last = found;
        var keep = _selected;
        EntriesGrid.ItemsSource = found.Rows;
        if (keep != null && found.Rows.Contains(keep)) {
            EntriesGrid.SelectedItem = keep;
            EntriesGrid.ScrollIntoView(keep);
        } else if (found.Rows.Count > 0) {
            EntriesGrid.SelectedIndex = 0;
            EntriesGrid.ScrollIntoView(found.Rows[0]);
        } else {
            ShowDetail(null);
        }

        UpdateSide();
        UpdateStatus();
    }

    void UpdateSide() {
        var quiet = _quiet;
        _quiet = true;
        var items = new List<SideItem>();
        SideItem? current = null;
        for (var i = 0; i < Category.All.Length; i++) {
            var category = Category.All[i];
            var item = new SideItem {
                Key = category.Key,
                Label = category.Label(),
                IsHeader = category.Header,
                Count = category.Header || _last == null ? "" : Strings.Count(_last.Counts[i]),
                Dot = category.Dot == null ? null : Res(category.Dot),
                Indent = category.Header ? new Thickness(2, i == 0 ? 0 : 10, 0, 0) : category.Sub ? new Thickness(10, 0, 0, 0) : new Thickness(0),
            };
            items.Add(item);
            if (item.Key == _filter.Category) current = item;
        }
        CategoryList.ItemsSource = items;
        CategoryList.SelectedItem = current;

        var stomped = _last?.Timestomped ?? 0;
        TimestompChip.Visibility = stomped > 0 ? Visibility.Visible : Visibility.Collapsed;
        TimestompText.Text = Strings.TimestompChip(stomped);
        _quiet = quiet;
    }

    void UpdateStatus() {
        if (_result == null) {
            StatusText.Text = "";
            WarnText.Text = "";
            return;
        }

        StatusText.Text = Strings.Status(_result.Found, _result.Deleted, _last?.Rows.Count ?? 0);
        var notes = new List<string> { Strings.ParsedIn(_result.Elapsed) };   
        if (_result.Orphans > 0) notes.Add(Strings.OrphansNote(_result.Orphans));
        if (_result.Torn > 0) notes.Add(Strings.TornNote(_result.Torn));
        if (_result.Source.Notes.Contains("attrlist")) notes.Add(Strings.AttributeListNote);
        WarnText.Text = string.Join(" · ", notes);
    }

    void OnEntrySelected(object sender, SelectionChangedEventArgs e) {
        _selected = EntriesGrid.SelectedItem as MftEntry;
        ShowDetail(_selected);
    }

    void OnTab(object sender, RoutedEventArgs e) {
        if (SummaryPane == null) return;
        SummaryPane.Visibility = SummaryTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AttributesPane.Visibility = AttributesTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        StreamsPane.Visibility = StreamsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RecordPane.Visibility = RecordTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (RecordTab.IsChecked == true) ShowHex(_selected);
        if (AttributesTab.IsChecked == true) ShowAttributes(_selected);
    }

    void ShowDetail(MftEntry? entry) {
        var streams = entry?.Base.Streams ?? new List<DataStream>();
        StreamsTab.Content = streams.Count > 0 ? $"{Strings.TabStreams}  {streams.Count}" : Strings.TabStreams;
        OpenFolderButton.IsEnabled = CopyButton.IsEnabled = entry != null;

        if (entry == null) {
            DetailName.Text = Strings.SelectRecord;
            DetailMeta.Text = "";
            ReasonList.ItemsSource = null;
            for (var i = 0; i < 4; i++) _siTimes[i].Text = _fnTimes[i].Text = "";
            InfoGrid.Children.Clear();
            InfoGrid.RowDefinitions.Clear();
            StreamList.ItemsSource = null;
            NoStreamsText.Text = "";
            HexText.Inlines.Clear();
            AttributeList.ItemsSource = null;
            return;
        }

        DetailName.Text = entry.Name.Length == 0 ? "\\" : entry.Name;
        DetailMeta.Text = $"{entry.FullPath}   ·   {Strings.RecordMeta(entry.Index, entry.Sequence)}   ·   {Strings.ParentMeta(entry.ParentIndex, entry.ParentSequence)}";

        ReasonList.ItemsSource = Reasons(entry);
        ShowTimes(entry);
        ShowInfo(entry);
        ShowStreams(entry.Base, streams);
        if (RecordTab.IsChecked == true) ShowHex(entry);
        if (AttributesTab.IsChecked == true) ShowAttributes(entry);
    }

    void ShowAttributes(MftEntry? entry) {
        AttributeList.ItemsSource = null;
        if (entry == null || _result == null) return;
        try {
            var raw = _result.Source.ReadRecord(entry.Index);
            AttributeList.ItemsSource = AttributeInspector.Inspect(raw, resident => resident ? Strings.FormResident : Strings.FormNonResident);
        } catch (Exception ex) {
            AttributesNote.Text = Strings.HexFailed(ex.Message);
        }
    }

    List<Reason> Reasons(MftEntry entry) {
        var list = new List<Reason>();
        if (entry.IsAds && entry.Stream != null) list.Add(new Reason(Strings.ReasonAdsRow(entry.Stream.Name, entry.Base.Name), "Accent"));
        if (!entry.InUse) list.Add(new Reason(Strings.ReasonDeleted, "Red"));
        if (entry.Has(EntryFlags.SiBeforeFn)) list.Add(new Reason(Strings.ReasonSiBeforeFn, "Orange"));
        if (entry.Has(EntryFlags.ZeroFraction)) list.Add(new Reason(Strings.ReasonZeroFraction, "Orange"));
        if (entry.IsDownloaded) list.Add(new Reason(Strings.ReasonDownloaded(HostOf(entry)), "Violet"));
        if (!entry.IsAds && entry.Has(EntryFlags.Ads)) list.Add(new Reason(Strings.ReasonAds, "Accent"));
        if (entry.ReparseTarget.Length > 0) list.Add(new Reason(Strings.ReasonReparse(entry.ReparseTarget), "Muted"));
        if (entry.IsOrphan) list.Add(new Reason(Strings.ReasonOrphan, "Muted"));
        return list;
    }

    static string HostOf(MftEntry entry) {
        var zone = entry.Base.Streams?.FirstOrDefault(s => s.Name.Equals("Zone.Identifier", StringComparison.OrdinalIgnoreCase));
        if (zone?.Content == null) return "";
        foreach (var line in MftReader.Text(zone.Content).Split('\n')) {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("HostUrl=", StringComparison.OrdinalIgnoreCase)) continue;
            return Uri.TryCreate(trimmed.Substring(8), UriKind.Absolute, out var uri) ? uri.Host : "";
        }
        return "";
    }

    void ShowTimes(MftEntry entry) {
        long[] si = { entry.SiCreated, entry.SiModified, entry.SiChanged, entry.SiAccessed };
        long[] fn = { entry.FnCreated, entry.FnModified, entry.FnChanged, entry.FnAccessed };
        for (var i = 0; i < 4; i++) {
            _siTimes[i].Text = FileTime.Precise(si[i]);
            _fnTimes[i].Text = FileTime.Precise(fn[i]);
            _siTimes[i].Foreground = _fnTimes[i].Foreground = Res("Text");
            _siTimes[i].Background = Brushes.Transparent;
        }

        if (entry.Has(EntryFlags.SiBeforeFn)) {
            _siTimes[0].Foreground = Res("Orange");
            _siTimes[0].Background = Res("OrangeFaint");
        }
        if (entry.Has(EntryFlags.ZeroFraction)) _siTimes[0].Foreground = Res("Orange");
    }

    void ShowInfo(MftEntry entry) {
        InfoGrid.Children.Clear();
        InfoGrid.RowDefinitions.Clear();

        var state = entry.IsDirectory ? $"{Strings.StateDirectory} · " : "";
        Add(Strings.KeyState, state + (entry.InUse ? Strings.StateInUse : Strings.StateDeleted), entry.InUse ? "Green" : "Red");
        if (!entry.IsDirectory) {
            var allocated = entry.Resident ? Strings.ResidentAllocated : Strings.Count(entry.Allocated) + " B";
            Add(Strings.KeySize, $"{Strings.Count(entry.Size)} B  ({Strings.Allocated(allocated)})");
            Add(Strings.KeyData, !entry.HasData ? Strings.DataNone : entry.Resident ? Strings.DataResident : Strings.DataRuns(entry.Runs));
        }
        Add("SiFlags", entry.SiFlags.Length == 0 ? "-" : entry.SiFlags);
        Add("NameType", entry.NameTypeText);
        if (entry.ShortName != null) Add(Strings.KeyShortName, entry.ShortName);
        Add("ReferenceCount", entry.LinkCount.ToString(CultureInfo.InvariantCulture));
        Add(Strings.KeyParent, $"{entry.ParentIndex} / seq {entry.ParentSequence}");
        Add("UpdateSequenceNumber", entry.Usn.ToString(CultureInfo.InvariantCulture));
        Add("LogfileSequenceNumber", entry.Lsn.ToString(CultureInfo.InvariantCulture));
        Add("SecurityId", entry.SecurityId.ToString(CultureInfo.InvariantCulture));
        if (entry.ObjectIdText.Length > 0) Add("ObjectIdFileDroid", entry.ObjectIdText);
        if (entry.ReparseTarget.Length > 0) Add("ReparseTarget", entry.ReparseTarget);
        if (entry.LoggedUtilStream.Length > 0) Add("LoggedUtilStream", entry.LoggedUtilStream);

        void Add(string key, string value, string? color = null) {
            var row = InfoGrid.RowDefinitions.Count;
            InfoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = key, Foreground = Res("Muted"), FontSize = 12, Margin = new Thickness(0, 3, 16, 3), VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBox {
                Text = value,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Res(color ?? "Text"),
                FontFamily = (FontFamily)FindResource("Mono"),
                FontSize = 13,
                Margin = new Thickness(0, 3, 0, 3),
                Padding = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(label, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            InfoGrid.Children.Add(label);
            InfoGrid.Children.Add(text);
        }
    }

    void ShowStreams(MftEntry entry, List<DataStream> streams) {
        NoStreamsText.Text = streams.Count == 0 ? Strings.NoStreams : "";
        NoStreamsText.Visibility = streams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StreamList.ItemsSource = streams.Select(s => new StreamView {
            Title = $"{entry.Name}:{s.Name}",
            Meta = s.Resident ? Strings.StreamResident(Format.Size(s.Size)) : Strings.StreamNonResident(Format.Size(s.Size)),
            Body = StreamBody(s),
        }).ToList();
    }

    static string StreamBody(DataStream stream) {
        if (stream.Content == null) return Strings.StreamNotRead;
        if (stream.Content.Length == 0) return "";
        if (LooksLikeText(stream.Content)) return MftReader.Text(stream.Content).TrimEnd('\0', '\r', '\n');

        var sb = new StringBuilder();
        sb.AppendLine(Strings.StreamBinary(Format.Size(stream.Content.Length)));
        sb.AppendLine();
        var shown = Math.Min(stream.Content.Length, 256);
        for (var i = 0; i < shown; i += 16) {
            var count = Math.Min(16, shown - i);
            sb.Append(i.ToString("X4")).Append("  ");
            for (var j = 0; j < 16; j++) sb.Append(j < count ? stream.Content[i + j].ToString("X2") + " " : "   ");
            sb.Append(' ');
            for (var j = 0; j < count; j++) sb.Append(Printable(stream.Content[i + j]));
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    static bool LooksLikeText(byte[] content) {
        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE) return true;
        var printable = 0;
        foreach (var b in content) if (b is >= 0x20 and < 0x7F or 0x09 or 0x0A or 0x0D) printable++;
        return printable >= content.Length * 0.9;
    }

    static char Printable(byte b) => b is >= 0x20 and < 0x7F ? (char)b : '.';

    void ShowHex(MftEntry? entry) {
        HexText.Inlines.Clear();
        if (entry == null || _result == null) return;

        byte[] raw;
        try {
            raw = _result.Source.ReadRecord(entry.Index);
        } catch (Exception ex) {
            HexText.Inlines.Add(new Run(Strings.HexFailed(ex.Message)) { Foreground = Res("Red") });
            return;
        }

        var colors = HexColors(raw);
        var muted = Res("Muted");
        for (var line = 0; line < raw.Length; line += 16) {
            HexText.Inlines.Add(new Run(line.ToString("X4") + "  ") { Foreground = muted });

            var sb = new StringBuilder();
            string? color = null;
            for (var i = line; i < line + 16 && i < raw.Length; i++) {
                if (colors[i] != color && sb.Length > 0) {
                    HexText.Inlines.Add(new Run(sb.ToString()) { Foreground = Res(color ?? "Muted") });
                    sb.Clear();
                }
                color = colors[i];
                sb.Append(raw[i].ToString("X2")).Append(' ');
            }
            if (sb.Length > 0) HexText.Inlines.Add(new Run(sb.ToString()) { Foreground = Res(color ?? "Muted") });

            var ascii = new StringBuilder(" ");
            for (var i = line; i < line + 16 && i < raw.Length; i++) ascii.Append(Printable(raw[i]));
            HexText.Inlines.Add(new Run(ascii.ToString()) { Foreground = muted });
            if (line + 16 < raw.Length) HexText.Inlines.Add(new LineBreak());
        }
    }

    static string?[] HexColors(byte[] raw) {
        var colors = new string?[raw.Length];
        if (raw.Length < 0x30 || U32(raw, 0) != 0x454C4946) return colors;

        int first = raw[0x14] | raw[0x15] << 8;
        for (var i = 0; i < Math.Min(first, raw.Length); i++) colors[i] = "Accent";

        var offset = first;
        while (offset + 8 <= raw.Length) {
            var type = U32(raw, offset);
            if (type == 0xFFFFFFFF) {
                for (var i = offset; i < Math.Min(offset + 8, raw.Length); i++) colors[i] = "Accent";
                break;
            }
            var length = (int)U32(raw, offset + 4);
            if (length < 16 || offset + length > raw.Length) break;

            var color = type switch { 0x10 => "Orange", 0x30 => "Green", 0x80 => "Violet", _ => "Text" };
            for (var i = offset; i < offset + length; i++) colors[i] = color;
            offset += length;
        }
        return colors;

        static uint U32(byte[] b, int at) => BitConverter.ToUInt32(b, at);
    }

    void OnGridDoubleClick(object sender, MouseButtonEventArgs e) {
        if (e.OriginalSource is DependencyObject source && FindParent<DataGridRow>(source) != null) OnOpenFolder(sender, e);
    }

    static T? FindParent<T>(DependencyObject child) where T : DependencyObject {
        var current = child;
        while (current != null && current is not T)
            current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        return current as T;
    }

    void OnOpenFolder(object sender, RoutedEventArgs e) {
        var entry = _selected;
        if (entry == null || _result?.Source.IsLive != true) return;

        try {
            var folder = RealPath(entry.Base.Folder);
            var path = folder + "\\" + entry.Base.Name;
            if (entry.InUse && (File.Exists(path) || Directory.Exists(path))) {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            } else if (Directory.Exists(folder)) {
                Process.Start("explorer.exe", $"\"{folder}\"");
            } else {
                WarnText.Text = Strings.PathGone;
            }
        } catch (Exception ex) {
            WarnText.Text = ex.Message;
        }
    }

    string RealPath(string folder) {
        var root = _result?.Source.Root ?? "";
        return folder == "." ? root + "\\" : folder.StartsWith(".\\") ? root + folder.Substring(1) : folder;
    }

    async void OnCopy(object sender, RoutedEventArgs e) {
        var entry = _selected;
        if (entry == null) return;

        var sb = new StringBuilder();
        void Field(string name, object value) => sb.AppendLine($"{name,-22} {value}");
        Field("EntryNumber", entry.Index);
        Field("SequenceNumber", entry.Sequence);
        Field("InUse", entry.InUse);
        Field("ParentEntryNumber", entry.ParentIndex);
        Field("ParentSequenceNumber", entry.ParentSequence);
        Field("ParentPath", entry.Folder);
        Field("FileName", entry.Name);
        Field("Extension", entry.ExtensionText);
        Field("FileSize", entry.Size);
        Field("ReferenceCount", entry.LinkCount);
        Field("ReparseTarget", entry.ReparseTarget);
        Field("IsDirectory", entry.IsDirectory);
        Field("HasAds", entry.HasAds);
        Field("IsAds", entry.IsAds);
        Field("SI<FN", entry.SiLessFn);
        Field("uSecZeros", entry.USecZeros);
        Field("Copied", entry.Copied);
        Field("SiFlags", entry.SiFlags);
        Field("NameType", entry.NameTypeText);
        Field("Created0x10", entry.Created10);
        Field("Created0x30", entry.Created30);
        Field("LastModified0x10", entry.Modified10);
        Field("LastModified0x30", entry.Modified30);
        Field("LastRecordChange0x10", entry.Changed10);
        Field("LastRecordChange0x30", entry.Changed30);
        Field("LastAccess0x10", entry.Accessed10);
        Field("LastAccess0x30", entry.Accessed30);
        Field("UpdateSequenceNumber", entry.Usn);
        Field("LogfileSequenceNumber", entry.Lsn);
        Field("SecurityId", entry.SecurityId);
        Field("ObjectIdFileDroid", entry.ObjectIdText);
        Field("LoggedUtilStream", entry.LoggedUtilStream);
        Field("ZoneIdContents", entry.ZoneIdContents);
        var reasons = Reasons(entry);
        if (reasons.Count > 0) {
            sb.AppendLine();
            foreach (var reason in reasons) sb.AppendLine("- " + reason.Text);
        }
        foreach (var stream in entry.Base.Streams ?? new List<DataStream>()) {
            sb.AppendLine();
            sb.AppendLine($"{entry.Base.Name}:{stream.Name} ({Format.Size(stream.Size)})");
            sb.AppendLine(StreamBody(stream));
        }

        try {
            Clipboard.SetText(sb.ToString().TrimEnd());
            CopyButton.Content = Strings.Copied;
            await Task.Delay(1200);
        } catch (COMException) {
        }
        CopyButton.Content = Strings.Copy;
    }

    protected override void OnClosed(EventArgs e) {
        _parseCancel?.Cancel();
        _filterCancel?.Cancel();
        _result?.Source.Dispose();
        base.OnClosed(e);
    }

    [DllImport("kernel32.dll")]
    static extern ulong GetTickCount64();

    sealed class Reason {
        public Reason(string text, string color) {
            Text = text;
            Foreground = Res(color);
            Background = Res(color + "Faint");
        }

        public string Text { get; }
        public Brush Foreground { get; }
        public Brush Background { get; }
    }

    sealed class StreamView {
        public string Title { get; set; } = "";
        public string Meta { get; set; } = "";
        public string Body { get; set; } = "";
    }
}

public sealed class VolumeOption {
    public char Letter { get; set; }
    public bool IsNtfs { get; set; }
    public bool IsFile { get; set; }
    public string VolumeLabel { get; set; } = "";
    public string Format { get; set; } = "";
    public long TotalSize { get; set; }

    public string Drive => IsFile ? "" : $"{Letter}:";
    public string ShortLabel => IsFile ? Strings.OpenFileOption : $@"{Letter}:\";
    public string Name { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public string Free { get; private set; } = "";

    public void Refresh() {
        Name = VolumeLabel.Length > 0 ? VolumeLabel : Strings.LocalDisk;
        Detail = $"{Format} · {Models.Format.Size(TotalSize)}";
        Free = IsNtfs ? "" : Strings.NoMft;
    }

    public static VolumeOption From(DriveInfo drive) {
        var option = new VolumeOption { Letter = drive.Name[0] };
        try {
            option.Format = drive.DriveFormat;
            option.VolumeLabel = drive.VolumeLabel;
            option.TotalSize = drive.TotalSize;
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
        option.IsNtfs = option.Format.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
        option.Refresh();
        return option;
    }

    public static VolumeOption File() => new() { IsFile = true };
}
