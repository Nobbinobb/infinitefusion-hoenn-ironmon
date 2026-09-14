using System.IO;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ironmon.Setup.Core;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;
using Microsoft.Win32;

namespace Ironmon.Setup;

/// <summary>
/// Presents the approved disposable Location, Options, Install and Ready flow using native Windows controls.
/// </summary>
internal sealed class SetupWindow : Window
{
    private const string DefaultDirectory = "Programs";
    private const string FontName = "Segoe UI";
    private const string NumberFormat = "N1";
    private const string DateFormat = "g";
    private const string ThemeUri = "/Ironmon Setup;component/SetupTheme.xaml";
    private const string BackgroundResource = "SetupBackground";
    private const string ForegroundResource = "SetupForeground";
    private const string MutedResource = "SetupMuted";
    private const string BorderResource = "SetupBorder";
    private const string SurfaceResource = "SetupSurface";
    private const string AccentResource = "SetupAccent";
    private const string WarningResource = "SetupWarning";
    private const string SelectedResource = "SetupSelected";
    private const string NotSelectedResource = "SetupNotSelected";
    private const string PackageGroup = "TrackerPackage";
    private readonly SetupSession _session;
    private bool _displayedBusy;
    private readonly System.Windows.Threading.DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StackPanel _body = new() { Margin = new Thickness(24, 12, 24, 12) };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _reviewHelp = new() { FontSize = 12, Margin = new Thickness(14, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Right, Visibility = Visibility.Collapsed };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 36, Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _steps = new() { Orientation = Orientation.Horizontal };
    private readonly ProgressBar _progress = new() { Height = 5, Margin = new Thickness(0, 0, 0, 14) };
    private readonly TextBox _destination = new();
    private Button? _install;
    private Button? _back;
    private Button? _cancel;
    private Button? _retry;
    private bool _sprites;
    private bool _shortcut;
    private bool _webViewConsent;
    private bool _noActiveRun;
    private string _flavor = ReleaseProtocol.SelfContained;
    private int _page;
    private int _conflictIndex;
    private bool _showConflicts;
    private SetupReview? _displayedReview;
    private bool _displayedOptionalIncomplete;
    private string? _bodyError;

    /// <summary>
    /// Builds a self-contained native window that remains available while the game and tracker are closed.
    /// </summary>
    /// <param name="session">The real shared installer workflow.</param>
    internal SetupWindow(SetupSession session)
    {
        _session = session;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(ThemeUri, UriKind.Relative) });
        Title = UpdaterText.SetupWindowIronmonSetup;
        Width = 680;
        Height = 650;
        MinWidth = 680;
        MinHeight = 600;
        Background = Brush(BackgroundResource);
        Foreground = Brush(ForegroundResource);
        _status.Foreground = Brush(MutedResource);
        _error.Foreground = Brush(WarningResource);
        FontFamily = new FontFamily(FontName);
        FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _destination.Text = session.Destination?.Root ?? session.InstallationRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DefaultDirectory);
        AutomationProperties.SetName(_destination, UpdaterText.SetupWindowInstallationFolder);
        var layout = new DockPanel { Background = Background };
        var header = new StackPanel { Margin = new Thickness(24, 16, 24, 0) };
        header.Children.Add(new TextBlock { Text = UpdaterText.SetupWindowBrand, FontSize = 11, Foreground = Brush(MutedResource), Margin = new Thickness(0, 0, 0, 8) });
        header.Children.Add(new Border { Child = _steps, BorderBrush = Brush(BorderResource), BorderThickness = new Thickness(0, 0, 0, 1) });
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var footer = new StackPanel { Margin = new Thickness(24, 0, 24, 16) };
        footer.Children.Add(new Border { Height = 1, Background = Brush(BorderResource), Margin = new Thickness(0, 0, 0, 8) });
        var statusRow = new Grid();
        statusRow.ColumnDefinitions.Add(new ColumnDefinition());
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusRow.Children.Add(_status);
        Grid.SetColumn(_reviewHelp, 1);
        var help = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(UpdaterText.SetupReviewWhatHappensNext)) { Foreground = Brush(AccentResource) };
        help.Click += (_, _) => MessageBox.Show(this, UpdaterText.SetupWindowSaveYourGameBeforeInstallingSetupWillCloseThe, UpdaterText.SetupReviewWhatHappensNext, MessageBoxButton.OK, MessageBoxImage.Information);
        _reviewHelp.Inlines.Add(help);
        statusRow.Children.Add(_reviewHelp);
        footer.Children.Add(statusRow);
        footer.Children.Add(_error);
        footer.Children.Add(_progress);
        footer.Children.Add(_actions);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        layout.Children.Add(_body);
        Content = layout;
        _session.Changed += HandleChanged;
        _progressTimer.Tick += (_, _) => Refresh();
        Closing += HandleClosing;
        Closed += HandleClosed;
        PreviewKeyDown += HandleKey;
        Render();
    }

    /// <summary>
    /// Builds the selected review step while leaving progress updates independent of content and keyboard focus.
    /// </summary>
    private void Render()
    {
        _displayedBusy = _session.IsBusy;
        if (_destination.Parent is Panel parent)
            parent.Children.Remove(_destination);

        _body.Children.Clear();
        _actions.Children.Clear();
        _install = null;
        _retry = null;
        _back = null;
        _bodyError = null;
        _steps.Children.Clear();
        string[] titles = [UpdaterText.SetupWindow1Location, UpdaterText.SetupWindow2Options, UpdaterText.SetupWindow3Install, UpdaterText.SetupWindow4Ready];
        for (var index = 0; index < titles.Length; index++)
        {
            _steps.Children.Add(new Border
            {
                BorderBrush = index == _page ? Brush(AccentResource) : Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 2),
                Background = index == _page ? Brush(SurfaceResource) : Brushes.Transparent,
                Padding = new Thickness(14, 8, 14, 8),
                Child = new TextBlock { Text = titles[index], Foreground = index == _page ? Foreground : Brush(MutedResource), FontWeight = index == _page ? FontWeights.SemiBold : FontWeights.Normal }
            });
        }

        if (_page == 0)
        {
            Heading(UpdaterText.SetupWindowChooseYourGameFolder);
            Paragraph(UpdaterText.SetupWindowChooseAParentOrExistingGameFolder(SetupPreparation.InstallationDirectoryName));
            var folder = new Grid();
            folder.ColumnDefinitions.Add(new ColumnDefinition());
            folder.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            folder.Children.Add(_destination);
            var browse = Action(UpdaterText.SetupWindowBrowse, () =>
            {
                var picker = new OpenFolderDialog { Title = UpdaterText.SetupWindowChooseTheInfiniteFusionInstallationFolder };
                if (picker.ShowDialog(this) == true)
                    _destination.Text = picker.FolderName;
            });
            Grid.SetColumn(browse, 1);
            folder.Children.Add(browse);
            Card(Label(UpdaterText.SetupWindowFolderSection), folder, Description(UpdaterText.SetupWindowForAnExistingGameSelectTheFolderContainingInfiniteFusion2));
            Paragraph(UpdaterText.SetupWindowTheSuggestedLocationInstallsForYourWindowsAccountWindows);
            _actions.Children.Add(AsyncAction(UpdaterText.SetupWindowContinue, ContinueAsync, true));
        }
        else if (_page == 1)
        {
            Heading(UpdaterText.SetupWindowMakeItYours);
            Card(Option(UpdaterText.SetupWindowDownloadSpriteSheets, _sprites, value => _sprites = value, UpdaterText.SetupWindowLargeOptionalDownloadYouCanAlsoDownloadSpritesLater),
                Option(UpdaterText.SetupWindowCreateADesktopShortcut, _shortcut, value => _shortcut = value));
            _body.Children.Add(Label(UpdaterText.SetupWindowPackageSection));
            if (_session.Destination?.InstalledFlavor is { } installedFlavor)
            {
                Card(new TextBlock { Text = PackageLabel(installedFlavor), FontWeight = FontWeights.SemiBold }, Description(UpdaterText.SetupWindowSetupKeepsThePackageUsedByThisInstallation));
            }
            else
            {
                var packages = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                packages.ColumnDefinitions.Add(new ColumnDefinition());
                packages.ColumnDefinitions.Add(new ColumnDefinition());
                var included = PackageChoice(ReleaseProtocol.SelfContained, UpdaterText.SetupWindowRecommendedNoSeparateNETInstallationNeeded);
                included.Margin = new Thickness(0, 0, 5, 0);
                packages.Children.Add(included);
                var required = PackageChoice(ReleaseProtocol.RuntimeRequired, UpdaterText.SetupWindowSmallerDownloadRequiresTheMatchingNETRuntime);
                required.Margin = new Thickness(5, 0, 0, 0);
                Grid.SetColumn(required, 1);
                packages.Children.Add(required);
                _body.Children.Add(packages);
            }
            if (_session.NeedsWebView)
            {
                _body.Children.Add(Option(UpdaterText.SetupWindowInstallMicrosoftWebView2RequiredByTheTracker, _webViewConsent, value => _webViewConsent = value, UpdaterText.SetupWindowDownloadedFromMicrosoftAfterYouChooseInstallWindowsMay));
            }

            _back = Action(UpdaterText.SetupWindowBack, () => Navigate(0));
            _actions.Children.Add(_back);
            _actions.Children.Add(AsyncAction(UpdaterText.SetupWindowReviewInstallation, ReviewAsync, true));
        }
        else if (_page == 2)
        {
            Heading(_session.IsBusy && _session.Review is not null ? UpdaterText.ProgressInstallationInProgress : _showConflicts ? UpdaterText.SetupWindowReviewChangedFiles : _session.NeedsRunConfirmation ? UpdaterText.SetupWindowFinishYourRunFirst : _session.Review is not null || _session.RecoveryId is not null ? UpdaterText.SetupWindowReadyToInstall : _session.Error is not null ? UpdaterText.SetupWindowUnableToReviewInstallation : UpdaterText.SetupWindowCheckingYourInstallation);
            if (_showConflicts && _session.Review is { } conflictReview)
            {
                RenderConflict(conflictReview);
            }
            else if (_session.Review is { } review)
            {
                _displayedReview = review;
                _body.Children.Add(new TextBlock { Text = UpdaterText.SetupWindowInstallationFolder, FontSize = 10, Foreground = Brush(MutedResource), Margin = new Thickness(0, 0, 0, 3) });
                _body.Children.Add(new TextBlock { Text = review.Authorization.Request.InstallationRoot, ToolTip = review.Authorization.Request.InstallationRoot, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 8) });
                RenderReviewDetails(review);

                var conflicts = review.Plan.Entries.Count(entry => entry.Action == FilePlanAction.Conflict);
                if (conflicts > 0)
                {
                    var changes = new Grid();
                    changes.Children.Add(new TextBlock { Text = UpdaterText.SetupReviewLocalChanges, Foreground = Brush(MutedResource), VerticalAlignment = VerticalAlignment.Center });
                    var button = Action(UpdaterText.SetupWindowChangedFilesCount(conflicts), () => { _conflictIndex = 0; _showConflicts = true; Render(); });
                    button.HorizontalAlignment = HorizontalAlignment.Right;
                    button.Margin = new Thickness(0);
                    button.Padding = new Thickness(12, 5, 12, 5);
                    changes.Children.Add(button);
                    Card(changes);
                }

                _install = AsyncAction(review.AlreadyCurrent ? UpdaterText.SetupWindowFinishSetup : UpdaterText.SetupWindowInstall, () => _session.InstallAsync(_sprites, _shortcut, _webViewConsent), true);
            }
            else if (_session.RecoveryId is not null)
            {
                Paragraph(UpdaterText.SetupWindowRecoveryRestoresACompleteInstallationFromItsVerifiedBackups);
                _install = AsyncAction(UpdaterText.SetupWindowRecoverInstallation, _session.RecoverAsync, true);
            }
            else if (_session.NeedsRunConfirmation)
            {
                Paragraph(_session.Error ?? string.Empty);
                Card(Option(UpdaterText.SetupWindowMyIronmonRunIsFinished, _noActiveRun, value => { _noActiveRun = value; Refresh(); }));
            }
            else if (_session.Error is { } reviewError)
            {
                _bodyError = reviewError;
                Card(new TextBlock { Text = reviewError, ToolTip = reviewError, Foreground = Brush(WarningResource), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 180 });
            }

            _back = Action(UpdaterText.SetupWindowBack, BackFromReview);
            _actions.Children.Add(_back);
            if (!_showConflicts)
            {
                _retry = AsyncAction(UpdaterText.SetupWindowCheckAgain, ReviewAsync, _install is null);
                _actions.Children.Add(_retry);
            }

            if (_install is not null)
                _actions.Children.Add(_install);
        }
        else
        {
            _displayedOptionalIncomplete = _session.OptionalIncomplete;
            Heading(UpdaterText.SetupWindowYouReReadyToPlay);
            Paragraph(UpdaterText.SetupWindowYourGameAndIronmonAreInstalledFutureUpdatesAnd);
            if (_session.OptionalIncomplete)
            {
                Paragraph(UpdaterText.SetupWindowOptionalWorkIsIncompleteRetryNowOrFinishAnd);
                _retry = AsyncAction(UpdaterText.SetupWindowRetryOptionalWork, _session.RetryOptionalAsync);
                _actions.Children.Add(_retry);
            }

            _install = AsyncAction(UpdaterText.SetupWindowOpenTracker, _session.OpenTrackerAsync, true);
            _actions.Children.Add(_install);
        }

        _cancel = Action(_session.CoreInstalled ? UpdaterText.SetupWindowFinish : UpdaterText.SetupWindowCancel, Finish);
        _actions.Children.Add(_cancel);
        Refresh();
    }

    /// <summary>
    /// Presents one changed file at a time so every replacement has explicit consent without a scrolling list.
    /// </summary>
    /// <param name="review">The authenticated file plan.</param>
    private void RenderConflict(SetupReview review)
    {
        FilePlanEntry[] conflicts = [.. review.Plan.Entries.Where(entry => entry.Action == FilePlanAction.Conflict)];
        _conflictIndex = Math.Clamp(_conflictIndex, 0, conflicts.Length - 1);
        var entry = conflicts[_conflictIndex];
        Paragraph(UpdaterText.SetupWindowFileOf(_conflictIndex + 1, conflicts.Length));
        Card(new TextBlock { Text = entry.Path, ToolTip = entry.Path, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 100 });
        if (entry.CanReplaceAfterBackup)
        {
            Paragraph(UpdaterText.SetupWindowThisFileDiffersFromTheReleasedVersionSetupWill);
            _body.Children.Add(Option(UpdaterText.SetupWindowBackUpAndReplaceThisFile, _session.IsApproved(entry.Path), value => _session.Approve(entry.Path, value)));
        }
        else
        {
            Paragraph(UpdaterText.SetupWindowThisFileOrFolderPreventsInstallationMoveItOut);
        }

        var navigation = new StackPanel { Orientation = Orientation.Horizontal };
        var previous = Action(UpdaterText.SetupWindowPreviousFile, () => { _conflictIndex--; Render(); });
        previous.IsEnabled = _conflictIndex > 0;
        navigation.Children.Add(previous);
        var next = Action(UpdaterText.SetupWindowNextFile, () => { _conflictIndex++; Render(); });
        next.IsEnabled = _conflictIndex < conflicts.Length - 1;
        navigation.Children.Add(next);
        _body.Children.Add(navigation);
    }

    /// <summary>
    /// Returns from file conflicts without discarding approvals, or returns from installation review to options.
    /// </summary>
    private void BackFromReview()
    {
        if (_showConflicts)
        {
            _showConflicts = false;
            Render();
        }
        else
        {
            Navigate(1);
        }
    }

    /// <summary>
    /// Navigates between installer choices without changing any tracker view.
    /// </summary>
    /// <param name="page">The selected installer step.</param>
    private void Navigate(int page)
    {
        _session.ClearReview();
        _noActiveRun = false;
        _showConflicts = false;
        _page = page;
        Render();
    }

    /// <summary>
    /// Validates the chosen folder before showing only the package choices applicable to that installation.
    /// </summary>
    /// <returns>The local folder inspection and resulting navigation.</returns>
    private async Task ContinueAsync()
    {
        await _session.SelectDestinationAsync(_destination.Text);
        if (_session.Error is null)
        {
            _noActiveRun = false;
            _page = 1;
        }

        Render();
    }

    /// <summary>
    /// Cancels active work safely or closes Setup after the operation has stopped.
    /// </summary>
    private void Finish()
    {
        if (_session.IsBusy)
        {
            _session.Cancel();
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// Opens review before asynchronous inspection so progress is visible without extra confirmation screens.
    /// </summary>
    /// <returns>The complete signed review.</returns>
    private async Task ReviewAsync()
    {
        _session.ClearReview();
        _showConflicts = false;
        _page = 2;
        Render();
        var destination = _session.Destination ?? throw new InvalidOperationException(UpdaterText.SetupSessionReviewTheInstallationFolderFirst);
        await _session.ReviewAsync(destination.Root, destination.InstalledFlavor ?? _flavor, _noActiveRun);
        Render();
    }

    /// <summary>
    /// Refreshes progress and enabled actions without rebuilding the active page on every download event.
    /// </summary>
    private void Refresh()
    {
        _status.Text = _session.Status;
        var showReminder = _page == 2 && !_showConflicts && !_session.IsBusy && !_session.CoreInstalled && _session.Review is not null;
        _status.Foreground = Brush(showReminder ? WarningResource : MutedResource);
        _reviewHelp.Visibility = showReminder ? Visibility.Visible : Visibility.Collapsed;
        if (showReminder)
            _status.Text = UpdaterText.SetupReviewSaveReminder;
        _error.Text = _session.Error ?? string.Empty;
        _error.ToolTip = _session.Error;
        _error.Visibility = _session.Error is null || _session.NeedsRunConfirmation || _session.Error == _bodyError ? Visibility.Collapsed : Visibility.Visible;
        _progress.Visibility = _session.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        _progress.IsIndeterminate = _session.Progress is { } current ? current.Total <= 0 : _session.Download is not { Total: > 0 } && _session.SpriteProgress is not { TotalSheetCount: > 0 };
        if (_session.IsBusy && _session.Progress is { } measured)
        {
            _status.Text = measured.Text;
            _progress.Maximum = Math.Max(1, measured.Total);
            _progress.Value = measured.Completed;
        }
        else if (_session.Download is { Total: > 0 } download)
        {
            _progress.IsIndeterminate = false;
            _progress.Maximum = download.Total;
            _progress.Value = download.Received;
        }
        else if (_session.CoreInstalled && _session.SpriteProgress is { TotalSheetCount: > 0 } sprite)
        {
            _progress.IsIndeterminate = false;
            _progress.Maximum = sprite.TotalSheetCount;
            _progress.Value = sprite.CompletedSheetCount;
            _status.Text += UpdaterText.SetupWindowSheets(sprite.CompletedSheetCount, sprite.TotalSheetCount);
        }

        if (_session.IsBusy)
        {
            _status.Text += Environment.NewLine + UpdaterText.ProgressElapsed((Environment.TickCount64 - _session.StartedAt) / 60000d, (Environment.TickCount64 - _session.ProgressAt) / 1000L);
            _progressTimer.Start();
        }
        else
        {
            _progressTimer.Stop();
        }

        _body.IsEnabled = !_session.IsBusy;
        foreach (var button in _actions.Children.OfType<Button>())
            button.IsEnabled = !_session.IsBusy;

        _back?.IsEnabled = !_session.IsBusy;
        _retry?.IsEnabled = !_session.IsBusy && (!_session.NeedsRunConfirmation || _noActiveRun);
        _install?.IsEnabled = !_session.IsBusy && (_session.CoreInstalled || _session.RecoveryId is not null || _session.CanInstall && (!_session.NeedsWebView || _webViewConsent));
        _cancel?.Content = _session.IsBusy ? UpdaterText.SetupWindowCancel : _session.CoreInstalled ? UpdaterText.SetupWindowFinish : UpdaterText.SetupWindowCancel;
        _cancel?.IsEnabled = true;
    }

    /// <summary>
    /// Moves only to the completed page once core and optional work have reached a reviewable stopping point.
    /// </summary>
    private void HandleChanged()
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (!_session.IsBusy && _session.CoreInstalled && (_page != 3 || _displayedOptionalIncomplete != _session.OptionalIncomplete))
            {
                _page = 3;
                Render();
            }
            else if (_page == 2 && (_displayedBusy != _session.IsBusy || !_session.IsBusy && _displayedReview != _session.Review))
            {
                Render();
            }
            else
            {
                Refresh();
            }
        });
    }

    /// <summary>
    /// Adds a native heading to the bounded installer page.
    /// </summary>
    /// <param name="text">The player-facing heading.</param>
    private void Heading(string text)
        => _body.Children.Add(new TextBlock { Text = text, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });

    /// <summary>
    /// Adds a compact wrapped explanation to the installer page.
    /// </summary>
    /// <param name="text">The player-facing paragraph.</param>
    private void Paragraph(string text)
        => _body.Children.Add(new TextBlock { Text = text, Foreground = Brush(MutedResource), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

    /// <summary>
    /// Resolves a tracker-inspired native theme brush without changing the tracker stylesheet.
    /// </summary>
    /// <param name="name">The fixed resource key.</param>
    /// <returns>The shared native brush.</returns>
    private Brush Brush(string name)
        => (Brush)FindResource(name);

    /// <summary>
    /// Groups related choices on the tracker's bordered gradient surface.
    /// </summary>
    /// <param name="children">The related native controls.</param>
    private void Card(params UIElement[] children)
    {
        var content = new StackPanel();
        foreach (var child in children)
            content.Children.Add(child);

        _body.Children.Add(new Border { Child = content, Background = Brush(SurfaceResource), BorderBrush = Brush(BorderResource), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10) });
    }

    /// <summary>
    /// Aligns review labels and values in a compact group while keeping selection states explicit in text.
    /// </summary>
    /// <param name="rows">The labels, displayed values and selected option emphasis.</param>
    private void ReviewGroup(params ReviewDetail[] rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(225) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Foreground = Brush(MutedResource), Margin = new Thickness(0, 3, 12, 3) };
            label.Inlines.Add(new System.Windows.Documents.Run(row.Label));
            if (row.Required)
                label.Inlines.Add(new System.Windows.Documents.Run(UpdaterText.SetupReviewRequiredBadge) { FontSize = 9, Foreground = Brush(WarningResource) });

            var foreground = row.Selected is { } selected ? Brush(selected ? SelectedResource : NotSelectedResource) : Foreground;
            var value = new TextBlock { Text = row.Value, Foreground = foreground, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 8, 3) };
            var size = new TextBlock { Text = row.Size, ToolTip = row.SizeHint, Foreground = row.Selected == false ? Brush(MutedResource) : Foreground, TextAlignment = TextAlignment.Right, FontSize = 12, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(label, index);
            Grid.SetRow(value, index);
            Grid.SetColumn(value, 1);
            Grid.SetRow(size, index);
            Grid.SetColumn(size, 2);
            grid.Children.Add(label);
            grid.Children.Add(value);
            grid.Children.Add(size);
        }

        _body.Children.Add(new Border { Child = grid, Background = Brush(SurfaceResource), BorderBrush = Brush(BorderResource), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 0, 8) });
    }

    /// <summary>
    /// Shows versions, independent option statuses, component sizes and the selected known download total.
    /// </summary>
    /// <param name="review">The authenticated installation review.</param>
    private void RenderReviewDetails(SetupReview review)
    {
        ReviewDetail[] versions =
        [
            new(UpdaterText.SetupReviewIronmon, review.Manifest.IronmonVersion, UpdaterText.SetupReviewDownload),
            new(UpdaterText.SetupReviewGame, review.Manifest.Game.VersionLabel, DownloadSize(review.IncludesGame ? review.GameDownloadBytes : 0, review.IncludesGame), SizeHint: UpdaterText.SetupReviewGameEstimateHint),
            new(UpdaterText.SetupReviewTrackerPackage, PackageLabel(review.Authorization.Request.Flavor), DownloadSize(review.PackageBytes))
        ];

        ReviewGroup(versions);
        var spriteEstimate = _session.SpriteDownloadEstimate;
        var spriteSize = spriteEstimate is null ? UpdaterText.SetupReviewVaries : DownloadSize(spriteEstimate.Bytes, true);
        var spriteHint = spriteEstimate is null ? UpdaterText.SetupReviewSpritesSizeHint : UpdaterText.SetupReviewSpritesMeasuredHint(spriteEstimate.MeasuredAt.ToLocalTime().ToString(DateFormat, CultureInfo.CurrentCulture));
        List<ReviewDetail> options =
        [
            new(UpdaterText.SetupReviewSpriteLibrary, Selection(_sprites), _sprites ? spriteSize : string.Empty, _sprites, SizeHint: spriteHint),
            new(UpdaterText.SetupReviewDesktopShortcut, Selection(_shortcut), string.Empty, _shortcut)
        ];

        if (_session.NeedsWebView && !_session.IsBusy)
            options.Add(new(UpdaterText.SetupReviewWebView, Selection(_webViewConsent), DownloadSize(_session.WebViewDownloadBytes), _webViewConsent, true));

        ReviewGroup([.. options]);
        var summary = SetupDownloadSummary.Create(review, _session.NeedsWebView && _webViewConsent, _session.WebViewDownloadBytes, _sprites, _session.SpriteDownloadEstimate?.Bytes);
        var totalHint = review.SupportDownloadBytes > 0 ? UpdaterText.SetupReviewTotalHint(DownloadSize(review.SupportDownloadBytes)) : UpdaterText.SetupReviewCachedTotalHint;
        if (summary.Incomplete)
            totalHint += Environment.NewLine + UpdaterText.SetupReviewIncompleteHint;

        var total = new Grid { Margin = new Thickness(0, 2, 0, 14), ToolTip = totalHint };
        var title = UpdaterText.SetupReviewEstimatedDownload;
        total.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        var totalSize = DownloadSize(summary.KnownBytes, summary.Estimated || summary.Incomplete);
        total.Children.Add(new TextBlock { Text = summary.Incomplete ? UpdaterText.SetupReviewPartialSize(totalSize) : totalSize, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
        _body.Children.Add(total);
    }

    /// <summary>
    /// Formats a known or estimated transfer size separately from option status.
    /// </summary>
    /// <param name="bytes">The published bytes or null when unavailable.</param>
    /// <param name="estimated">Whether the size represents a measured estimate.</param>
    /// <returns>The localized size or unavailable label.</returns>
    private static string DownloadSize(long? bytes, bool estimated = false)
    {
        if (bytes is null)
            return UpdaterText.SetupReviewSizeUnavailable;
        if (bytes == 0)
            return UpdaterText.SetupReviewNoDownload;

        var size = bytes >= 1073741824L ? UpdaterText.SetupReviewGiB((bytes.Value / 1073741824d).ToString(NumberFormat, CultureInfo.CurrentCulture)) : UpdaterText.SetupReviewMiB((bytes.Value / 1048576d).ToString(NumberFormat, CultureInfo.CurrentCulture));
        return estimated ? UpdaterText.SetupReviewEstimatedSize(size) : size;
    }

    /// <summary>
    /// Formats a selection without mixing requirements or download details into its status.
    /// </summary>
    /// <param name="selected">The explicit option state.</param>
    /// <returns>The localized selection status.</returns>
    private static string Selection(bool selected)
        => selected ? UpdaterText.SetupReviewSelected : UpdaterText.SetupReviewNotSelected;

    /// <summary>
    /// Describes one review row without combining its status, requirement and download size.
    /// </summary>
    /// <remarks>Constructs a presentation row for a version or selected installation component.</remarks>
    /// <param name="Label">The component label.</param>
    /// <param name="Value">The version, package type or selection status.</param>
    /// <param name="Size">The independently formatted download size.</param>
    /// <param name="Selected">The optional selected state, or null for plain version rows.</param>
    /// <param name="Required">Whether to display the separate requirement badge.</param>
    /// <param name="SizeHint">The explanation of an estimated or variable size.</param>
    private sealed record ReviewDetail(string Label, string Value, string Size, bool? Selected = null, bool Required = false, string? SizeHint = null);

    /// <summary>
    /// Creates a restrained section label matching the tracker hierarchy.
    /// </summary>
    /// <param name="text">The section name.</param>
    /// <returns>The native label.</returns>
    private TextBlock Label(string text)
        => new() { Text = text, FontSize = 10, Foreground = Brush(MutedResource), Margin = new Thickness(0, 0, 0, 10) };

    /// <summary>
    /// Creates wrapped secondary text beneath a choice or field.
    /// </summary>
    /// <param name="text">The short explanatory text.</param>
    /// <returns>The native description.</returns>
    private TextBlock Description(string text)
        => new() { Text = text, FontSize = 12, Foreground = Brush(MutedResource), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>
    /// Names a package by the prerequisite decision the player needs to make.
    /// </summary>
    /// <param name="flavor">The observed or selected package flavor.</param>
    /// <returns>The concise visible package name.</returns>
    private static string PackageLabel(string flavor)
        => flavor == ReleaseProtocol.SelfContained ? UpdaterText.SetupWindowRuntimeIncluded : UpdaterText.SetupWindowRuntimeRequired;

    /// <summary>
    /// Creates one of the two mutually exclusive package choices available to new installations.
    /// </summary>
    /// <param name="flavor">The release package identity.</param>
    /// <param name="description">The prerequisite explanation.</param>
    /// <returns>The themed native radio choice.</returns>
    private RadioButton PackageChoice(string flavor, string description)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = PackageLabel(flavor), FontWeight = FontWeights.SemiBold });
        content.Children.Add(Description(description));
        var choice = new RadioButton { Content = content, GroupName = PackageGroup, IsChecked = _flavor == flavor, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(choice, PackageLabel(flavor));
        choice.Checked += (_, _) => _flavor = flavor;
        return choice;
    }

    /// <summary>
    /// Builds a keyboard-accessible optional choice.
    /// </summary>
    /// <param name="text">The visible label.</param>
    /// <param name="selected">The stored selection.</param>
    /// <param name="changed">The explicit choice callback.</param>
    /// <param name="description">An optional short explanation grouped with its checkbox.</param>
    /// <returns>The native checkbox.</returns>
    private CheckBox Option(string text, bool selected, Action<bool> changed, string? description = null)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        if (description is not null)
            content.Children.Add(Description(description));

        var checkbox = new CheckBox { Content = content, IsChecked = selected, Margin = new Thickness(0, 2, 0, 6) };
        AutomationProperties.SetName(checkbox, text);
        checkbox.Click += (_, _) => changed(checkbox.IsChecked == true);
        return checkbox;
    }

    /// <summary>
    /// Builds a native button whose action does not require a background operation.
    /// </summary>
    /// <param name="text">The visible button label.</param>
    /// <param name="action">The explicit action.</param>
    /// <returns>The configured button.</returns>
    private static Button Action(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0), MinWidth = 75 };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>
    /// Builds a native button connected to a session-contained asynchronous workflow.
    /// </summary>
    /// <param name="text">The visible button label.</param>
    /// <param name="action">The serialized session action.</param>
    /// <param name="primary">Whether this is the primary keyboard-default action.</param>
    /// <returns>The configured button.</returns>
    private static Button AsyncAction(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = text, IsDefault = primary, Margin = new Thickness(8, 0, 0, 0), MinWidth = 75 };
        button.Click += async (_, _) => await action();
        return button;
    }

    /// <summary>
    /// Interprets Escape as cancellation during work, otherwise as returning to the previous Setup step.
    /// </summary>
    /// <param name="sender">The Setup window.</param>
    /// <param name="args">The keyboard event.</param>
    private void HandleKey(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape)
            return;

        args.Handled = true;
        if (_session.IsBusy)
        {
            _session.Cancel();
        }
        else if (_page == 2)
        {
            BackFromReview();
        }
        else if (_page == 1)
        {
            Navigate(0);
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// Waits for safe rollback or optional cancellation before the disposable window may close.
    /// </summary>
    /// <param name="sender">The Setup window.</param>
    /// <param name="args">The cancellable window close.</param>
    private void HandleClosing(object? sender, CancelEventArgs args)
    {
        if (!_session.IsBusy)
            return;

        args.Cancel = true;
        _session.Cancel();
    }

    /// <summary>
    /// Removes the progress subscription when this independent window closes.
    /// </summary>
    /// <param name="sender">The Setup window.</param>
    /// <param name="args">The window close notification.</param>
    private void HandleClosed(object? sender, EventArgs args)
    {
        _progressTimer.Stop();
        _session.Changed -= HandleChanged;
    }
}
