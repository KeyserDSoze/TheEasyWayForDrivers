#Requires -Version 7.0
$ErrorActionPreference = 'Stop'

# Static WPF UI contract: catches structural regressions without a GUI runner.
# This does not replace visual QA at 100/125/150% Windows DPI.
$repositoryRoot = Split-Path -Path $PSScriptRoot -Parent
$xamlPath = Join-Path $repositoryRoot 'src/App2.Desktop/MainWindow.xaml'
[xml]$document = Get-Content -LiteralPath $xamlPath -Raw
$namespaces = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
$namespaces.AddNamespace('w', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$namespaces.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')

function Require-Node([string]$xpath, [string]$message) {
    $node = $document.SelectSingleNode($xpath, $namespaces)
    if ($null -eq $node) {
        throw "Guided results UI contract failed: $message"
    }
    return $node
}

$resultsPanel = Require-Node "//w:Grid[@x:Name='HomeResultsPanel']" 'guided results panel missing'
$rows = $resultsPanel.SelectNodes('./w:Grid.RowDefinitions/w:RowDefinition', $namespaces)
if ($rows.Count -ne 5 -or $rows[2].GetAttribute('Height') -ne '*' -or
    $rows[3].GetAttribute('Height') -ne 'Auto') {
    throw 'Guided results: update list must occupy the flexible space, OEM handoffs must not take half the screen.'
}

$filters = Require-Node "//w:ComboBox[@x:Name='ResultFilterComboBox']" 'filter missing'
$firstFilter = $filters.SelectSingleNode('./w:ComboBoxItem[1]', $namespaces)
if ($null -eq $firstFilter -or $firstFilter.GetAttribute('Tag') -ne 'all') {
    throw 'Guided results: default filter must show all WUA candidates.'
}

$updates = Require-Node "//w:ListView[@x:Name='CompactUpdatesList']" 'WUA candidate list missing'
if ($updates.GetAttribute('ItemsSource') -ne '{Binding CompactUpdatesView}') {
    throw 'Guided results: WUA list must bind to CompactUpdatesView.'
}
# #468: WPF UI Automation must identify the actual candidate in each
# recycled virtualized row. Static "Seleziona aggiornamento" or generic
# "Aggiorna"/"Dettagli" labels make dozens of rows indistinguishable to
# a screen reader and may hide that firmware/OEM rows are review-only.
$updateCheckbox = Require-Node "//w:ListView[@x:Name='CompactUpdatesList']//w:CheckBox[@IsChecked='{Binding IsSelected, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}']" 'WUA row checkbox missing'
if ($updateCheckbox.GetAttribute('IsEnabled') -ne '{Binding CanQuickInstall}' -or
    $updateCheckbox.GetAttribute('AutomationProperties.Name') -ne '{Binding SelectionAccessibleName}') {
    throw 'WUA result checkbox must remain quick-install gated and have a unique bound UIA name.'
}
$updateAction = Require-Node "//w:ListView[@x:Name='CompactUpdatesList']//w:Button[@Content='{Binding QuickInstallLabel}']" 'WUA row action missing'
if ($updateAction.GetAttribute('Click') -ne 'QuickUpdateButton_Click' -or
    $updateAction.GetAttribute('AutomationProperties.Name') -ne '{Binding ActionAccessibleName}') {
    throw 'WUA result action must open the guarded handler and have a per-row UIA name.'
}
$desktopModelPath = Join-Path $repositoryRoot 'src/App2.Desktop/Models/SelectableDriverUpdate.cs'
$desktopModel = Get-Content -LiteralPath $desktopModelPath -Raw
if ($desktopModel -notmatch 'CompactUpdatePresentation.KindText\(Update, CanQuickInstall\)' -or
    $desktopModel -notmatch 'CompactUpdatePresentation.SelectionAccessibleName\(Update, CanQuickInstall\)' -or
    $desktopModel -notmatch 'CompactUpdatePresentation.ActionAccessibleName\(Update, CanQuickInstall\)') {
    throw 'WUA result accessible names and badges must use shared quick-install presentation policy.'
}


$verified = Require-Node "//w:ListView[@x:Name='VendorChecksList']" 'verified OEM list missing'
if ($verified.GetAttribute('ItemsSource') -ne '{Binding VendorApplicableView}') {
    throw 'Guided results: ONLY manufacturer-assessed OEM updates should be shown by default.'
}

$expander = Require-Node "//w:Expander[@x:Name='OtherVendorSourcesExpander']" 'unverified sources expander missing'
if ($expander.GetAttribute('IsExpanded') -ne 'False') {
    throw 'Guided results: unverified device/vendor handoffs must be collapsed by default.'
}

$handoffs = Require-Node "//w:ListView[@x:Name='VendorHandoffsList']" 'vendor handoff list missing'
if ($handoffs.GetAttribute('ItemsSource') -ne '{Binding VendorHandoffsView}' -or
    $null -eq $handoffs.SelectSingleNode('ancestor::w:Expander[@x:Name="OtherVendorSourcesExpander"]', $namespaces)) {
    throw 'Guided results: unverified handoffs must stay inside the collapsed expander.'
}

# Advanced WUA candidates with exact PnP matches must be visible on the
# device tab, but must NEVER enable the per-device installation action.
$reviewOnlyPanel = Require-Node "//w:Border[@x:Name='ReviewOnlyWuaDevicePanel']" 'review-only WUA device panel missing'
if ($null -eq $reviewOnlyPanel.SelectSingleNode('ancestor::w:TabItem[@x:Name="DriversTab"]', $namespaces) -or
    $null -eq $reviewOnlyPanel.SelectSingleNode('.//w:DataTrigger[@Value="True"]', $namespaces)) {
    throw 'Advanced WUA evidence must be optional in the device tab, not a primary install action.'
}
$reviewOnlySummary = Require-Node "//w:Run[contains(@Text,'ReviewOnlyCandidateTitle')]" 'read-only candidate title binding missing'
$advancedReview = Require-Node "//w:Button[@x:Name='DeviceReviewAdvancedButton']" 'open advanced WUA review action missing'
if ($advancedReview.GetAttribute('Click') -ne 'ShowAdvancedUpdatesButton_Click' -or
    $null -eq $advancedReview.SelectSingleNode('ancestor::w:Border[@x:Name="ReviewOnlyWuaDevicePanel"]', $namespaces)) {
    throw 'Review-only device action must open the advanced update list, not call an installer.'
}
$deviceInstall = Require-Node "//w:Button[@x:Name='InstallDeviceUpdateButton']" 'per-device install button missing'
if ($deviceInstall.GetAttribute('IsEnabled') -ne 'False') {
    throw 'Per-device installation must stay disabled until a verified ordinary WUA candidate exists.'
}
$null = Require-Node "//w:Button[@x:Name='CompactUpdateAllButton']" 'Update All action missing'
$null = Require-Node "//w:Button[@x:Name='CopyCoverageReportButton']" 'copyable QA report missing'
$pcSpecialist = Require-Node "//w:Button[@x:Name='PcSpecialistPortalButton']" 'PCSpecialist driver portal missing'
if ($pcSpecialist.GetAttribute('Visibility') -ne 'Collapsed') {
    throw 'Guided results: PCSpecialist portal must be hidden unless the actual OEM matches.'
}
$rankSection = Require-Node "//w:Expander[@x:Name='WindowsDriverStoreExpander']" 'Windows local Driver Store evidence section missing'
if ($rankSection.GetAttribute('Visibility') -ne 'Collapsed' -or
    $rankSection.GetAttribute('IsExpanded') -ne 'False') {
    throw 'PnPUtil evidence must stay hidden until an inventory exists and collapsed by default.'
}
$rankDevice = Require-Node "//w:ComboBox[@x:Name='WindowsStoreDeviceCombo']" 'device picker for PnPUtil is missing'
if ($rankDevice.GetAttribute('ItemsSource') -ne '{Binding Drivers}') {
    throw 'PnPUtil device picker must bind only to live Windows driver inventory.'
}
$rankButton = Require-Node "//w:Button[@x:Name='WindowsStoreReadButton']" 'read-only PnPUtil action missing'
if ($rankButton.GetAttribute('Click') -ne 'WindowsStoreReadButton_Click' -or
    $null -eq $rankButton.SelectSingleNode('ancestor::w:Expander[@x:Name="WindowsDriverStoreExpander"]', $namespaces)) {
    throw 'PnPUtil command must be a direct opt-in from the local Driver Store section.'
}
$rankOutput = Require-Node "//w:TextBox[@x:Name='WindowsStoreEvidenceText']" 'local Windows rank view missing'
if ($rankOutput.GetAttribute('IsReadOnly') -ne 'True' -or
    $rankOutput.GetAttribute('Visibility') -ne 'Collapsed') {
    throw 'PnPUtil evidence must not be editable or shown before a manual request.'
}
# Official Microsoft Update Catalog browser handoff: may search publication
# metadata after explicit consent, NEVER create an installable WUA candidate.
$homeCatalog = Require-Node "//w:Button[@x:Name='HomeCatalogSearchButton']" 'official Catalog handoff missing in Home'
if ($homeCatalog.GetAttribute('Click') -ne 'HomeCatalogSearchButton_Click' -or
    $null -eq $homeCatalog.SelectSingleNode('ancestor::w:Expander[@x:Name="WindowsDriverStoreExpander"]', $namespaces)) {
    throw 'Microsoft Catalog handoff must remain opt-in inside the collapsed per-device diagnostic section.'
}
$deviceCatalog = Require-Node "//w:Button[@x:Name='DeviceCatalogSearchButton']" 'official Catalog handoff missing on device tab'
if ($deviceCatalog.GetAttribute('Click') -ne 'DeviceCatalogSearchButton_Click' -or
    $null -eq $deviceCatalog.SelectSingleNode('ancestor::w:TabItem[@x:Name="DriversTab"]', $namespaces)) {
    throw 'Device Catalog search must belong to the selected Windows device, not Update All.'
}
if ($null -ne $homeCatalog.SelectSingleNode('ancestor::w:ListView[@x:Name="CompactUpdatesList"]', $namespaces) -or
    $null -ne $deviceCatalog.SelectSingleNode('ancestor::w:TabItem[@x:Name="UpdatesTab"]', $namespaces)) {
    throw 'Manual catalog browsing must never be presented as an installable update action.'
}
$partialDriverReview = Require-Node "//w:Expander[@x:Name='PartialWuaHintsExpander']" 'read-only incomplete WUA hints section missing'
if ($partialDriverReview.GetAttribute('Visibility') -ne 'Collapsed' -or
    $partialDriverReview.GetAttribute('IsExpanded') -ne 'False' -or
    $null -eq $partialDriverReview.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorToolsExpander"]', $namespaces)) {
    throw 'Incomplete WUA evidence must remain hidden and collapsed inside the advanced OEM diagnostic area until partial results exist.'
}
$partialDriverRows = Require-Node "//w:ListView[@x:Name='PartialWuaHintsList']" 'unverified partial WUA titles list missing'
if ($partialDriverRows.GetAttribute('ItemsSource') -ne '{Binding PartialWuaDriverHints}' -or
    $null -eq $partialDriverRows.SelectSingleNode('ancestor::w:Expander[@x:Name="PartialWuaHintsExpander"]', $namespaces)) {
    throw 'Partial driver hints must bind only to their informational collection, not the installable WUA list.'
}
if ($partialDriverRows.GetAttribute('ItemsSource') -eq $updates.GetAttribute('ItemsSource')) {
    throw 'Partial WUA hints must NEVER populate CompactUpdatesList or Update All.'
}
$softwareReview = Require-Node "//w:Expander[@x:Name='RelatedSoftwareExpander']" 'vendor Software review missing'
if ($softwareReview.GetAttribute('Visibility') -ne 'Collapsed' -or
    $softwareReview.GetAttribute('IsExpanded') -ne 'False') {
    throw 'WUA Type=Software results must be hidden until searched and collapsed by default.'
}
$softwareRows = Require-Node "//w:ListView[@x:Name='RelatedSoftwareList']" 'WUA vendor software list missing'
if ($softwareRows.GetAttribute('ItemsSource') -ne '{Binding RelatedSoftwareHints}' -or
    $null -eq $softwareRows.SelectSingleNode('ancestor::w:Expander[@x:Name="RelatedSoftwareExpander"]', $namespaces)) {
    throw 'WUA software evidence must have separate informational binding, not WUA Driver updates.'
}
if ($updates.GetAttribute('ItemsSource') -eq $softwareRows.GetAttribute('ItemsSource')) {
    throw 'Companion Type=Software items must never be attached to the installable Driver list.'
}
$localZip = Require-Node "//w:Button[@x:Name='CompareLocalOemZipButton']" 'local OEM archive comparison action missing'
if ($localZip.GetAttribute('Visibility') -ne 'Collapsed') {
    throw 'Local OEM comparison must stay disabled until inventory exists.'
}
$localResult = Require-Node "//w:Expander[@x:Name='LocalOemComparisonExpander']" 'local comparison findings section missing'
$localRows = Require-Node "//w:ListView[@x:Name='LocalOemFindingsList']" 'local OEM findings view missing'
if ($localRows.GetAttribute('ItemsSource') -ne '{Binding LocalOemFindings}' -or
    $null -eq $localRows.SelectSingleNode('ancestor::w:Expander[@x:Name="LocalOemComparisonExpander"]', $namespaces)) {
    throw 'ZIP findings must be bound only to the non-installable local OEM section.'
}
if ($localZip.GetAttribute('Click') -ne 'CompareLocalOemZipButton_Click') {
    throw 'Local OEM comparison button must invoke the explicit read-only flow.'
}
if ($localZip.GetAttribute('Content') -notmatch 'ZIP / CAB') {
    throw 'Local OEM comparison must make CAB support visible to the user.'
}
$localFolder = Require-Node "//w:Button[@x:Name='CompareLocalOemFolderButton']" 'OEM folder compare action missing'
if ($localFolder.GetAttribute('Visibility') -ne 'Collapsed' -or
    $localFolder.GetAttribute('Click') -ne 'CompareLocalOemFolderButton_Click') {
    throw 'OEM folder comparison must be gated behind a guided scan and explicit consent.'
}
if ($null -eq $localFolder.SelectSingleNode('ancestor::w:Border', $namespaces)) {
    throw 'OEM folder comparison must belong to the non-installable source section.'
}
$verifyOemCat = Require-Node "//w:Button[@x:Name='VerifyOemFolderCatalogButton']" 'read-only OEM CAT/INF review button missing'
if ($verifyOemCat.GetAttribute('Visibility') -ne 'Collapsed' -or
    $verifyOemCat.GetAttribute('Click') -ne 'VerifyOemFolderCatalogButton_Click') {
    throw 'OEM CAT/INF audit must be disabled by default and opt-in only.'
}
if ($verifyOemCat.GetAttribute('Content') -notmatch 'firme e file driver') {
    throw 'OEM signature action must clearly announce referenced payload checks.'
}
if ($null -eq $verifyOemCat.SelectSingleNode('ancestor::w:Expander[@x:Name="LocalOemComparisonExpander"]', $namespaces)) {
    throw 'OEM trust review must appear in the non-installable local OEM section.'
}
$null = Require-Node "//w:TextBlock[@x:Name='LocalOemCatalogAuditText']" 'audit status text missing'
if ($localResult.GetAttribute('Header') -notmatch 'non installabile') {
    throw 'Local OEM CAB/ZIP findings must be labelled non-installable.'
}
# Visual refresh #140: buttons, scan state and source hierarchy must
# remain usable on compact windows without weakening the install policy.
$primaryButton = Require-Node "//w:Style[@x:Key='PrimaryButton']" 'primary button visual style missing'
$chrome = $primaryButton.SelectSingleNode('./w:Setter[@Property="Template"]/w:Setter.Value/w:ControlTemplate/w:Border[@x:Name="Chrome"]', $namespaces)
if ($null -eq $chrome -or $chrome.GetAttribute('CornerRadius') -ne '10') {
    throw 'Visual refresh: primary/secondary buttons must share rounded control template.'
}
$focus = $primaryButton.SelectSingleNode('.//w:Trigger[@Property="IsKeyboardFocused"]', $namespaces)
$disabled = $primaryButton.SelectSingleNode('.//w:Trigger[@Property="IsEnabled" and @Value="False"]', $namespaces)
$secondary = Require-Node "//w:Style[@x:Key='SecondaryButton']" 'secondary button style missing'
if ($null -eq $focus -or $null -eq $disabled -or
    $secondary.GetAttribute('BasedOn') -ne '{StaticResource PrimaryButton}') {
    throw 'Visual refresh: button keyboard focus, disabled state and shared styling are required.'
}

$scan = Require-Node "//w:Button[@x:Name='ScanAllButton']" 'primary scan action missing'
if ($scan.GetAttribute('Click') -ne 'ScanAllButton_Click') {
    throw 'Visual refresh: prominent scan action must keep the guided read-only handler.'
}
$progress = Require-Node "//w:ProgressBar[@x:Name='HomeScanProgressBar']" 'guided scan progress missing'
if ($progress.GetAttribute('Visibility') -ne 'Collapsed' -or
    $progress.GetAttribute('IsIndeterminate') -ne 'True' -or
    $null -eq $progress.SelectSingleNode('ancestor::w:ScrollViewer[@x:Name="HomePanel"]', $namespaces)) {
    throw 'Visual refresh: scanning progress must be hidden initially and remain inside Home.'
}

$emptyPanel = Require-Node "//w:StackPanel[@x:Name='CompactEmptyStatePanel']" 'result empty panel missing'
$emptyCopy = Require-Node "//w:TextBlock[@x:Name='CompactEmptyStateText']" 'result empty message missing'
if ($emptyPanel.GetAttribute('Visibility') -ne 'Collapsed' -or
    $null -eq $emptyCopy.SelectSingleNode('ancestor::w:StackPanel[@x:Name="CompactEmptyStatePanel"]', $namespaces)) {
    throw 'Visual refresh: empty explanation must show only for zero visible candidates.'
}
if ($null -eq $updates.SelectSingleNode('.//w:TextBlock[@Text="{Binding KindText}"]', $namespaces)) {
    throw 'Visual refresh: each WUA row must expose its kind separately from OEM results.'
}
$toolsSection = Require-Node "//w:Expander[@x:Name='VendorToolsExpander']" 'OEM diagnostic tools grouping missing'
if ($toolsSection.GetAttribute('IsExpanded') -ne 'False' -or
    $null -eq $localFolder.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorToolsExpander"]', $namespaces) -or
    $null -eq $softwareRows.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorToolsExpander"]', $namespaces) -or
    $null -ne $verified.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorToolsExpander"]', $namespaces)) {
    throw 'Visual refresh: diagnostic OEM tools must be opt-in while verified recommendations remain visible.'
}

$windowCode = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/App2.Desktop/MainWindow.xaml.cs') -Raw
# #472: repeating ONLY a WUA search must not wipe explicitly
# queried Dell DCU / HP HPIA / Lenovo recommendations, Dell/HP catalog
# metadata or NVIDIA release announcements. Manual handoff matches ARE
# regenerated; a whole new scan still clears the previous OEM inventory.
$oemRetentionCore = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/VendorSourceResultRetention.cs') -Raw
$refreshVendorStart = $windowCode.IndexOf('private void RefreshVendorChecks()')
$refreshVendorEnd = $windowCode.IndexOf('private async void LenovoApplicableButton_Click', $refreshVendorStart)
$runFullStart = $windowCode.IndexOf('private async Task RunFullCheckAsync')
$runFullEnd = $windowCode.IndexOf('private async void RetryOemCatalogButton_Click', $runFullStart)
$wuaSearchStart = $windowCode.IndexOf('private async Task SearchUpdatesCoreAsync(')
$wuaSearchEnd = $windowCode.IndexOf('private async void InstallSelectedButton_Click', $wuaSearchStart)
if ($refreshVendorStart -lt 0 -or $refreshVendorEnd -le $refreshVendorStart -or
    $runFullStart -lt 0 -or $runFullEnd -le $runFullStart -or
    $wuaSearchStart -lt 0 -or $wuaSearchEnd -le $wuaSearchStart) {
    throw 'Cannot locate WUA refresh vs full-scan OEM lifetime boundary.'
}
$refreshVendorBody = $windowCode.Substring($refreshVendorStart, $refreshVendorEnd - $refreshVendorStart)
$fullScanBody = $windowCode.Substring($runFullStart, $runFullEnd - $runFullStart)
$wuaSearchBody = $windowCode.Substring($wuaSearchStart, $wuaSearchEnd - $wuaSearchStart)
if (-not $refreshVendorBody.Contains('VendorSourceResultRetention.KeepEvidence(VendorChecks)') -or
    -not $refreshVendorBody.Contains('VendorManualHandoffSummary.Create(preview.Rows)') -or
    -not $refreshVendorBody.Contains('foreach (var row in existingOemEvidence)') -or
    -not $refreshVendorBody.Contains('VendorChecks.Add(row);') -or
    -not $wuaSearchBody.Contains('RefreshVendorChecks();') -or
    -not $fullScanBody.Contains('VendorChecks.Clear();') -or
    -not $oemRetentionCore.Contains('VendorSourceDiscovery.IsTrustedOfficialUrl(row.OfficialUrl)') -or
    -not $oemRetentionCore.Contains('row.IsVendorApplicable') -or
    -not $oemRetentionCore.Contains('row.IsCatalogMetadata') -or
    -not $oemRetentionCore.Contains('row.IsReleaseAnnouncement')) {
    throw 'WUA-only refresh must retain trusted OEM evidence; full PC scan must reset it.'
}

# #474: the next WUA-only search must replace ONLY the old Windows
# warnings, then recompute the complete Home source/installed-driver
# coverage using the current report. Preserve independent OEM findings.
$wuaUnavailableCore = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/GuidedWuaUnavailableSteps.cs') -Raw
$homeEvidenceStart = $windowCode.IndexOf('private void RefreshGuidedWuaEvidence(')
$homeEvidenceEnd = $windowCode.IndexOf('private void RefreshHomeVendorCoverage()', $homeEvidenceStart)
if ($homeEvidenceStart -lt 0 -or $homeEvidenceEnd -le $homeEvidenceStart) {
    throw 'WUA Home source evidence recomputation method missing.'
}
$homeEvidenceBody = $windowCode.Substring($homeEvidenceStart, $homeEvidenceEnd - $homeEvidenceStart)
if (-not $wuaUnavailableCore.Contains('WuaSourceFailurePolicy.UnavailableSources(report)') -or
    -not $wuaUnavailableCore.Contains('WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report)') -or
    -not $wuaUnavailableCore.Contains('step.StartsWith(SourcePrefix') -or
    -not $wuaSearchBody.Contains('GuidedWuaUnavailableSteps.WithLatestReport(') -or
    -not $wuaSearchBody.Contains('RefreshHomeVendorCoverage();') -or
    -not $wuaSearchBody.Contains('RefreshGuidedWuaEvidence(_lastScanOemProvidersComplete);') -or
    -not $wuaSearchBody.Contains('HomeScanStateText.Text = incomplete') -or
    -not $wuaSearchBody.Contains('HomeUpdatesButton.IsEnabled =') -or
    -not $fullScanBody.Contains('RefreshGuidedWuaEvidence(oemComplete);') -or
    -not $windowCode.Contains('_lastScanOemProvidersComplete = oemComplete;') -or
    -not $homeEvidenceBody.Contains('HomeCoverageText.Text = FormatSourceCoverage(') -or
    -not $homeEvidenceBody.Contains('HomeInstalledDriverEvidenceText.Text =') -or
    -not $homeEvidenceBody.Contains('WindowsDriverCandidateMerge.ConfirmedForReport(_lastSearchReport)')) {
    throw 'WUA-only refresh must recompute Home source/PnP coverage and replace only old WUA failures.'
}

# #475: WMI normalization itself must reject (not trim/repair)
# private or malformed manufacturer and model values before detection.
$identityNormalizer = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/SystemIdentityNormalizer.cs') -Raw
if (-not $identityNormalizer.Contains('value.Any(ch => char.IsControl(ch)') -or
    -not $identityNormalizer.Contains('value.Contains("PRIVATE_"') -or
    -not $identityNormalizer.Contains('value.Contains("://", StringComparison.Ordinal)') -or
    -not $identityNormalizer.Contains("value.Trim(' ')") -or
    $identityNormalizer.Contains('var cleaned = value.Trim();')) {
    throw 'WMI OEM identity must fail closed before trimming controls and private references.'
}

# #482: Type=Software review titles require real vendor/component
# boundaries and cannot echo local paths, PnP tokens or invisible controls.
$softwareClassifier482 = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Update/WuaDriverRelatedSoftwareClassifier.cs') -Raw
if (-not $softwareClassifier482.Contains('HasWordBoundary(title, v)') -or
    -not $softwareClassifier482.Contains('HasWordBoundary(title, c)') -or
    -not $softwareClassifier482.Contains('HasUnsafeTitleMetadata(title)') -or
    -not $softwareClassifier482.Contains('char.GetUnicodeCategory(ch)') -or
    -not $softwareClassifier482.Contains('title.Contains("PRIVATE_"') -or
    $softwareClassifier482.Contains('title.Contains(v, StringComparison.OrdinalIgnoreCase)') -or
    $softwareClassifier482.Contains('DriverContexts.Any(c => title.Contains(c,')) {
    throw 'Type=Software WUA hint classification must use whole vendor/component tokens and private-title rejection.'
}

# #486: WUA Type=Driver PARTIAL title hints must use the SAME
# Unicode vendor boundary as Type=Software and the eight-category QA report.
$partialVendor486 = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Update/WuaPartialDriverHintCollector.cs') -Raw
$partialGroupStart486 = $partialVendor486.IndexOf('private static string VendorGroup(')
if ($partialGroupStart486 -lt 0 -or
    -not $partialVendor486.Substring($partialGroupStart486).Contains(
        'WuaDriverRelatedSoftwareClassifier.HasWordBoundary(title, vendor)') -or
    -not $partialVendor486.Substring($partialGroupStart486).Contains('has("NVIDIA")') -or
    -not $partialVendor486.Substring($partialGroupStart486).Contains('has("Killer")') -or
    -not $partialVendor486.Substring($partialGroupStart486).Contains('has("AMD")') -or
    $partialVendor486.Substring($partialGroupStart486).Contains('title.Contains(')) {
    throw 'Partial WUA Driver hint fairness must not classify fake vendor substrings.'
}

# #485: SetupAPI ranking native descriptions/providers also require
# full-source privacy validation BEFORE truncation to the 72-character UI cap.
$setupRank485 = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/WindowsSetupApiRankEvidence.cs') -Raw
if (-not $setupRank485.Contains('HasPrivateNativeText(source)') -or
    -not $setupRank485.Contains('private static bool HasPrivateNativeText(') -or
    -not $setupRank485.Contains('PrivateNativeMarkers.Any(marker =>') -or
    -not $setupRank485.Contains('"PRIVATE_"') -or
    -not $setupRank485.Contains('"SECRET_"') -or
    -not $setupRank485.Contains('"ORDER_"') -or
    -not $setupRank485.Contains('"SERIALNUMBER"') -or
    -not $setupRank485.Contains('UnicodeCategory.Format') -or
    -not $setupRank485.Contains('char.IsSurrogate(ch)') -or
    -not $setupRank485.Contains('SafeLabel(item.Provider)') -or
    -not $setupRank485.Contains('SafeLabel(item.Description)')) {
    throw 'SetupAPI local rank labels must inspect complete native strings for private IDs and invisible Unicode.'
}

# #484: PnPUtil rank text must redact whole sensitive lines and
# inspect the raw Unicode input BEFORE substituting invisible code points.
$driverStore484 = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/WindowsDriverStoreEvidence.cs') -Raw
if (-not $driverStore484.Contains('HasSensitiveTextMarker(inspection)') -or
    -not $driverStore484.Contains('PrivateLineMarkers.Any(marker =>') -or
    -not $driverStore484.Contains("text.Contains('@')") -or
    -not $driverStore484.Contains('text.Contains("/Users/"') -or
    -not $driverStore484.Contains('text.Contains("/home/"') -or
    -not $driverStore484.Contains('text.Contains("/mnt/"') -or
    -not $driverStore484.Contains('char.GetUnicodeCategory(ch)') -or
    -not $driverStore484.Contains("ch == '\uFFFD'") -or
    -not $driverStore484.Contains('IsUnsafeUnicode(ch)') -or
    -not $driverStore484.Contains('RedactSensitiveLines(scrubbed.ToString(), output)') -or
    -not $driverStore484.Contains('VisibleInfFileName.IsMatch(sanitized)')) {
    throw 'Local Driver Store diagnostic must redact sensitive native lines before permitting INF evidence.'
}

# #483: the same bounded Unicode vendor matcher must feed BOTH
# the fair 32-slot Type=Software grouping and the eight-component QA matrix.
$softwareMatrix483 = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/ReferenceDriverCoverage.cs') -Raw
$vendorGroupStart483 = $softwareClassifier482.IndexOf('private static string VendorGroup(')
$vendorGroupEnd483 = $softwareClassifier482.IndexOf('/// <summary>Preserve one WUA identity', $vendorGroupStart483)
if ($vendorGroupStart483 -lt 0 -or $vendorGroupEnd483 -le $vendorGroupStart483 -or
    -not $softwareClassifier482.Contains('internal static bool HasWordBoundary(') -or
    -not $softwareClassifier482.Contains('UnicodeCategory.NonSpacingMark') -or
    -not $softwareClassifier482.Substring($vendorGroupStart483,
        $vendorGroupEnd483 - $vendorGroupStart483).Contains('HasWordBoundary(title, "NVIDIA")') -or
    -not $softwareClassifier482.Substring($vendorGroupStart483,
        $vendorGroupEnd483 - $vendorGroupStart483).Contains('HasWordBoundary(title, "Killer")') -or
    $softwareClassifier482.Substring($vendorGroupStart483,
        $vendorGroupEnd483 - $vendorGroupStart483).Contains('title.Contains(') -or
    -not $softwareMatrix483.Contains(
        'WuaDriverRelatedSoftwareClassifier.HasWordBoundary(title, word)')) {
    throw 'WUA Type=Software fairness and eight-category report must use one strict Unicode vendor boundary.'
}

# #481: Device rows and tray must use the full confirmed WUA report
# as source of GUID vetoes, not the already merged ordinary UI candidate.
$deviceGate481 = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/DeviceWuaPresentationGate.cs') -Raw
$deviceRow481 = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/App2.Desktop/Models/DriverDeviceRow.cs') -Raw
$applyMatches481 = $windowCode.IndexOf('private void ApplyUpdateMatches()')
$selectionChanged481 = $windowCode.IndexOf('private void DriversGrid_SelectionChanged(', $applyMatches481)
$trayCall481 = $windowCode.IndexOf('NotifyDriverUpdatesIfChanged(', $selectionChanged481)
if ($applyMatches481 -lt 0 -or $selectionChanged481 -le $applyMatches481 -or
    $trayCall481 -lt 0 -or
    -not $deviceGate481.Contains('IReadOnlySet<Guid>? approvedSourceGuids = null') -or
    -not $deviceGate481.Contains('AllowedByFullReport(update)') -or
    -not $deviceGate481.Contains('!AllowedByFullReport(update)') -or
    -not $deviceRow481.Contains('approvedSourceGuids);') -or
    -not $windowCode.Substring($applyMatches481, $selectionChanged481 - $applyMatches481).Contains(
        'WindowsDriverCandidateMerge.ConfirmedForReport(_lastSearchReport)') -or
    -not $windowCode.Substring($applyMatches481, $selectionChanged481 - $applyMatches481).Contains(
        'approvedSourceGuids);') -or
    -not $windowCode.Contains('NotifyDriverUpdatesIfChanged(' + [Environment]::NewLine +
        '            WindowsDriverCandidateMerge.ConfirmedForReport(searchReport),')) {
    throw 'Device status and tray must retain full confirmed WUA source vetoes (#481).'
}

# #480: direct .NET / IPC exception messages may contain Windows user
# profiles, PnP identifiers, local paths or managed network host names.
# The main Desktop must render only explicit non-identifying labels.
$failureText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/DesktopFailureText.cs') -Raw
if ($windowCode.Contains('exception.Message') -or
    -not $windowCode.Contains('DesktopFailureText.ConnectionInterrupted') -or
    -not $windowCode.Contains('DesktopFailureText.BusyOperation(exception)') -or
    -not $windowCode.Contains('DesktopFailureText.SettingsSaveFailed') -or
    -not $windowCode.Contains('DesktopFailureText.SettingsResetFailed') -or
    -not $windowCode.Contains('DesktopFailureText.HistorySaveFailed') -or
    -not $windowCode.Contains('DesktopFailureText.HistoryReadFailed') -or
    -not $windowCode.Contains('DesktopFailureText.HistoryExportFailed') -or
    $failureText.Contains('exception.Message') -or
    $failureText.Contains('exception.ToString()')) {
    throw 'Desktop failures must never interpolate raw .NET/service exception details into UI.'
}

# #479: Home single, selected and all WUA actions must use the
# SAME full confirmed source scope, including trusted BrowseOnly/Firmware
# vetoes from another representation of the same canonical GUID.
$compactSelection = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Update/CompactInstallActionPolicy.cs') -Raw
$compactRow = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/App2.Desktop/Models/SelectableDriverUpdate.cs') -Raw
if (-not $compactSelection.Contains('QuickUpdatePolicy.AllOrdinaryIds(') -or
    -not $compactSelection.Contains('Guid.Parse(') -or
    -not $compactSelection.Contains('!seen.Add(id)') -or
    -not $windowCode.Contains('private IReadOnlyList<string> CurrentQuickInstallIds(') -or
    -not $windowCode.Contains('WindowsDriverCandidateMerge.ConfirmedForReport(_lastSearchReport)') -or
    -not $windowCode.Contains('CompactInstallActionPolicy.SelectIds(') -or
    -not $windowCode.Contains('CompactInstallActionPolicy.TryAuthorizeRequested(') -or
    -not $windowCode.Contains('result = await _serviceClient.InstallUpdatesAsync(') -or
    -not $windowCode.Contains('canonicalIds,') -or
    -not $windowCode.Contains('item.ApplyQuickInstallAuthorization(') -or
    -not $compactRow.Contains('OnPropertyChanged(nameof(SelectionAccessibleName))') -or
    -not $compactRow.Contains('OnPropertyChanged(nameof(ActionAccessibleName))') -or
    -not $compactRow.Contains('OnPropertyChanged(nameof(KindText))')) {
    throw 'Home WUA actions must share canonical full-scan GUID veto, UI labeling and IPC authorization.'
}

# #478: advanced Dell and Lenovo catalog queries must reject untrusted
# IPC provider identities, invalidate only their own stale metadata on
# retry, and never reveal raw module exceptions or crash on timeouts.
$advancedPolicy = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/AdvancedOemCatalogQueryPolicy.cs') -Raw
$dellStart = $windowCode.IndexOf('private async void ReadDellDriverCatalogButton_Click(')
$lenovoStart = $windowCode.IndexOf('private async void ReadLenovoDriverCatalogButton_Click(', $dellStart)
$hpStart478 = $windowCode.IndexOf('private async void ReadHpSoftpaqCatalogButton_Click(', $lenovoStart)
if ($dellStart -lt 0 -or $lenovoStart -le $dellStart -or $hpStart478 -le $lenovoStart) {
    throw 'Advanced Dell and Lenovo handlers are missing.'
}
$dellBody478 = $windowCode.Substring($dellStart, $lenovoStart - $dellStart)
$lenovoBody478 = $windowCode.Substring($lenovoStart, $hpStart478 - $lenovoStart)
if (-not $advancedPolicy.Contains('SystemOemCatalog.IsDellManufacturer(') -or
    -not $advancedPolicy.Contains('SystemOemCatalog.IsLenovoManufacturer(') -or
    -not $advancedPolicy.Contains('SystemIdentityNormalizer.Clean(') -or
    -not $advancedPolicy.Contains('provider.IsApplicable') -or
    -not $dellBody478.Contains('AdvancedOemCatalogQueryPolicy.TryGetDellModel(') -or
    -not $dellBody478.Contains('ReplaceUnifiedOem(DiscoveryResultKind.DellDeploymentPack, []);') -or
    -not $dellBody478.Contains('AdvancedOemCatalogQueryPolicy.DellUnavailableMessage') -or
    -not $dellBody478.Contains('dellModel, timeout.Token') -or
    -not $lenovoBody478.Contains('AdvancedOemCatalogQueryPolicy.CanQueryLenovo(') -or
    -not $lenovoBody478.Contains('ReplaceUnifiedOem(DiscoveryResultKind.LenovoCatalogDriver, []);') -or
    -not $lenovoBody478.Contains('AdvancedOemCatalogQueryPolicy.LenovoUnavailableMessage') -or
    -not $lenovoBody478.Contains('OperationCanceledException') -or
    $dellBody478.Contains('exception.Message') -or
    $lenovoBody478.Contains('exception.Message')) {
    throw 'Advanced Dell/Lenovo query must validate provider, remove stale evidence, and redact module errors.'
}

# #477: advanced HP CMSL must share the strict WMI identity gate
# with Home/HPIA and must never emit PowerShell exception details.
$hpPolicy = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/HpAdvancedCatalogQueryPolicy.cs') -Raw
$hpStart = $windowCode.IndexOf('private async void ReadHpSoftpaqCatalogButton_Click(')
$hpEnd = $windowCode.IndexOf('private void OpenSurfaceDownloadCenterButton_Click', $hpStart)
if ($hpStart -lt 0 -or $hpEnd -le $hpStart) {
    throw 'Cannot locate advanced HP CMSL click handler.'
}
$hpBody = $windowCode.Substring($hpStart, $hpEnd - $hpStart)
if (-not $hpPolicy.Contains('SystemOemCatalog.IsHpManufacturer(') -or
    -not $hpPolicy.Contains('provider.IsApplicable') -or
    -not $hpBody.Contains('HpAdvancedCatalogQueryPolicy.CanQuery(provider)') -or
    -not $hpBody.Contains('ReplaceUnifiedOem(DiscoveryResultKind.HpSoftpaqDriver, []);') -or
    -not $hpBody.Contains('HpAdvancedCatalogQueryPolicy.UnavailableMessage') -or
    -not $hpBody.Contains('OperationCanceledException') -or
    $hpBody.Contains('SystemManufacturer?.Contains("HP"') -or
    $hpBody.Contains('exception.Message')) {
    throw 'Advanced HP catalog must verify exact OEM identity, clear stale evidence, and redact timeout/PowerShell errors.'
}

# #476: every OEM Home button and advanced Lenovo/Surface handoff
# must use the same verified raw manufacturer/model identity as Core.
$systemOemMatcher = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/SystemOemCatalog.cs') -Raw
$automaticProbe = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/App2.Desktop/Services/SupportedCatalogProbe.cs') -Raw
if (-not $systemOemMatcher.Contains('public static bool IsLenovoManufacturer(') -or
    -not $systemOemMatcher.Contains('public static bool IsMicrosoftSurfacePc(') -or
    -not $systemOemMatcher.Contains('(char.IsWhiteSpace(ch) && ch !=') -or
    -not $windowCode.Contains('SystemOemCatalog.IsLenovoManufacturer(') -or
    -not $windowCode.Contains('SystemOemCatalog.IsMicrosoftSurfacePc(') -or
    -not $automaticProbe.Contains('SystemOemCatalog.IsLenovoManufacturer(manufacturer)') -or
    $windowCode.Contains('SystemManufacturer?.Contains("Lenovo"') -or
    $windowCode.Contains('SystemManufacturer?.Contains("Microsoft"') -or
    $windowCode.Contains('SystemModel?.Contains("Surface"') -or
    $systemOemMatcher.Contains('Contains(value, "Lenovo")') -or
    $systemOemMatcher.Contains('Contains(model, "Surface")')) {
    throw 'Lenovo/Surface and other OEM routing must be anchored to strict Core identities.'
}

# #475: OEM system identity is untrusted WMI/SMBIOS text. Automatic
# Dell/HP routing and opt-in buttons use the same strict Core boundary,
# and the probe never echoes raw manufacturer/model/exception details.
$catalogProbe = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/App2.Desktop/Services/SupportedCatalogProbe.cs') -Raw
$systemOemIdentity = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/SystemOemCatalog.cs') -Raw
$catalogDisplay = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/AutomaticOemCatalogDisplayText.cs') -Raw
if (-not $catalogProbe.Contains('SystemOemCatalog.IsDellManufacturer(manufacturer)') -or
    -not $catalogProbe.Contains('SystemOemCatalog.IsHpManufacturer(manufacturer)') -or
    -not $catalogProbe.Contains('AutomaticOemCatalogDisplayText.DellCompleted(') -or
    -not $catalogProbe.Contains('AutomaticOemCatalogDisplayText.HpCompleted(') -or
    -not $catalogProbe.Contains('AutomaticOemCatalogDisplayText.UnknownProducer') -or
    -not $catalogProbe.Contains('AutomaticOemCatalogDisplayText.QueryFailed') -or
    -not $windowCode.Contains('SystemOemCatalog.IsDellManufacturer(') -or
    -not $windowCode.Contains('SystemOemCatalog.IsHpManufacturer(') -or
    -not $systemOemIdentity.Contains('HasManufacturerPrefix(') -or
    $catalogProbe.Contains("'{model}'") -or
    $catalogProbe.Contains("'{manufacturer}'") -or
    $catalogProbe.Contains('Catalogo OEM {manufacturer}') -or
    $windowCode.Contains("'{provider.SystemModel}'") -or
    $windowCode.Contains('provider.SystemManufacturer?.Contains("Dell"') -or
    $windowCode.Contains('IsHpPcManufacturer(') -or
    $catalogDisplay.Contains('string manufacturer') -or
    $catalogDisplay.Contains('string model')) {
    throw 'OEM catalog statuses and actions must enforce strict Dell/HP identity and hide raw WMI strings.'
}

# #470: a failed Dell/HP catalog query is not a successful OEM
# provider discovery. A visible, opt-in retry must use only the official
# read-only catalog probe, not rerun WUA or install a driver.
$catalogRetry = Require-Node "//w:Button[@x:Name='RetryOemCatalogButton']" 'Dell/HP catalog retry action missing'
if ($catalogRetry.GetAttribute('Visibility') -ne 'Collapsed' -or
    $catalogRetry.GetAttribute('Click') -ne 'RetryOemCatalogButton_Click' -or
    $catalogRetry.GetAttribute('AutomationProperties.Name') -notmatch 'catalogo ufficiale' -or
    $null -eq $catalogRetry.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorResultsExpander"]', $namespaces)) {
    throw 'Dell/HP retry must be accessible and hidden until an OEM catalog failure.'
}
$probeCode = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/App2.Desktop/Services/SupportedCatalogProbe.cs') -Raw
$catalogCoverage = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/AutomaticOemCatalogCoverage.cs') -Raw
if (-not $probeCode.Contains('bool IsVerificationIncomplete = false') -or
    -not $probeCode.Contains('bool CanRetry = false') -or
    -not $probeCode.Contains('IsVerificationIncomplete: true') -or
    -not $probeCode.Contains('CanRetry: true') -or
    -not $catalogCoverage.Contains('WithOutcome(') -or
    -not $windowCode.Contains('if (vendorReport.IsVerificationIncomplete)') -or
    -not $windowCode.Contains('unavailable.Add(AutomaticOemCatalogCoverage.UnavailableStep)') -or
    -not $windowCode.Contains('RetryOemCatalogButton.Visibility = vendorReport.CanRetry') -or
    -not $windowCode.Contains('unavailable.Contains(AutomaticOemCatalogCoverage.UnavailableStep)') -or
    -not $windowCode.Contains('RetryOemCatalogButton.IsEnabled =')) {
    throw 'An unavailable automatic Dell/HP catalog must mark Home as partial and expose a retry.'
}
$retryStart = $windowCode.IndexOf('private async void RetryOemCatalogButton_Click')
$retryEnd = $windowCode.IndexOf('private void RefreshVendorResultsState()', $retryStart)
if ($retryStart -lt 0 -or $retryEnd -le $retryStart) {
    throw 'Dell/HP retry handler cannot be located.'
}
$retryBody = $windowCode.Substring($retryStart, $retryEnd - $retryStart)
# #473: IsCatalogMetadata is ALSO true for DCU/HPIA recommendations.
# Retry must use the strict identity of the *automatic* CMSL/Driver Pack
# producer, never a vendor-wide removal that erases applicable OEM results.
$catalogReplacementCore = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/AutomaticOemCatalogRowReplacement.cs') -Raw
if (-not $catalogReplacementCore.Contains('Dell Driver Pack Catalog') -or
    -not $catalogReplacementCore.Contains('HP Client Management Script Library') -or
    -not $catalogReplacementCore.Contains('!row.IsVendorApplicable') -or
    -not $catalogReplacementCore.Contains('!row.IsReleaseAnnouncement') -or
    -not $catalogReplacementCore.Contains('row.SourceName') -or
    -not $catalogReplacementCore.Contains('row.Vendor')) {
    throw 'Automatic Dell/HP retry must distinguish catalog source from opt-in DCU/HPIA recommendations.'
}
if (-not $retryBody.Contains('SupportedCatalogProbe.TryReadAsync(') -or
    -not $retryBody.Contains('AutomaticOemCatalogCoverage.WithOutcome(') -or
    -not $retryBody.Contains('AutomaticOemCatalogRowReplacement.ForRetry(') -or
    -not $retryBody.Contains('UpdateSimpleScanSummary();') -or
    $retryBody.Contains('SearchUpdatesCoreAsync(') -or
    $retryBody.Contains('InstallUpdatesCoreAsync(') -or
    $retryBody.Contains('DriverUpdates.Clear(') -or
    $retryBody.Contains('Drivers.Clear(')) {
    throw 'Dell/HP retry must replace only OEM catalog metadata and recompute Home coverage without WUA/install.'
}

# #471: the COPYABLE diagnostic report must track the latest structured
# Dell/HP metadata outcome (including retry) rather than quietly treating
# a catalog failure as "no OEM drivers". Never export raw system model,
# SoftPaq title, OEM URL or process exception to QA copy.
$copyCode = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Shared.Core/Drivers/ScanCoverageText.cs') -Raw
if (-not $probeCode.Contains('bool WasQueried = false') -or
    -not $probeCode.Contains('AutomaticOemCatalogCoverage.Classify(') -or
    -not $probeCode.Contains('WasQueried: true') -or
    -not $catalogCoverage.Contains('enum CatalogQueryStatus') -or
    -not $catalogCoverage.Contains('StatusForAnonymousReport(') -or
    -not $copyCode.Contains('automaticOemCatalogStatus') -or
    -not $copyCode.Contains('AutomaticOemCatalogCoverage.StatusForAnonymousReport(') -or
    -not $windowCode.Contains('_lastAutomaticOemCatalogStatus = vendorReport.AuditStatus;') -or
    -not $windowCode.Contains('_lastAutomaticOemCatalogStatus = result.AuditStatus;') -or
    -not $windowCode.Contains('automaticOemCatalogStatus: _lastAutomaticOemCatalogStatus') -or
    -not $windowCode.Contains('CatalogQueryStatus.NotApplicable;')) {
    throw 'QA copy must contain the current anonymous Dell/HP catalog outcome including retry.'
}
if ($windowCode.Contains('automaticOemCatalogStatus: VendorCatalogStatusText.Text') -or
    $copyCode.Contains('VendorCatalogStatusText.Text')) {
    throw 'QA copy may not export OEM catalog free text, system model or error output.'
}

# Security regression: the legacy detailed WUA tab must never bypass the
# compact quick-update eligibility gate, even for Select All or forged UI IDs.
$installSelected = $windowCode.IndexOf('private async void InstallSelectedButton_Click')
$installDevice = $windowCode.IndexOf('private async void InstallDeviceUpdateButton_Click')
$installCore = $windowCode.IndexOf('private async Task InstallUpdatesCoreAsync')
$selectAll = $windowCode.IndexOf('private void SelectAllUpdatesButton_Click')
$selectNone = $windowCode.IndexOf('private void DeselectAllUpdatesButton_Click')
if ($installSelected -lt 0 -or $installDevice -lt $installSelected -or
    $installCore -lt $installDevice -or $selectAll -lt 0 -or
    $selectNone -lt $selectAll) {
    throw 'Unable to locate all Windows driver install entry points.'
}
if ($windowCode.Substring($installSelected, $installDevice - $installSelected) -notmatch 'CurrentQuickInstallIds\(' -or
    $windowCode.Substring($installDevice, $installCore - $installDevice) -notmatch 'CurrentDeviceQuickInstallIds\(' -or
    $windowCode.Substring($installCore, 2000) -notmatch 'CompactInstallActionPolicy.TryAuthorizeRequested\(' -or
    $windowCode.Substring($selectAll, $selectNone - $selectAll) -notmatch 'update.CanQuickInstall') {
    throw 'Detailed WUA install, device install or Select All bypasses the canonical full-report quick-install policy.'
}
$selectableCode = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/App2.Desktop/Models/SelectableDriverUpdate.cs') -Raw
if ($selectableCode -notmatch 'value && CanQuickInstall') {
    throw 'Non-installable updates must never be selectable through data binding.'
}
$serviceCode = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/App1.Service/Services/WindowsUpdateDriverProvider.cs') -Raw
if ($serviceCode -notmatch 'searcher.Search\(WuaInstallRecheckPolicy.Criteria\)' -or
    $serviceCode -notmatch 'WuaInstallRecheckPolicy.CanStage\(' -or
    $serviceCode -notmatch 'WuaInstallRecheckPolicy.IncludePotentiallySuperseded') {
    throw 'Privileged WUA installation must recheck only non-hidden, non-superseded ordinary driver offers.'
}
if ($windowCode -notmatch 'DeviceWuaMatchGroups.Prepare\(' -or
    $windowCode -notmatch 'prepared.ForDevice\(driver.Driver\)' -or
    $windowCode -notmatch 'driver\.SetMatchedUpdates\(\s*matches\.Ordinary,\s*matches\.ReviewOnly,\s*matches\.ModelOnlyReview,\s*approvedSourceGuids\)' -or
    $windowCode -notmatch '!QuickUpdatePolicy.IsEligible\(preferred\)' -or
    $windowCode -notmatch 'return !QuickUpdatePolicy.IsEligible\(update.Update\);') {
    throw 'Device matching must show review-only WUA matches but gate per-device installation via QuickUpdatePolicy.'
}
# Model-only WUA evidence is a THIRD, informational group. Validate that
# the selected device cannot promote that GUID to ordinary installation.
if ($windowCode -match 'matches\.ModelOnlyReview' -and
    ($windowCode -notmatch 'driver\.SetMatchedUpdates\(\s*matches\.Ordinary,\s*matches\.ReviewOnly,\s*matches\.ModelOnlyReview,\s*approvedSourceGuids\)' -or
     $windowCode -match 'QuickUpdatePolicy\.AllOrdinaryIds\(matches\.ModelOnlyReview\)')) {
    throw 'Model-only WUA associations must stay review-only on the device tab.'
}
if ($windowCode -notmatch 'WuaPartialDriverHintCollector' -or
    $windowCode -notmatch 'ConfirmedForReport\(searchReport\)' -or
    $windowCode -notmatch 'PartialWuaDriverHints.Clear\(\)' -or
    $windowCode -notmatch 'PartialWuaHintsExpander.Visibility' -or
    $windowCode -match 'AddDriverUpdate\(hint\)') {
    throw 'Partial WUA results must be reset on each scan and shown read-only, never installed.'
}
if ($windowCode -notmatch 'MicrosoftUpdateCatalogSearch.Create\(selected.Driver\)' -or
    $windowCode -notmatch 'MessageBoxButton.YesNo' -or
    $windowCode -notmatch 'target.Url.Host' -or
    $windowCode -notmatch 'target.Url.AbsoluteUri') {
    throw 'Catalog browsing must create a model-only Microsoft URL, check its host and ask for consent.'
}
if ($windowCode -notmatch 'HomeScanProgressBar.Visibility = Visibility.Visible' -or
    $windowCode -notmatch 'HomeScanProgressBar.Visibility = Visibility.Collapsed' -or
    $windowCode -notmatch 'CompactEmptyStatePanel.Visibility = visible == 0') {
    throw 'Visual refresh: live progress and empty state must be synchronized by code-behind.'
}

# Compact list navigation #142: local search, OEM space and visible-only
# selection. These static checks supplement xUnit and real Windows UI QA.
$search = Require-Node "//w:TextBox[@x:Name='CompactSearchTextBox']" 'compact result search missing'
if ($search.GetAttribute('TextChanged') -ne 'CompactSearchTextBox_TextChanged' -or
    $search.GetAttribute('AutomationProperties.Name') -notmatch 'Cerca') {
    throw 'Compact results: search must be accessible and wired to the live result filter.'
}
$searchParent = $search.SelectSingleNode('ancestor::w:Border[ancestor::w:Grid[@x:Name="HomeResultsPanel"]][1]', $namespaces)
if ($null -eq $searchParent -or $searchParent.GetAttribute('Grid.Row') -ne '1') {
    throw 'Compact results: search must stay next to the WUA kind filter.'
}
$vendorPanel = Require-Node "//w:Expander[@x:Name='VendorResultsExpander']" 'collapsible OEM results area missing'
if ($vendorPanel.GetAttribute('Grid.Row') -ne '3' -or
    $vendorPanel.GetAttribute('IsExpanded') -ne 'False' -or
    $null -eq $verified.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorResultsExpander"]', $namespaces) -or
    $null -eq $localFolder.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorResultsExpander"]', $namespaces)) {
    throw 'Compact results: OEM tools and recommendations must be under the collapsed source section.'
}

$selected = Require-Node "//w:Button[@x:Name='CompactUpdateSelectedButton']" 'selected update action missing'
if ($selected.GetAttribute('ToolTip') -notmatch 'visibili') {
    throw 'Compact results: selected update action must explain visible-only semantics.'
}
$all = Require-Node "//w:Button[@x:Name='CompactUpdateAllButton']" 'update all action missing'
if ($all.GetAttribute('ToolTip') -notmatch 'nascosti dal filtro') {
    throw 'Compact results: update all must disclose that it operates independently of filter visibility.'
}
if ($windowCode -notmatch 'CompactResultSearch.Matches' -or
    $windowCode -notmatch 'CompactSearchTextBox.Clear\(\)' -or
    $windowCode -notmatch 'CompactUpdatesView.Cast<SelectableDriverUpdate>\(\)' -or
    $windowCode -notmatch 'VendorResultsExpander.IsExpanded =') {
    throw 'Compact results: search, reset, visible selected updates and OEM auto-expand must be wired.'
}

# #149: Eight-category diagnostic evidence stays informational,
# behind both the OEM results expander and its own opt-in section.
$referenceExpander = Require-Node "//w:Expander[@x:Name='ReferenceCoverageExpander']" 'eight-component source coverage missing'
if ($referenceExpander.GetAttribute('Visibility') -ne 'Collapsed' -or
    $referenceExpander.GetAttribute('IsExpanded') -ne 'False' -or
    $referenceExpander.GetAttribute('Header') -notmatch 'NVIDIA' -or
    $null -eq $referenceExpander.SelectSingleNode('ancestor::w:Expander[@x:Name="VendorResultsExpander"]', $namespaces)) {
    throw 'Reference source coverage: must be opt-in inside the non-installable OEM source section.'
}
$referenceRows = Require-Node "//w:ItemsControl[@x:Name='ReferenceCoverageList']" 'per-component diagnosis list missing'
if ($referenceRows.GetAttribute('ItemsSource') -ne '{Binding ReferenceCoverageRows}' -or
    $null -eq $referenceRows.SelectSingleNode('ancestor::w:Expander[@x:Name="ReferenceCoverageExpander"]', $namespaces)) {
    throw 'Reference source coverage: independent informational rows must bind to fixed-category diagnostics.'
}
foreach ($field in @('Component', 'PnpStatus', 'WindowsDriverStatus', 'SoftwareStatus', 'NextStep')) {
    $candidate = $referenceRows.SelectSingleNode(".//w:TextBlock[@Text='{Binding $field}']", $namespaces)
    if ($null -eq $candidate) {
        throw "Reference source coverage: missing visual explanation for $field."
    }
}
if ($windowCode -notmatch 'ReferenceCoveragePresentation.Create' -or
    $windowCode -notmatch 'ReferenceCoverageRows.Clear\(\)' -or
    $windowCode -notmatch 'RefreshReferenceCoverage\(\)' -or
    $null -ne $referenceRows.SelectSingleNode('ancestor::w:ListView[@x:Name="CompactUpdatesList"]', $namespaces)) {
    throw 'Reference source coverage: reset, refresh and WUA installation lists must remain separate.'
}

# The main installed/selected WUA list and notifications must be gated by
# the SAME completed-source provenance as the anonymous QA/coverage report.
# Partial or legacy IPC snapshots must never bypass this presentation gate.
if ($windowCode -notmatch 'var updates =\s*WindowsDriverCandidateMerge\.ConfirmedDistinctForReport\(searchReport\);' -or
    $windowCode -notmatch '_hasSearchedUpdates\s*=\s*WindowsDriverCandidateMerge\.HasCompletedKnownWuaPass\(searchReport\);' -or
    $windowCode -match 'var updates = searchReport\.Updates;') {
    throw 'Main WUA update list/notifications must require each source to have completed.'
}

# All result surfaces must use the same distinct, source-verified GUIDs,
# including Home's headline and the list used by Update All.
if ($windowCode -notmatch 'var updates =\s*WindowsDriverCandidateMerge\.ConfirmedDistinctForReport\(searchReport\);' -or
    $windowCode -match 'var updates = searchReport\.Updates;') {
    throw 'Main WUA list must use distinct completed-source GUIDs.'
}

# Software title hints are informational, but must come only from
# their OWN completed Type=Software pass. A raw legacy IPC hint from
# a failed source must not be displayed as a found component.
if ($windowCode -notmatch 'foreach \(var related in WuaDriverRelatedSoftwareClassifier\s*\.ConfirmedForReport\(searchReport\)\)' -or
    $windowCode -match 'foreach \(var related in searchReport\.SoftwareReviewItems\)') {
    throw 'Software review panel must require completed WUA Type=Software source.'
}

Write-Host 'Guided results UI static contract: passed.'
